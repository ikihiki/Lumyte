# 2D描画リストの部分更新アーキテクチャ（検討案）

- 状態: 検討案・未採用・未実装
- 日付: 2026-11-02
- 調査基準: main `fbc8105`、コピー実装の比較対象はPR #105 `ffe075c`
- 対象: CPU記録、UI接続、CPU/GPU準備、RenderGraph連携、フレーム間の所有権

これは設計レビュー用の提案であり、既存ADRの採用事項やAPIを変更しない。合意後に[ADR運用方針](../adr/0001-adr-writing-policy.md)に従って正式ADRへ分割・採番し、採用してから実装する。候補APIと性能目標は実装済み・測定済みではない。

dense/full更新のbulk構築とGPU転送選択は、別PRのGRAPHICS-0026で提案する。本PRはそのbuilder/APIを含めず、保持内容と差分公開の共通境界に絞る。

PRレビュー用の主要判断は[GRAPHICS-0025](../adr/graphics/GRAPHICS-0025-retained-drawing-content.md)へ整理した（状態は提案）。本書は詳細仕様・測定計画を補足する。

## 結論

**不変の描画fragmentを保持するsceneと、変更部分だけを公開するtransactionを導入する。描画順とproperty scopeを構造として保持し、毎frameに全commandをReplay・連結・固定化する方式を避ける。**

CPUの保持scene、deviceごとの再利用可能な描画資産、毎frameのone-shot実行ownerを分離する。GPU準備の再利用と、画面のdirty rectangle再描画は別の最適化である。初期段階では画面全体を毎frame描いてもよい。

コピーの完全排除自体を目標にしない。必要な境界でのコピーと、表現の都合による毎frameの全体コピーを分け、変更しないデータを作り直さないことを優先する。

## 1. 現状と問題の境界

- [DrawingCanvas2D.CompleteRecording/Reset](../../src/Graphics/Lumyte.Graphics.Drawing2D/Model/DrawingCanvas2D.cs)はmutableな記録ListからDrawListを作り、CanvasのListをclearして再利用する。現在の表現のままListを共有すると、完成済み記録の不変性が壊れる。
- [DrawingCanvas2D.Replay](../../src/Graphics/Lumyte.Graphics.Drawing2D/Model/DrawingCanvas2D.cs)は参照をsceneへ追加する操作ではない。全commandを走査し、transform/clipを合成した新しいcommand recordを記録先へ追加する。
- [DrawList2D](../../src/Graphics/Lumyte.Graphics.Drawing2D/Model/DrawList2D.cs)は描画順配列・型別配列・型別offsetを保持する。#105は一時Listの拡張と再コピーを減らすが、scene全体を更新し直す構造は変えない。
- [Renderer2D.PrepareAsync](../../src/Graphics/Lumyte.Graphics.Drawing2D/Execution/Renderer2D.cs)はリストを順に走査し、同型・同clipの連続batchを毎回prepareする。
- [PreparedDraw2D](../../src/Graphics/Lumyte.Graphics.Drawing2D/Execution/PreparedDraw2D.cs)は一回だけRecordできるownerである。これをcacheして繰り返しRecordする案は現契約に反する。
- UIにはElement実体の永続化・Signal部分木更新・layout cacheがある。一方、一般のElement単位のOnRecord cacheはまだない。[標準hostのcache](../../src/UI/Lumyte.UI.Application/UiHostedDrawingSource.Recording.cs)はresource-freeなPanel/Stack/Rectangleの静止frame全体が対象で、animation/presence/transition等では利用できない。[UiVisualLayer](../../src/UI/Lumyte.UI.Application/UiVisualLayer.cs)は別途、遷移subtreeのidentity/revision/sizeに基づく記録を保持する。これらを一般の局所更新と混同しない。

[既存のWindows UI詳細調査](../performance/ui-device-deep-profile.md)では、測定対象の1要素変更でもroot描画走査と固定化が発生し、静止時にも矩形packが残った。一方、その調査の最大の固定費はsurface能力照会である。これは当該revision・workloadの結果であり、本提案の速度向上や現在の支配的負荷を証明する数字ではない。新設計の採否は工程別に再計測する。

## 2. 三つの「部分更新」を分離する

