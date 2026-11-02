# ADR-GRAPHICS-0026: typed contentの一括構築とGPU転送選択を分離する

- 状態: 提案
- 日付: 2026-11-03
- 比較元: main `fbc8105`

## 背景

局所更新に適したCOWでも、全変更をN件の変更tupleやcommand objectに変換してから適用すると構築・割当費用が増える。
[direct矩形試作](../../performance/drawing2d-direct-pages.md)では局所更新は改善したが、全変更の割当とpipeline退化、分散copy/barrierの費用が残った。
[保持内容の判断（GRAPHICS-0025）](GRAPHICS-0025-retained-drawing-content.md)を補完する一括構築経路が必要である。

本ADRは[bulk設計文書](../../design/drawing2d-bulk-path.md)の主要判断を記録する。詳細なAPI/失敗/座標/退役契約は同文書を参照する。
製品のtyped bulk builder/consumer/arenaは未実装であり、benchmark-onlyの矩形bulk branchを完成実装とは扱わない。

## 決定（提案）

**指定content範囲の完成形をfreshなtyped storageへ線形構築し、N件の変更tuple列を中間表現として要求しない。**
sparseとbulkは同じ不変content、順序付きplanner、device資産、freshなone-shot frame ownerへ合流する。
全scene共通BulkModeや用途別rendererを作らず、content slot/leafまたは独立drawingを単位にする。
包含node/slotのIDは維持し、内部command indexを永続identityにしない。構造編集はscene transactionの別操作とする。

### Canonical CPU storageとFreeze

- 最終typed value rowとorder tapeへ最初から書く。複雑paint/geometry/clipは不変参照、単色はColor2Dをinline保持する。
- 固有色ごとのPaintGraph2D.Solid object生成を必須にせず、pack時に既存linear premultiplied変換を維持する。
- order tapeは混在型の順序・scope/参照を保ち、型別全体sortや全量command/tuple/device-packed配列の併存をcanonicalにしない。
- transform-only/instance属性更新ではgeometry/paint/clip本体を保持する。CPU bulkを選んでも再compileは必須ではない。
- canonical rowへraw GPU handleを埋め込まない。ただし参照paint/旧fragmentのborrowed texture/viewはdevice依存になり得る。

single-writer builderは未公開pageを専有し、spanを検証・コピーする。caller配列を保持せず、検証/コピー中の並列変更を禁止する。
通常の入力検証失敗は呼出し前cursorを維持し、append失敗はcheckpointへ戻す。OOM等で保証不能ならFaultedにしDisposeだけを許す。
Freezeは構造/dependencyを検証してpageをdetachし、旧contentの全clone/全分類/全row再検証をしない。metadataまで常にO(1)とはしない。
成功後のbuilderは空の論理状態から新pageへ書き、旧pageをClear/再利用しない。空Freezeによる明示置換は描画削除で、no-op transactionとは異なる。
初版sealed CPU pageはGC所有とし、CPU reader leaseを別途定義するまでpoolへ返さない。GPU完了だけではCPU reader終了を証明できない。

### Producer選択とconsumerの明示性

CPU sparse/bulkは初版ではproducerが明示する。rendererが部分変更列から完成形を勝手に再構築する方式は採らない。
CreateDefaultはtyped compilerへの明示opt-in、FromLegacyは指定された既存renderer表を実際に呼ぶadapterとする。
FromLegacyのcommand再生成・固定化費用は残り、native bulkの性能と混ぜない。未対応consumerはNotSupportedExceptionとし、隠れて既定compilerへ迂回しない。
standaloneはsceneなしでcontentをPrepareできる。scene接続はtransactionでcontent参照を一度に置換し、builder失敗で公開rootを変えない。

### 公開API候補（未実装）

比較元main `fbc8105`に以下の追加APIは存在しない。判断対象の抜粋であり、名称・最終公開範囲は未確定である。

```diff
+namespace Lumyte.Graphics.Drawing2D
+{
+    public sealed class DrawingContent2D; // GC所有の不変CPU内容。native資源は所有しない
+    public readonly record struct BulkRectangle2D(
+        Rect2D Rect, PaintGraph2D Paint, System.Numerics.Matrix3x2 Transform);
+    public readonly record struct BulkSolidRectangle2D(
+        Rect2D Rect, Color2D Color, System.Numerics.Matrix3x2 Transform);
+    public readonly record struct BulkInstance2D(
+        DrawList2D Content, System.Numerics.Matrix3x2 Transform);
+    public sealed class DrawingBulkBuilder2D : IDisposable
+    {
+        public DrawingBulkBuilder2D(int capacityHint = 0); // order件数hint、負数は拒否
+        public void AddRectangles(ReadOnlySpan<BulkRectangle2D> items, DrawingState2D scope);
+        public void AddSolidRectangles(ReadOnlySpan<BulkSolidRectangle2D> items, DrawingState2D scope);
+        public void AddInstances(ReadOnlySpan<BulkInstance2D> instances, DrawingState2D scope);
+        public void AddFragment(DrawList2D drawing, DrawingState2D scope); // Replay展開ではない
+        public DrawingContent2D Freeze(); // pageをdetach、builder再利用でも旧内容不変
+        public void Dispose(); // 未公開storageのみ破棄
+    }
+    public sealed record DrawingState2D // 既存型への追加member
+    {
+        public static DrawingState2D Identity { get; }
+        public DrawingState2D WithClipRect(Rect2D rect); // 現transformでclipをcapture
+        public DrawingState2D WithClipPath(Path2D path, FillRule2D rule = FillRule2D.NonZero);
+    }
+    public sealed class DrawingBulkRenderers2D
+    {
+        public static DrawingBulkRenderers2D CreateDefault(); // typed compilerを明示選択
+        public static DrawingBulkRenderers2D FromLegacy(DrawingCommandRenderers2D renderers);
+    }
+    public sealed class PreparedBulkDraw2D : IDisposable
+    {
+        public void Record(IGraphicsCommandBuffer commands); // 一度だけ
+        public void AttachSubmission(IGraphicsSubmission submission); // 正確なSubmit戻り値を借用
+        public void Retire(); // Completedかつcommand Disposedを確認してslot返却
+        public void AbortUnsubmitted(); // Submit未試行かつcommand破棄済みだけ
+        public void Dispose(); // pending/成否不明の使用は解放せず拒否
+    }
+    public sealed class Renderer2D // 既存型への追加overload
+    {
+        public ValueTask<PreparedBulkDraw2D> PrepareAsync(DrawingContent2D content,
+            RenderTarget2D target, DrawingBulkRenderers2D bulkRenderers,
+            CancellationToken cancellationToken = default);
+    }
+}
```

