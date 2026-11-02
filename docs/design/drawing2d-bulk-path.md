# Drawing2D bulk更新経路（設計案）

- 状態: 提案・未採用・未実装
- 日付: 2026-11-03
- 基準: 調査branch `04836c8`、main `fbc8105` / #105 `ffe075c`
- 範囲: CPU一括構築、描画準備、一括転送、snapshot/実行ownerの寿命

実装や新しい計測は行わない。[部分更新案](drawing2d-incremental-rendering.md)を補完する設計で、既存ADR/APIを変更しない。採用時は[ADR運用](../adr/0001-adr-writing-policy.md)に従って判断を分割・採番する。

PRレビュー用の主要判断は[GRAPHICS-0026](../adr/graphics/GRAPHICS-0026-bulk-drawing-updates.md)へ整理した（状態は提案）。本書はAPI/失敗/座標/退役の詳細を補足する。

## 1. 決定したい方針

**bulkは指定したcontent範囲をfreshなtyped storageへ線形構築する経路である。差分操作を大量に作ってから適用する方式ではない。**

差分とbulkは、同じ不変content/snapshot、順序付きrender plan、device資産、freshなone-shot frame ownerへ合流する。用途別rendererや全scene共通のBulkModeを作らない。

CPU bulk構築とGPU full-run uploadは別の判断である。更新範囲のCPU内容を一括置換しても既存geometryを参照できるし、CPU差分だけを更新してGPU転送だけfull-runを選んでもよい。

### 非目標

- 全frameをO(dirty)にすること、全コピー/全allocationの排除。
- partial framebuffer redraw、damage tracking、部分Present、保存画像の再利用。
- MSDF/atlasの導入、任意shaderのdraw融合、文字組みやUI schedulerの変更。
- 現在のPreparedDraw2Dを複数回Record可能にすること。
- 未実装のUI/path対応を、矩形試作の性能結果だけで高速と認定すること。

## 2. 根拠と解決する費用

[直接矩形試作](../performance/drawing2d-direct-pages.md)の全変更はCPU更新約0.66ms、約1.76 MB/frameのmanaged allocationで、legacyよりpipeline throughputが低下した。現試作は全変更専用branchでも、command record群、変更tuple snapshot、command参照配列、6 Vector4のpayloadを新しく用意する。

[正式12workload比較](../performance/formal-rendering-workload-comparison.md)は別内容である。editorのPrepare/GPU支配を、CPU bulkだけで解消するとは主張しない。

現行コードとの境界:

- [DrawingCanvas2D](../../src/Graphics/Lumyte.Graphics.Drawing2D/Model/DrawingCanvas2D.cs)はcommand objectのListへ記録し、Replayは全commandを再生成する。
- [DrawList2D](../../src/Graphics/Lumyte.Graphics.Drawing2D/Model/DrawList2D.cs)は順序と型別格納を持つ。#105だけでは公開表現は変わらない。
- [DirectRectangleScene.Update](../../tools/benchmarks/drawing2d-incremental/DirectRectangleScene.cs)は変更tupleをsnapshotしてからpageを作る。bulk producerにこの変更列を要求しない。
- [PreparedGpuBatch2D](../../src/Graphics/Lumyte.Graphics.Drawing2D/Rendering/PreparedGpuBatch2D.cs)の汎用経路は命令ごとのtable/buffer生成・uploadを行う。CPU一括構築だけではその費用は残る。
- [PreparedDraw2D](../../src/Graphics/Lumyte.Graphics.Drawing2D/Execution/PreparedDraw2D.cs)はone-shot。cacheする対象は内部のimmutable compiled assetであり、このownerではない。

## 3. 更新単位と使い分け

bulkの単位はsceneのcontent slot/leafまたは独立した順序付きdrawingであり、GPU pageやUI root全体に固定しない。

- 2D全instance移動: geometry/paintは保持し、instance属性列だけをbulk更新する。
- 2D geometry stream: geometryが本当に変わる範囲だけcontentをbulk置換する。
- ゲームUI: animated menuのcontentだけbulk、静止HUDは保持、世界とは別の順序付きlayer。
- エディタUI: resize/reflowで変わったpanel/runだけbulk、他panel/previewは独立。