1. **CPU記録の部分更新**: 変更された論理単位のfragmentだけを再記録・置換する。
2. **描画準備の部分更新**: 変わらないgeometry、paint解析、GPU payloadを保持し、必要な項目だけを再準備する。
3. **画面の部分再描画**: 前の画像を保持し、damage領域だけを復元・描画する。

1と2は、targetをClearして全体を描く実装でも有効である。3には画像の保存、消去、重なり、filter/backdrop依存の処理が追加で必要になる。swapchain画像が保持されることを暗黙に仮定しない。

## 3. 推奨データモデル

```text
UI / 任意の2Dアプリ
  ├─ 変わった要素だけ local fragment を記録
  └─ scene transaction: content / transform / clip / order を更新
                   ↓ atomic publish
       immutable SceneSnapshot（rootと版を保持）
         ├─ ordered group / fragment reference tree
         ├─ transform / clip scope
         └─ immutable DrawList2D fragments + external resource dependencies
                   ↓ deviceと資源世代で解決
       cached render plan / compiled payloads
                   ↓ fresh frame lease
       PreparedDraw2D → Record once → Submit → GPU完了 → retire
```

### 論理単位と物理単位を同一にしない

- **論理fragment**: 独立して再記録できる単位。UIではElementの自己描画等、一般アプリでは図形・文字run・地図tile等。初期版は既存DrawList2Dを不変payloadとして参照できる。
- **property scope**: fragmentの内容と、配置transform・外側clipを別に保持する。fragment内のclipは記録時のtransformを保持し、sceneの外側clipは定義したscopeの座標に属する。子のtransform変更で親clipを動かさない。scope自体を移動する場合はclipを含むscope全体が移動する。
- **描画順tree/recipe**: self描画とchild/slot参照を任意に交互配置できる順序付きsequenceを保持する。Children順や「self→children」固定ではない。[ButtonElement](../../src/UI/Lumyte.UI/ButtonElement.Drawing.cs)もbackground→self→contentの順を使う。透明描画の順を型・paint・texture順に全体sortしない。
- **payloadと配置instance**: 一つの不変fragmentを複数の位置で共有できる。fragment identityとscene occurrenceのnode IDを分離し、同じpayloadを使った二つの配置のtransform/clip/orderは独立に更新する。
- **GPU格納page/run**: batchをまとめる物理単位。1 fragment = 1 GPU drawとはしない。

巨大な1 fragment内の1 command変更は、初期APIではそのfragmentの再記録になる。commandごとの最小更新を必ず保証するとはしない。利用者が適切に分割できること、UI adapterが過大なroot fragmentを作らないことが重要である。

### identityとrevision

- SceneのIDと世代付きnode IDを使い、別scene・削除済みID・再利用された古いIDを拒否する。
- indexをidentityにしない。挿入・削除・reorderで無関係なnodeのIDを変更しない。
- Content、Properties、Structure/Order、外部依存の変更を区別する。単一のdirty boolで全体cacheを無効化しない。
- 同一fragmentの参照が同じでも、外部texture内容や資源世代の変更は別の依存revisionとして扱う。
- 更新操作のIDはtransactionの版に対して検証する。Removeやslot再利用でlive側の世代が進んでも、旧snapshot内の参照はcaptureした版に対して解決する。

### persistentなsnapshot

変更していない部分木・fragmentは新旧snapshotで共有する。更新は変更leafとその経路だけをcopy-on-writeし、child列の編集に全sceneの配列cloneを使わない。persistent tree/rope等の選択、page容量は測定で決める。

no-opのCommitは内容revisionを増やさず、同じrootを使う。Captureはroot lease取得に留め、命令のflattenや全体hash走査をしない。短命wrapper等の一定量の割当までゼロと保証するものではない。

## 4. コピーと所有権

### 保持すべき不変条件

- 公開済みsnapshotから見えるcommand、順序、property値を後の更新で変更しない。
- Canvas再Begin、別transaction、後続frameが旧snapshotの記録を壊さない。
- ReadOnlyMemoryやread-only wrapperだけでmutableなbacking bufferをfreezeしたことにしない。

### 消せるコピーと残すコピー