scope clipはitem transformで動かさず、rowはitem.Transform * scope.Transform、fragment内部も既存Replayと同順に合成する。
null Clipを空扱いせず拒否し、finite検証と既存shape/clip/paintの同一subpixel評価を維持する。

### GPU準備・転送と退役

slot専有arenaへ連続sliceを確保し、bounded scratchからpackする。grow/descriptor変更はidle時だけ行い、in-flight参照を上書きしない。
GPU patch/full-runはCPU方式と独立にrunごとに選ぶ。dirty byte/range、map/copy/barrier、slotの未反映版数を測り、固定dirty率を真実としない。
同一contentはresident再利用、属性だけなら属性full-run、geometry変更なら該当範囲を転送できる。slot休止中の変更と旧snapshotへの巻戻しも追従する。
全体/slot byte予算と分割上限を設け、未完了slotを強制回収せず、収まらなければ明示失敗する。

状態はReserved → Prepared → Recorded → Submitted → Completed → CommandsDisposed → Retired/Idleとする。
callerは正確なcommandのSubmit戻り値をAttachSubmissionへ渡し、完了確認後にcommandをDisposeしてRetireし、最後にsubmissionをDisposeする。
ownerはcommand/submissionを借用するだけでSubmit/Wait/Disposeを代理しない。exact associationはcaller契約である。
Retireは生存するsubmissionのStatus=Completedとcommand.State=Disposedを検査し、device有効時だけresident版を公開してslotを返す。
Submit未試行かつcommand破棄済みだけAbortでき、Wait取消・Failed・成否不明は完了ではない。device失効はcoordinatorの安全な退役へ回す。
borrowed paint/viewは完全なCPU再構築可能資産ではない。失効時は資源tokenの再resolveか新viewでcontentを再作成し、正確な世代lease/epochを守る。
CPU Commit後のPrepare失敗はrootを自動rollbackせず、表示hostが最後の成功snapshot等を選ぶ。

## 検討した代替案

- N件の変更tupleを作って適用: dense構築前後のobject/コピーを減らせず、bulkの目的に反する。
- 既存DrawList高速constructorだけ: 移行は小さいがper-command準備やGPU転送を解決しない。
- mutable全配列上書き/初版からsealed page pool: 旧snapshotとreader/in-flight寿命を別途解決する必要がある。
- Vello Scene/wgpu StagingBeltの直接導入: encoding/arena/retireの原則を参考にし、既存共通backend契約の置換や新依存は要求しない。

## 結果と影響

全更新の中間表現を減らし属性とgeometryを再利用できる可能性があるが、GC page割当、arena常駐容量、adapter費用は残る。
bulkが1 copy/1 drawになるとは限らず、custom/effect/RenderGraph境界とpainter orderを保つ。pass融合は独立に評価する。

## 検証方針・導入段階

1. CPU builder/Freezeと旧snapshot・混在順序・失敗原子性を検証する。
2. 限定GPUはdefault矩形ABIの単色paint、有限かつ逆変換可能transform、最大8段の矩形clip等に限定し、参照fragment内部も判定する。
   path/text/未対応paint/path clip/custom shaderは初期fast Prepareで明示拒否する。必要ならcallerがFromLegacyを選ぶ。
3. generic instruction/data offset ABIとdescriptor上限をreviewし、path/clip/textへ拡張する。
4. GPU patch/full-runを強制比較後に自動policyを導入する。CPU自動選択は完成形producer契約の成立後に別途判断する。

上記eligibilityは提案する次段階であり、既存direct試作は固定順/数・単色矩形・identity・clipなし・固定1024 targetのみである。
[正式12 workload](../../performance/formal-rendering-workload-comparison.md)はmain/#105だけの比較で、保持/bulk UIの検証結果ではない。
[部分更新設計](../../design/drawing2d-incremental-rendering.md)とbulk仕様の測定計画に従い、no-op/局所/分散/full/属性/order/empty、1/3 in-flightを比較する。
Build/Freeze/compile/pack/map/encode/dispose、割当/保持byte、copy range/byte、draw/pass、画素一致と失敗/取消/epochを分離評価し、未対応型を矩形へ置換して成功扱いしない。

## 互換性・非目標・別途決定する事項

既存Canvas/DrawList/PreparedDraw2Dの契約は維持する。custom表はFromLegacyで明示接続し、hidden overrideしない。
partial redraw/present、MSDF/atlas、任意shader融合、UI scheduler変更、全frame O(dirty)と全割当排除は非目標。
高速custom consumer登録、generic ABI、capacity/予算/選択係数、sceneのDrawingContent2D受入overloadは後続API reviewで確定する。