包含scene node/content slotのIDは維持する。内部のcommand indexは永続identityにしない。bulk contentを取り替えてもsceneの兄弟・別slotのIDは変えない。子Element/nodeの構造編集はscene transactionの別操作であり、content置換に紛れ込ませない。

## 4. 共通データモデル

```text
Producer / UI adapter
  ├─ sparse: leaf/page編集
  └─ bulk: 完成形を順番にwrite（変更tuple列なし）
                     ↓
   Immutable DrawingContent / SceneSnapshot
     typed item pages + order tape + immutable dependency references
                     ↓
   Compile / asset resolve / ordered run planning
     geometry/paint資産と、placement属性を分離
                     ↓
   Prepare frame slot: patch範囲 or full-run転送
                     ↓
   fresh one-shot frame owner → Record → Submit → 完了 → retire
```

### Canonical CPU storage

- 最初から最終typed itemへ書く。矩形等のper-itemはvalue rowで、geometry/複雑paint/clipは既存immutable objectまたはtokenを参照する。単色はColor2Dをinline保持し、固有色ごとにPaintGraph2D.Solidを生成する入口を必須にしない。
- 描画順tapeは(kind, typed-index/range, scope/reference)を保持する。混在命令を型別に全体sortしない。
- command object配列、型別配列、変更tuple配列、device-packed配列を同時に全量保持することをcanonical表現にしない。
- 新canonical rowにraw GPU handle/device packed ABIを埋め込まない。ただし既存paint/fragmentはborrowed texture/viewを間接参照し得る。旧device後もCPU資産だけで再prepareできるのはdevice非依存部分だけ。外部画像は再resolve可能な資源tokenを使うか、新viewを再提供してcontentを再作成する。
- 変換だけの更新ではshape/paint/clipの本体を再構築しない。cameraの外側propertyと各instanceの属性を区別する。

現DirectRectangleSceneの6 Vector4/device-target依存payloadを、このcanonical CPU表現と同一と見なさない。共通化時はsparse側もtyped content pages＋device側pack cacheへ適合させる。既存試作の比較には、adapterのpack費用を含めた独立段階が必要である。

### BuilderとFreeze

1. builderはsingle-writerで未公開storageを専有する。必要容量hintは任意で、hintより多くても意味を変えずpage追加か明示budget失敗にする。
2. span一括入力は受け取り中に検証・コピーする。caller配列を保持しない。
3. 各rowの入力検証はappend時に済ませる。Freezeはscope/dependencyの構造を検査して現在のpagesをdetachし、immutable contentへ所有権移譲する。旧contentの全clone、全型分類、全hash走査、全rowの再検証をしない。Freezeがmetadata量まで常にO(1)とは保証しない。
4. Freeze成功後はbuilderの論理件数とorder/dependency cursorを空へ戻し、次の追加を新しいpagesへ書く。旧snapshotのbacking storageをClear/再使用しない。空のFreezeは空contentを返し、それを明示置換すると当該drawingを消す。空transactionのno-opとは区別する。
5. 既存DrawList2Dの参照を含める場合は不変fragment参照として保持する。Replayで全展開する互換経路は既定にしない。

CPU pagesは初版ではGC所有とする。短命scratchのみArrayPoolを利用できるが、公開済みpageはpoolへ返さない。これで配列allocation自体は残るが、per-command object・変更tuple・不要な全体コピーを避ける。
CPU sealed pageのpool化は、明示的なsnapshot lease/最後のreader検出を別判断で導入してから行う。read-only wrapperやGPU完了だけではCPU readerの終了を証明しない。CPU snapshotを何世代保持するかはhostが管理し、GPU cacheのbyte budgetでCPU旧snapshotまで自動evictできるとはしない。

## 5. APIの候補（名称は未確定）

比較元には以下のAPIはない。物理pageやshader ABIを利用者へ公開しない。