- Canvasが専有している新規記録bufferは、Endで切り離して不変payloadへ所有権を渡せる。Canvasは別bufferで次の記録を開始する。
- 型別格納と描画順tapeへ最初から記録すれば、Endで全commandを型別に再振り分けする必然性はない。
- 既存の外部mutable入力を受け取る経路は、防御的コピーを残すか、別の明示的な所有権移譲APIを使う。内部bufferのdetachと外部配列の無断共有を混同しない。
- COWの変更page、容量整理、CPU→GPU転送は残り得る。page単位コピーを許しても、毎frameの全sceneコピーへは戻さない。

detachするとbufferの余剰capacityをsnapshotが保持する。copy回数だけでなく、保持byte数・GC/LOH・fragment数・pool上限を測る。初期版では既存DrawListのCPU配列をGC所有にしてよく、寿命を追跡できない配列をArrayPoolへ返す最適化は行わない。

managed objectへの強参照は、借用native texture/viewがDisposeされない保証ではない。基本経路は既存のborrowed契約を維持し、呼び出し側がそのsnapshotの将来の使用と全GPU使用を終えるまで生存を保証する。CPU snapshotが残ることだけでnative資源まで使用可能とは扱わない。

Resources adapterがowned leaseを供給する経路では、正確な資源世代のtokenをcompiled asset/frameから共有し、最後の使用後に返す。CPU snapshotがGPU cache全体を永久にpinすることは要求せず、未使用資産はevictして再prepareできる。任意のIDisposableを二重所有する独自の資源管理系は作らない。

## 5. 更新と公開の契約

1. Sceneはsingle-writer。BeginUpdateで編集transactionを開始する。
2. 変更fragmentは未公開bufferへ記録する。ID・順序・数値・scope整合性を検証する。
3. Commitで新rootを一度に公開する。検証失敗・取消・Commit前のDisposeでは旧rootを維持する。
4. rendererは一つのsnapshotを読む。部分更新中の新旧内容を混在させない。
5. 古いsnapshotのCPU記録は後続Commit後も使用できる。親削除後の子やRemove後の内容も、旧snapshotが必要なら生存する。借用native資源の生存は前節の別契約である。

Capture/Commit/Dispose間のroot参照取得は小さなgate等で安全に同期し、rootを読んだ直後に最後のleaseが失効する競合を防ぐ。独立したCPU fragment記録は個別builderで行えるが、一つのCanvasを並列操作しない。Rendererの並列Prepare制限も勝手に緩めない。

scene更新のatomic性は、任意の外部I/Oや任意のcustom GPU実装の成功まで保証しない。GPUで未対応の内容はPrepareで明示的に失敗させる。必要ならアプリのpublication層がprepare成功後のsnapshotを表示対象へ採用する。

### API形状の候補

比較元main `fbc8105`に以下のscene APIは存在しない。これは責務を示す抜粋であり、名称・型・公開範囲を確定するものではない。

```diff
+namespace Lumyte.Graphics.Drawing2D
+{
+    // scene由来の世代付き不透明ID。defaultは無効。
+    public readonly struct DrawingNodeId2D;
+    public sealed class DrawingScene2D : IDisposable
+    {
+        public DrawingScene2D();
+        public DrawingNodeId2D Root { get; }
+        public DrawingSceneUpdate2D BeginUpdate();
+        // rootを保持するだけで命令を連結しない。
+        public DrawingSnapshot2D Capture();
+        // 新規更新/取得を閉じる。取得済みsnapshotを破壊しない。
+        public void Dispose();
+    }
+    public sealed class DrawingSceneUpdate2D : IDisposable
+    {
+        public DrawingNodeId2D AddGroup(DrawingNodeId2D parent, int index);
+        public DrawingNodeId2D Insert(DrawingNodeId2D parent, int index, DrawList2D content);
+        public void ReplaceContent(DrawingNodeId2D node, DrawList2D content);
+        public void SetTransform(DrawingNodeId2D node, System.Numerics.Matrix3x2 transform);
+        // scopeローカル座標のclip。nullはこのscopeのclip解除、親/fragment内clipは維持。
+        public void SetClip(DrawingNodeId2D node, Path2D? clip, FillRule2D rule = FillRule2D.NonZero);
+        public void Move(DrawingNodeId2D node, DrawingNodeId2D parent, int index);
+        public void Remove(DrawingNodeId2D node);
+        // atomic公開し、その版のsnapshotを返す。
+        public DrawingSnapshot2D Commit();
+        // 未Commitの編集は取り消す。
+        public void Dispose();
+    }
+    public sealed class DrawingSnapshot2D : IDisposable
+    {
+        public ulong Revision { get; }
+        public void Dispose();
+    }
+    public sealed class Renderer2D
+    {
+        // 既存rendererへ追加。CPU scene用に別の描画エンジンを作らない。
+        public ValueTask<PreparedDrawingFrame2D> PrepareAsync(
+            DrawingSnapshot2D snapshot, RenderTarget2D target,
+            DrawingContentRenderers2D renderers, CancellationToken cancellationToken = default);
+    }
+}
```