```diff
+namespace Lumyte.Graphics.Drawing2D
+{
+    // ImmutableなCPU content。初版はGC所有で、borrowed GPU資源の所有権は含まない。
+    public sealed class DrawingContent2D;
+
+    // 値rowの一例。paintは不変参照。transformはinstance属性。
+    public readonly record struct BulkRectangle2D(
+        Rect2D Rect, PaintGraph2D Paint, System.Numerics.Matrix3x2 Transform);
+
+    // 全件固有色でもpaint objectを要さない単色経路。
+    public readonly record struct BulkSolidRectangle2D(
+        Rect2D Rect, Color2D Color, System.Numerics.Matrix3x2 Transform);
+
+    // 同じ不変contentを複数配置する。geometryを毎frame作り直さない。
+    public readonly record struct BulkInstance2D(
+        DrawList2D Content, System.Numerics.Matrix3x2 Transform);
+
+    public sealed class DrawingBulkBuilder2D : IDisposable
+    {
+        // capacityHintは論理order entry件数のhint（byte数/draw数ではない）。
+        // ゼロは既定値、負数はArgumentOutOfRangeException。
+        public DrawingBulkBuilder2D(int capacityHint = 0);
+        // clipは共通scopeの座標に固定。item transformで親clipを移動しない。
+        // 入力検証失敗では呼出し前を維持。書込み失敗時は下記契約に従う。
+        // local→scopeはitem.Transform * scope.Transform。scope clipはitemで動かさない。
+        public void AddRectangles(
+            ReadOnlySpan<BulkRectangle2D> items, DrawingState2D scope);
+        // 単色は値として書き、既存のlinear premultiplied変換をpack時に維持する。
+        public void AddSolidRectangles(
+            ReadOnlySpan<BulkSolidRectangle2D> items, DrawingState2D scope);
+        // 同じcontentの配置属性列を一括構築。spanの要素はコピーする。
+        public void AddInstances(
+            ReadOnlySpan<BulkInstance2D> instances, DrawingState2D scope);
+        // 参照を追加するのであり、DrawingCanvas2D.Replayの別名ではない。
+        public void AddFragment(DrawList2D drawing, DrawingState2D scope);
+        // scope/dependency/順序を検証しstorageを切り離す。本APIにSave stackはない。
+        public DrawingContent2D Freeze();
+        // 未公開storageだけを破棄。Freeze済みcontentは破壊しない。
+        public void Dispose();
+    }
+
+    // 既存stateへの追加member抜粋。null Clipを空として扱わない。
+    public sealed record DrawingState2D
+    {
+        public static DrawingState2D Identity { get; }
+        // 呼出し時のTransformでclipをcaptureする。戻り値は新しいstate。
+        public DrawingState2D WithClipRect(Rect2D rect);
+        public DrawingState2D WithClipPath(Path2D path, FillRule2D rule = FillRule2D.NonZero);
+    }
+
+    // bulk consumerの明示選択。既存renderer表から勝手に既定compilerへ迂回しない。
+    public sealed class DrawingBulkRenderers2D
+    {
+        public static DrawingBulkRenderers2D CreateDefault();
+        public static DrawingBulkRenderers2D FromLegacy(DrawingCommandRenderers2D renderers);
+    }
+
+    // 新しいcompletion-aware owner。既存PreparedDraw2Dの契約は変更しない。
+    public sealed class PreparedBulkDraw2D : IDisposable
+    {
+        public void Record(IGraphicsCommandBuffer commands);
+        // 当該commandをSubmitして実際に返ったsubmissionを一度だけ借用する。
+        public void AttachSubmission(IGraphicsSubmission submission);
+        // 関連submission完了と記録command破棄を確認してからslotを返す。
+        public void Retire();
+        // Submitを一度も実行していないことをcallerが保証する明示経路。
+        public void AbortUnsubmitted();
+        // 未Recordの予約は返せる。pending/不明な使用は解放せず例外。
+        public void Dispose();
+    }
+
+    // 既存classへのmember追加の抜粋。現classをpartialへ変更する提案ではない。
+    public sealed class Renderer2D
+    {
+        // bulk表を明示して共通plannerへ渡す。戻り値はfreshなone-shot owner。
+        public ValueTask<PreparedBulkDraw2D> PrepareAsync(
+            DrawingContent2D content, RenderTarget2D target,
+            DrawingBulkRenderers2D bulkRenderers,
+            CancellationToken cancellationToken = default);
+    }
+}
```

standaloneの2DアプリはDrawingContent2Dを上記PrepareAsync候補overloadへ直接渡し、scene/永続node IDを必須にしない。sceneへの接続は既存の部分更新案のtransactionで、指定nodeのcontent参照を一度に置換する形を取る。builderとscene transactionの責務を混ぜず、Commit前のbuilder失敗/取消では現在のscene rootを維持する。