現行のClipStack2Dは読み取り用にはpublicだが構築操作がinternalであるため、利用側の編集入力にそのまま要求しない。sceneのclipは公開PathBuilder2Dで作れるPath2DとFillRuleから与え、stack/捕捉座標の解決を内部で行う。

RenderGraph layer/effectの編集APIはこの基本APIへ無理に入れず、既存Drawing2D.RenderGraph側の拡張として別途仕様化する。group opacityは各子のalpha乗算と同値ではなく、必要な隔離合成を省略しない。

## 6. GPU準備とframe寿命

### 三つの寿命

- **CPU snapshot**: 不変記録と外部依存の参照を保持する。GPUコマンドを所有しない。
- **compiled asset/plan**: device・資源世代に属するgeometry、解析済みpaint、GPU data等。複数frameからleaseできる。
- **frame prepared owner**: そのframeのtarget/binding/uploadとcompiled assetのleaseを保持し、Recordは一回だけ。既存PreparedDraw2Dの規則を維持する。

cacheするのは再利用可能な資産であり、消費済みPreparedDraw2D、command buffer、transientなRenderGraph handleではない。毎frameのuploadが必要なものと、upload済みでそのまま読めるものも分離する。

現在の[DrawingProgram2D](../../src/Graphics/Lumyte.Graphics.Drawing2D/Rendering/DrawingProgram2D.cs)の出力には、pixel transformだけでなくargument table固有のtexture/sampler参照も入る。CPU配列だからdevice非依存とは判断しない。再利用可能なCPU解析結果はsymbolic binding/relocationと分離し、物理参照へのmaterializeはdevice/table世代に属する成果物とする。既存のbound programをそのまま保持する段階では、その正確なtable/resource ownerも保持する。

標準shaderのshape・clip・paintを同じ4×4 subpixel点で評価する契約を維持する。cache導入を理由に、別々に平均したshape coverageとclip coverageを掛けて同じ結果だと扱わない。

### cache keyと無効化

- fragment/content revision、renderer登録・実装設定の版。
- geometry/style、paint、clipとその座標系、pixel-scale依存の許容誤差。
- device identity/runtime generation、pipeline/resource publication世代、必要なtarget format等。
- texture/atlas/view/sampler等の依存ID、view subresource、atlas UV/layout、binding・生存のrevision。textureのpixel内容revisionはraster/effect結果に重要だが、同じlive resourceをsamplingするだけならgeometry再compileを必ず要求するものではない。

target view・load/clear・サイズ等のframe stateを何でもgeometry keyへ入れない。ただしpixel-origin、DPI、scissor、tessellation toleranceが計算結果を変えるなら該当成果物は無効化する。pathのflatten/strokeを単に「transformはshaderで変更できる」として無条件再利用しない。

既存DrawListの画像/atlas commandは物理deviceのviewを借用できるため、CPU snapshot全体が自動でdevice-independentになるわけではない。device loss後はgeometry等を再利用できても、これらのbindingは再解決・置換が必要である。Resources連携は既存publication/leaseを利用し、基本Drawing2DにResourceManager依存を追加しない。