AddRectangles/AddSolidRectangles/AddInstancesは検証passで入力の不備を全て確認し、通常の入力検証失敗ではbuilderの論理cursorを変えない。次に必要pageをreserveし、checkpointを置いてappendする。通常の書込み失敗はcursor・order/dependency tableをcheckpointへ戻す。allocation/OOM等で巻戻しを保証できない場合はbuilderをFaultedにしてFreeze/追加を禁止し、Disposeだけを許可する。公開済みcontent/scene rootは常に影響を受けない。検証passだけで資源不足時の原子性まで保証したとはしない。

検証済みrowを大量のtuple/objectへ複製する必要はない。ただし検証・コピー中にcallerが同じspanを別threadで変更しない契約を要求する。所有配列の無断共有は行わない。
DrawingCanvas2Dは現在private Listへ非virtualに書くので、継承するだけではtyped bulk recorderにならない。新builderを独立に置き、将来共有recording sinkへ整理するかは別の互換性判断とする。

### 既存renderer拡張との接続

現在のIFillRectRenderer2Dは`ReadOnlyMemory<FillRectCommand2D>`を受け取り、typed rowを直接は読めない。consumer表を追加するだけで現拡張が自動的に高速化されるとはしない。

- CreateDefaultは新しいtyped bulk compilerへの明示的opt-in。旧Renderer2Dに渡した任意のcustom登録が適用されると仮定しない。
- FromLegacyは指定された既存DrawingCommandRenderers2Dを実際に呼ぶadapter。連続型/clip/orderを維持し、必要ならtyped rowから既存commandを再生成する。このobject生成/固定化費用が残る経路をnative bulk性能へ混ぜない。
- 対応のないconsumerはNotSupportedException。内部で勝手にCreateDefaultへ切替えない。initial版の高速custom consumer登録APIは別review項目であり、既存custom実装はFromLegacyで維持する。
- consumer表とdevice資源は既存同様borrowed。GPU使用中に差し替えず、frameが必要な正確な資源世代を保持する。

### transform/clipの合成

DrawingState2D.Identityを公開候補として追加するので、外部callerもidentity＋clipなしを構築できる。ClipStack2Dのinternal Empty/constructorに依存せず、null Clipは検証で拒否する。WithClipRect/WithClipPathは不変stateを返し、その時点のtransformをclipへcaptureする。

outer scopeをS、instance transformをI、fragment内command transformをT、内側clipがcaptureしたtransformをCとすると、論理root座標への合成は次の通り。

- row: `item.Transform * S.Transform`
- fragment command: `T * I * S.Transform`（AddFragment単独ではI=identity）
- fragment内clip: `C * I * S.Transform`
- S.Clip内の外側clip: capture済み座標のまま。I/Tの変更では移動しない。

既存Replayと同じ順で外側/内側clipを交差し、PixelScale/PixelOriginはtarget段で適用する。各合成結果とboundsをfinite検証する。参照追加を全leaf走査にしないため、fragment内部とouter transformの組合せでのみ検出できるoverflowはPrepareで拒否できる。scope変更だけでclipの座標規則を変えない。

## 6. GPU bulk準備

### CPU内容とGPU資産を分ける

不変geometry/paintのcompile資産をdevice epoch・shader ABI・依存revisionで再利用する。bulk内容が新しくても同じgeometry参照なら再compileを要求しない。target/DPI/clipがpacked dataへ焼き込まれる場合はその依存をkeyへ含めるか、property列へ分離する。

### Per-slot arena

- slotがcapacity単位のupload/data arenaとdescriptor領域を専有し、連続したsliceへsuballocateする。
- capacity不足のgrow/新descriptor設定はそのslotがidleのときだけ行う。旧frameのGPU ref/offsetを後から変更しない。
- 同じABI/資源領域の隣接データをまとめ、map/unmapとcopy範囲をrun単位に寄せる。
- packはbounded scratchからmapped uploadへ順次書く。現在のbuffer APIはCopyFromが中心で、汎用mapped Spanが公開されていると仮定しない。全frameサイズの一時ToArrayを作る代わりに、一定サイズのscratchを再使用する。
- instruction/dataのアラインメント・shader layout・descriptor上限を守る。物理pageはbudgetで分割でき、bulkが必ず1 buffer/1 copy/1 drawになるとはしない。
- renderer全体とslotごとにidle/retained byteの上限を設ける。budget超過時に未完了slotをWaitして強制回収したり、黙って全sceneへfallbackしない。全体budget内の新slot/分割だけを許可し、それでも収まらなければ明示失敗とする。新slot追加で全体上限を迂回しない。