HotReloadで新publicationができても、旧世代leaseがまだ有効なら旧frameはその世代を使える。device lossでは物理資源が別epochへ移行できない。既存の[GraphicsResourceRuntime](../../src/Graphics/Lumyte.Resources.Graphics/GraphicsResourceRuntime.cs)と[ResourceLease](../../src/Core/Lumyte.Resources/ResourceLease.cs)の世代・退役契約を使い、cache独自のdevice-lost boolだけで置き換えない。

任意のcustom rendererにimmutableなcache契約があるとは仮定しない。既定では従来どおり毎frame準備する。再利用へのopt-inとrevision契約は後続ADRで定義し、現在の専用interfaceによる明示的dispatchを勝手なrenderer探索に置き換えない。

### in-flight保護

新revisionで、先行GPUが読んでいるbufferやargument tableを上書きしない。

- 初期の安全な方式は、変更pageだけ新しい物理領域を確保するCOW。旧pageは最後のframe leaseが完了するまで保持する。
- GPUで旧pageをコピーして差分uploadする場合、copy元もcopy完了まで保持し、必要なbarrierと依存を記録する。
- 後に複数frame slotを使う方式へ変えるなら、slotごとの適用済みrevisionとdirty履歴が必要。「今回の差分だけ」を各slotへ書くと、数frame古いslotの内容が欠落する。履歴不足時のfull refreshも定義する。
- Submit成功後のWait取消はGPU完了ではない。実際の完了または確立したdevice-loss cleanup条件まで退役を延期する。
- 未送信のRecord失敗はcommandを先に破棄してからleaseを返す。cache evictionやScene/Renderer.Disposeも使用中資源を即解放しない。

GPU cacheはbyte/page予算付きLRU等とし、evictionでcacheの参照を外しても、使用中資源の破棄/再利用は最後のlease完了まで延期する。CPU解析cacheとGPU資産cacheの予算、oversized itemのbypass、pin中byte数、dirty履歴の上限を別に持つ。frame数を上限付きにし、保留frameが増えた場合はbackpressureをかける。利用側が保持する大量のCPU snapshotまで無限に安価とはしない。

### upload公開と退役の状態を区別する

`CPUCompiled → UploadRecorded → Submitted(handle) → Ready`を区別する。現在の[rectangle pool](../../src/Graphics/Lumyte.Graphics.Drawing2D/Rendering/PreparedRectangleBatch2D.cs)の`Entry.Used`はRecord時に立ち、Submit成功やupload実行の証拠ではない。単にこのフラグを見て次frameでuploadを省略してはならない。

未送信commandの破棄やSubmit拒否ではresident revisionを進めない。初期版は完了確認後だけReadyにする。upload完了前に別frameから共有する最適化を行うなら、確立したsubmission/queue/semaphore依存も保持する。Submitの成否が不明な失敗やdevice lossは資源を隔離し、既存runtimeのepoch退役条件が満たされるまで再利用しない。

基本的なpage cache、buffer copy/barrier、submission handleによる退役queueは既存Graphics backend APIで構築できる。今回のためにsecondary command buffer、timeline semaphore等を新しい必須能力にしない。

## 7. fragment境界でbatch性能を壊さない

UIの1要素1fragmentを、そのまま1 drawへ変換すると、従来まとめられた1,000矩形が1,000 drawになる危険がある。論理更新単位とGPU batch/pageを分離することを受け入れ条件にする。

初期版は互換性のある**連続run**を固定容量pageへ配置し、fragmentのcommandを安定slotへ対応づける。painter-orderのrange/index列を別に持ち、必要な境界だけを再構築する。並べ替え時にgeometry payloadまで無条件移動しない。

- CPU command objectを全frame連結して型別ReadOnlyMemoryを作る方式は採らない。
- 既存のtyped-memory interfaceは互換経路として維持できるが、保持型の既定fast pathにはpage/rangeを消費する内部表現が必要になる。
- 色等の更新は小さい属性領域、geometry変更は該当payload、reorderは順序構造を更新する。
- pageのCOWで全page uploadが必要な初期実装なら、そのbyte数を正直に計上する。「1 command更新だから1 command分しか転送しない」とは言わない。
- 親transform/clip変更が子の解決済み属性を変える場合、初期版はその部分木の再解決を許す。完全なproperty indirection/GPU側合成を別途測定し、親変更が常にO(1)とは保証しない。