新しいgeneric packet ABIでtableを共有する場合はinstruction/dataへのbase offset等が必要になる。現ABIへ押し込むため同一bindingをcommandごとに上書きしない。descriptorは当該encoded frameの使用が終わるまで不変。ABI/offset設計はgeneric compiler導入段階でレビューする。

### Upload plan

CPU bulkを選んだことと、全GPU内容を毎frame転送することは同義ではない。

- 同一content/依存revision: resident資産を再利用し、uploadしない。
- 属性列のみ変化: 属性列だけをfull-run uploadできる。
- geometry/content全変更: そのcontent/runの有効範囲をまとめてuploadする。
- CPU sparseでもdirty範囲が細かすぎる場合: GPU full-run uploadを選べる。

各slotが保持する版と、今回snapshotの版を比較する。slotを使っていなかったframeの変更もcatch upし、古いsnapshotへ戻った場合も必要な内容を反映する。

## 7. 方針選択

初版はCPU側のsparse/bulkをproducerが明示する。部分的な変更列しか渡されていないのに、rendererが勝手に全完成形を再構築できるとは仮定しない。

GPU側のpatch/full-runは各runで選ぶ。候補costは、dirty byte数、range数、map回数、copy/barrier回数、転送帯域、slotが未反映の版数から見積もる。命令数のdirty率だけを使わない。

```text
patch cost ≈ range固定費 + dirty bytes転送費 + pack/map費
full cost  ≈ run固定費   + full bytes転送費  + pack/map費
```

50%等の固定閾値を先に性能上の真実として採用しない。backend/ABI別の係数は計測で決め、必要ならhysteresisを設ける。自動選択前にForceSparse/ForceBulk相当の検証用overrideで両方を測れるようにする。

## 8. 描画順と境界

同じordered run plannerがsparse/bulk両方を消費する。CPU更新方式を変えただけでdraw数/pass分割が変わらない形を基本比較にする。

- 透明命令のpainter order、scope clip座標、group opacity/isolated compositingを維持する。
- 独立したprivate uploadを先に記録できるが、custom batch/effect/backdrop/外部資源依存を越えて移動しない。
- render pass融合は、隣接した互換packetだけを別の最適化として評価する。bulkと同時に有効化して効果を混同しない。
- custom IPreparedCommandBatch2DやRenderGraph専用layerは明示的なopaque境界。対応外を近似したり全体sortしたりしない。

## 9. 公開・GPU退役・失敗

CPU: Build → Freeze → transaction検証 → Commit。一度に新content/rootを公開し、旧snapshotは引き続き読める。空transactionは同root、明示した同値内容のReplaceは全体比較なしに新revisionとしてよい。

GPU: Idle → Reserved → Prepared → Recorded → Submitted → Completed → CommandsDisposed → Retired/Idle。

submissionの観測は新しいPreparedBulkDraw2Dが担当する。standalone callerもhost coordinatorも、Recordした正確なcommandをQueue.Submitした後、その戻り値をAttachSubmissionへ渡す。ownerはsubmission/commandを借用するだけで、Submit/Wait/Disposeを代理しない。基本APIだけではsubmissionにそのcommandが含まれるかを完全に証明できないため、exact associationはcaller契約とする。

callerはWaitAsync成功等で完了を確認し、記録commandを破棄し、その後Retireを呼ぶ。Retireはまだ生存する関連submissionのStatus=Completedとcommand.State=Disposedを確認し、resident版の公開とslot返却を行う。その後callerがsubmissionをDisposeする。GPU完了だけでcommand破棄前にslotを返さない。device失効時はresident版を公開せず、coordinatorの安全な退役/破棄確認へ回す。

AttachSubmission前でも、Queue.Submitを試行して成否不明になったframeをAbortUnsubmittedしてはならない。Abortは一度もSubmitしなかったこととcommand破棄の両方を必要とする。ownerのDisposeは未Recordなら予約を返し、Recorded以後はRetire済み/安全なAbort済み以外を拒否する。既存PreparedDraw2Dの単なるDisposeだけを完了通知へ読み替えない。