## 8. 更新の種類ごとの扱い

- **局所の色/paint変更**: 対応fragment/property revisionを更新。geometryが同じでも、現在のcompilerが両者を融合しているならまず成果物を分離する。
- **path/stroke/text layout変更**: そのgeometry・境界・GPU payloadを無効化。shapingはText側の責務を維持し、同じ文字列ならlayout変更がないとは仮定しない。
- **位置/transform変更**: fragmentのlocal記録を再実行しない。必要なworld-state・bounds・tolerance依存成果物を更新する。
- **clip/group opacity/filter/backdrop変更**: 構造scopeと合成依存を更新。group opacityとcommand単位alphaを取り違えない。
- **insert/remove/reorder**: subtree/順序/run境界を更新。削除された内容を旧snapshotから消さない。
- **外部texture内容変更**: geometry/descriptorを再利用できても再描画の必要性はある。変更通知/revisionがなければdynamic扱いにし、静止と断定しない。
- **resize/DPI/device/resource世代変更**: 対応する成果物・layer targetを無効化。native handleやswapchain imageはCPU記録と同じ寿命にしない。

## 9. UIとRenderGraphへの接続

UIは既存Elementのidentityとdirty分類をscene occurrenceへ対応づける。Widget全体比較や画素差分からdirtyを逆算する別engineは作らない。現在の粗いsurface/subtree visual revisionを、そのまま全fragmentのcontent revisionとして流用しない。

[UiSurface.Publish](../../src/UI/Lumyte.UI/UiSurface.Rebuild.cs)、[部分木更新](../../src/UI/Lumyte.UI/UiSurface.SubtreeUpdates.cs)、[layout公開](../../src/UI/Lumyte.UI/UiElement.Layout.cs)を差分採取の候補とする。layoutがdirtyでも、確定結果がoffsetだけ変わったのか、size依存の自己描画も変わったのかを比較する。offsetのみなら配置更新、size依存ならcontentも更新する。

`RecordSelf`/`RecordChild`を順序付きrecipeの捕捉境界とし、既定のbuilt-inからopt-inで始める。任意状態を読むcustom OnRecord/RecordOverrideは、明示invalidateや再利用契約がなければその範囲をvolatileとして毎frame記録する。既存のcustom描画順を変えない。

自己描画がdirtyなElementだけをlocal Canvasへ記録し、ReplaceContentする。layout、親子順、外側transform/clipの変更はそれぞれのscene操作へ渡す。UiSurfaceから一つの平坦なDrawListを毎frame作ることを必須にしない。

[UiVisualLayer](../../src/UI/Lumyte.UI.Application/UiVisualLayer.cs)とnavigationも同じfragment/sceneのadapterへ寄せる。ただし必要なraster layer cacheは維持し、[共有要素flight](../../src/UI/Lumyte.UI.Application/UiPageVisualFlight.cs)の通常pageからの除外・後段合成、resize/DPI/graphics epoch変更時の中断を壊さない。Animation評価は既存の明示update境界で進め、record回数を時刻の進行に使わない。記録を再利用するframeでもpresentation receiptと遷移完了通知は更新する。

RenderGraphのlayer/effectは、保持された論理nodeの更新を受けつつ、frameごとに正しいresource handleとpass依存を作る。前frameのtransient textureやCompiledRenderGraphを、そのまま次frameへ持ち越さない。retained textureを使う場合は明示的な外部owner/import/exportとして扱い、aliasされた一時資源と区別する。

上位UI側のWidget候補生成、layout、surface能力照会、submission通知待機等の負荷は別の改善対象である。2D listの部分更新だけでそれらが消えるとは主張しない。

## 10. dirty rectangleは後段に置く

初期版はtarget全体をClearし、必要な全描画を順にRecordする。CPU/GPU準備が再利用できることを先に確認する。

部分再描画へ進む場合は、保存されたoffscreen target上で、少なくとも次を満たす。