- Prepared/Recordedはupload完了を意味しない。resident版は関連submissionの完了とdevice有効性を確認してから進める。
- Record前の取消は予約だけを返せる。Record済み未送信ではcommandを先に破棄し、未送信を確認してから解放する。
- Submit後のWait取消・Failed・成否不明を完了と扱わない。再利用/破棄は既存のcoordinatorとGPU寿命契約へ従う。
- CPU Commit後のGPU prepare失敗はCPU rootを自動rollbackしない。表示hostは最後に成功したsnapshotを保つ等のpublication方針を持つ。
- borrowed texture/viewをCPU強参照だけで生存保証したことにしない。ResourceManagerの正確な世代leaseとdevice epochを尊重する。
- DisposeにSubmit/Wait/Presentを隠さない。frame ownerはone-shotのままにする。

## 10. 導入順と代替案

1. **契約/CPU**: typed bulk builderとfreeze、旧snapshot/混在順序を検証。既存Canvas/DrawList APIは維持する。
2. **限定GPU**: 単色矩形で現shader ABI/1024 instance runへ接続。partial試作と同じplannerでCPU全更新と属性全更新を区別する。初期eligibilityは現default rectangle shader ABIと、既存fast rectangleの単色paint・有限で逆変換可能なtransform・最大8段の矩形clip等に合わせる。fragment/instance参照の内部にも同じ判定を行い、path/text/未対応paint/path clip/custom shader描画は初期fast compilerのPrepareで明示拒否する。必要なら利用者がFromLegacyを明示選択する。CPU modelの妥当性とbackend対応は別検査とする。
3. **共通GPU arena**: generic instruction/dataのoffset ABIとdescriptor上限を定義し、path/clip/textの既存評価へ接続。callback/RenderGraph境界は残す。
4. **選択policy**: range分断とdense更新の実測後にGPU upload自動選択。CPU自動選択は完成形producer契約が成立した範囲だけ。

既存DrawListの高速constructorだけを使う案は移行が小さいが、変更tuple/per-command準備を消せない。毎frame全commandをmutable配列で上書きする案は旧snapshot/GPU使用を破壊する。最初からCPU sealed pageをpoolする案はreader寿命契約を先に必要とするため初版では採らない。

## 11. 測定・受け入れ条件

新しい速度値や閾値はまだない。実装後に同じ入力・出力範囲で次を比較する。

- no-op/single/clustered/dispersed/full content、transform-only、order-only、insert/remove/empty、resize/text reflow。
- 既存一括Canvas、既存sparse試作、ForceBulk、GPU patch/full-runを分ける。
- Build/Freeze/compile/pack/map/encode/dispose、managed allocation/live heap、arena retained byte、GPU allocation/copy byte/range数、draw/pass数を別記。
- full updateで既存bulk構築より遅い場合も残し、局所更新はbulkへ強制しない。
- 2D/ゲームUI/エディタUIの[既存12fixture](../../tools/benchmarks/drawing2d-incremental/WORKLOADS.md)を再利用。ただし対応していない型は未測定とし、矩形へ置換して成功扱いしない。
- 旧snapshotを保持してbuilder再利用、caller入力変更、失敗時の原子性、透明/clip順序、1/3 frame in flight、slot未使用世代と旧版への巻戻し、資源失効・失敗/取消を検証。
- 同じtarget/frame列で画素を照合する。AA/文字経路を変える別最適化は分離する。

## 12. 参考にした既存方式

- [Vello Scene](https://docs.rs/vello/latest/vello/struct.Scene.html): command/context/resourceのencodingと明示reset。reset可能なmutable sceneを、そのままLumyteの旧snapshot不変性の保証と混同しない。
- [wgpu StagingBelt](https://docs.rs/wgpu/latest/wgpu/util/struct.StagingBelt.html): stagingをchunkからsuballocateしfinish/submission/recallを分離する例。Vulkanを含むLumyte共通APIへ固有APIを直接持ち込まず、arenaと退役の設計を参照する。

既存方式を参考にするが、新しいrenderer/backend依存やライブラリ導入は提案しない。本書の採用・実装・性能保証は別の判断である。