- damageは旧boundsと新boundsの和を含み、削除、AA、stroke、filter/shadowの広がりを考慮する。
- 変更objectだけでなく、damageに重なる背景・前後の要素を正しい順で再描画する。単なるLoadによる重ね描きでは消去・半透明合成が壊れる。
- layerのinput clipとoutput/composite clipを分け、effect前後のclip順を変えない。backdropはpainter-order上の先行targetに依存する。
- backdrop/filterは入力側の変更も依存として追跡する。任意effectには現状inverse-ROI契約がないため、必要ならtarget全体snapshot/full damageを選ぶ。自fragmentのrevisionだけでbackdrop結果をcacheしない。
- outputの保持が保証できないswapchainは全体compositeする。partial presentが使えるとは仮定しない。
- 境界やcustom rendererの影響を保守的に確定できない場合は、明示的なfull-layer/full-frame redrawへ戻す。更新を黙って無視しない。

## 11. 代替案と既存方式

### flat arrayを直接書き換える

実装は単純だが、snapshotとin-flight frameを壊しやすく、可変長のinsert/removeやreorderで後方移動が起きる。single-frame・専有bufferの特殊用途を除き、基本契約にはしない。

### Endのbuffer移譲だけを行う

正当な改善候補であり、必要な不変性とコピーを切り離せる。ただし毎frameの全記録・全Replay・全GPU準備は残るので、本件全体の解決にはしない。

### root DrawListを毎回作り、後から全commandをdiffする

既存APIを維持しやすいが、比較前に全構築/コピーの費用を払う。明示的な変更通知を持てるUI等の主経路には採らない。外部immediate入力との互換adapterとしては検討できる。

### sceneを全て新しい描画ライブラリへ置き換える

既存方式として次を調査した。

- [Chromium paint artifacts/property trees](https://chromium.googlesource.com/chromium/src/+/main/third_party/blink/renderer/platform/graphics/paint/README.md): stable display item/chunk ID、transform/clip/effectの分離、cached subsequence、raster invalidationの分離が参考になる。
- [Flutter SceneBuilder.addRetained](https://api.flutter.dev/flutter/dart-ui/SceneBuilder/addRetained.html): 前frameのsubtree再利用。ただしEngineLayerの単一scene内使用制約とoldLayer更新契約があり、その所有権モデルをそのままLumyteへ移すものではない。
- [Vello Scene](https://docs.rs/vello/latest/vello/struct.Scene.html): scene記録/再使用の参考例。Sceneの保持やappend APIだけで、GPU差分uploadやdirty rectangle再描画が保証されるとは判断しない。

これらを直接採用するには、現在のGraphics backend、paint/coverage、外部renderer、Resources連携等の契約を置換する評価が別途必要になる。本提案では設計原則を参照し、既存Graphics/RenderGraphの上へ追加する。新しい外部ライブラリ依存や有料基盤は要求しない。

## 12. 段階的な実装案

1. **契約・基準測定**: 現行immediate、#105、保持型prototypeを同じworkloadで比較する。実装前にsnapshot/resourceの所有権と対応範囲をADRで合意する。
2. **CPU fragment scene**: stable ID、transaction、persistent snapshot、UIの局所fragment置換を導入。未変更部分のReplay/連結を避ける。必要なscope再解決は部分木内へ限定する。
3. **compiled asset cacheとGPU page**: 既定rendererの再利用可能な準備を切り出し、frame ownerと分離。dirty pageだけを更新し、snapshot間のCOWとGPU退役を検証する。
4. **property-only fast path**: geometry/paint/dataを分離し、色・transform・clip等の更新範囲を縮小。fragmentを跨ぐbatchの維持とin-flight slotのversion整合を確認する。
5. **layer cache / damage**: 画素一致と保存targetの契約が成立する範囲だけで導入。部分更新量が大きい場合のfull rebuild/redrawも測定で選ぶ。

既存Canvas2D/DrawList2D/Renderer2D経路は残す。新方式を使わないアプリの契約変更を必須にしない。#105は現方式内の局所改善であり、本提案の前提でも、部分更新の成立証明でもない。維持・置換は既存経路の需要と比較測定で判断し、この検討だけでPRを変更・closeしない。

## 13. 受け入れ条件と測定計画

### correctness

- 全体再記録をreferenceとし、混在型・半透明重なり・reorder・削除・clip・DPI変更を画素比較する。
- customのbackground→self→foreground、同一payloadの複数配置、width変更とoffsetのみ変更、clip追加時の座標固定を検証する。
- fast/fallback切替と1,023/1,024/1,025 command境界を跨ぐinsert/remove/reorderを検証する。
- 旧snapshotの再描画、builder再利用、外部配列の後変更、transaction失敗/取消で旧内容が変わらない。
- stale ID、scene跨ぎID、cycle、同時writerを拒否する。
- 少なくとも3 frame in flightの途中で同じnodeを更新し、各frameが自分のrevisionを描く。
- slotを数frame後に再利用しても中間更新が欠落しない。GPU copy元・外部資源・layer targetが早期解放されない。
- Wait取消、Record失敗、Submit後の例外、Dispose/device loss、resource hot reloadで漏れ・二重解放・使用中再利用がない。
- group opacity、blurの範囲、backdrop、動くclip/文字をfull redrawと一致させる。
- custom renderer/OnRecord未opt-in時に、その状態変更をcacheが隠さない。
- RecordしたがSubmitされなかったuploadをReady扱いしない。atlasのpixel変更とUV再配置、HotReloadとdevice lossを別に検証する。

### workloadと指標

10,000矩形の静止/1要素/1%/全要素更新、頻繁なinsert/remove/reorder、親transform更新、複雑path/clip、静的/動的文字、重なったlayer/effectを測る。1,000個の1-command fragmentでもdraw数がfragment数へ退化しないことを確認する。

記録、Commit/Capture、CPU compile/pack、GPU upload/copy、command record、submit/completionを分ける。CPU割当と保持容量、GPU page生成数/byte数、draw数、cache hit/miss理由、retire待ちbyte数も記録する。静止・局所更新だけでなく全更新時のtree/COW overheadも比較する。

no-opではcommandの再記録/全体コピー/再compile・再uploadを避けることを目標とする。一方、初期方式のcommand recordとdrawはvisibleなpacket数に比例し得る。**frame全体が常にO(dirty)になるとは言わない。**

性能倍率・page容量・cache予算・full rebuildの閾値は未測定であり、ここで確定しない。CPU-only測定とhardware GPU測定を分け、software rendererで実GPU性能を代用しない。

## CPU試作比較による追記（2026-11-02）

[性能比較結果](../performance/drawing2d-incremental-comparison.md)を追加した。固定要素数のfragment置換は局所CPU公開を短縮できたが、1 commandを既存DrawListで包む試作は初期構築・保持量・全更新で退化した。既存Renderer向けに全Replay/Endすると、その費用も含めた経路は全再記録より遅かった。

したがって、この試作をそのまま採用するのではなく、軽量なpayload/page表現、全更新のbulk経路、保持sceneを直接消費するRenderer、clip/batch共有の維持を次の検証対象にする。GPU resident cache・実UI host・実GPU性能はこの試作では未検証であり、本設計全体の性能成立を確認したものではない。

## 実GPUでの追試

[実GPU比較](../performance/drawing2d-hardware-comparison.md)ではRadeon8060S/RADVの作成deviceとtimestampを直接確認し、1/3 frame in flightも比較した。保持型＋全平坦化の試作は今回のworkloadで不利だった。これは保持sceneを直接消費するRendererやGPU resident cacheを実装した完成案の評価ではなく、互換平坦化を最終形にしない判断を補強する。

## 保持page直接描画の限定試作

[直接描画試作の結果](../performance/drawing2d-direct-pages.md)では、CPU64矩形pageとGPU1024矩形runを分離し、全Replay/Endを省いてresident GPU bufferへ差分反映した。限定した単色矩形経路では局所更新のCPU更新/Prepareとheadless throughputが改善した。一方、全変更時の割当/pipeline退化、分散更新のcopy/barrier費用が残る。一般のpath/text/clip/構造更新を含む製品設計の採用判断とは分ける。

## 次に合意する事項

- 最初の対応範囲を、基本Drawing2Dのfragment sceneと全体再描画までにするか。
- UIの保持recordをこの共通sceneへ接続し、別のUI専用描画storeを増やさない方針。
- custom rendererの再利用契約、外部資源revision/lease、RenderGraph layerの拡張境界。
- prototypeで測るpage/fragment粒度と、全更新時に許容するoverhead。
