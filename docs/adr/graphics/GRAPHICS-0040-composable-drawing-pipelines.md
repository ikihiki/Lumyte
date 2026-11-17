# ADR-GRAPHICS-0040: 描画種別ごとの構成を一つの renderer にまとめる

- 状態: 採用
- 日付: 2026-11-17
- 基準コミット: `06c3f3d71aab42d4913d4634673fdc8e0162e495`
- 変更関係: 一部変更
- 変更元: [GRAPHICS-0018 の命令別配送](GRAPHICS-0018-2d-graphics-engine.md#命令別interfaceとdi)
- 変更対象: renderer の構築に命令種別別の Composition 宣言と明示共有を追加する。
- 継続する判断: Canvas、既存の命令別 interface、CPU clip の数値規則、描画順、準備結果と資源所有権を維持する。

## 背景

renderer の作者は矩形とパスなどの実装を別々に設定しつつ、共通の管理対象を一つの instance にまとめたい。すべての描画をすべてのパイプラインへ問い合わせる方式では、無関係な描画種別の条件判定まで実行され、既存の固定配送と異なる責務になる。

基準コミット `06c3f3d71aab42d4913d4634673fdc8e0162e495` の `src/Graphics/Lumyte.Graphics.Drawing2D/Execution/Renderer2D.cs` の `PrepareAsync` と `DrawingCommandRenderers2D` は 11 種類の interface へ連続命令を配送する。同じ SHA の `Renderer2D.Retained.cs` の `IsBuiltinRenderer` は組込みの具体型を識別し、種別をまたぐ `TakeBuiltinBatch` と `PreparedRecordBatch2D` により共通の記録列を作る。単に各 backend を wrapper に入れるだけでは、この結合を失う。

## 決定

構築側は描画種別ごとの宣言を明示し、構築完了時に全宣言を一つの固定配送表と一つの renderer 資源管理へまとめる。同じ種別の中だけで FirstSupported の候補を評価し、共有する backend は graph 内の型付き identity を明示して同じ実 instance を注入する。通常の描画側は IRenderer2D と既存の準備結果だけを使う。

### 利用者の変更前・変更後

変更前は基準コードの固定表を constructor に渡す。device、target、list、cancellationToken は呼出し側が用意する。

```csharp
using Lumyte.Graphics.Drawing2D;

using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
using PreparedDraw2D prepared = await renderer.PrepareAsync(list, target, cancellationToken);
```

変更後は同じ 11 種類を Composition で明示する。以下は今回追加する API による構成であり、すべての単機能 backend を列挙する。resources は呼出し側が選択した DrawingResources2D、resourceOwner と validateResources は既存と同じ所有者と検証 callback である。null の resources から graph を補完しない。

```csharp
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.Pipelines;
using static Lumyte.Graphics.Drawing2D.Pipelines.DrawingCompose;

var bounds = new DrawingRetentionOptions2D
{
    MaxFramesInFlight = 3,
    MaxCachedAssets = 512,
    MaxCachedBytes = 64 * 1024 * 1024
};
var definition = Renderer(argumentTables: DrawingArgumentTableMode.Auto, retention: bounds)
[
    FillRect(BackendBinding2D<IFillRectRenderer2D>.Borrowed(new DefaultFillRectRenderer2D())),
    FillRoundedRect(BackendBinding2D<IFillRoundedRectRenderer2D>.Borrowed(new DefaultFillRoundedRectRenderer2D())),
    FillCircle(BackendBinding2D<IFillCircleRenderer2D>.Borrowed(new DefaultFillCircleRenderer2D())),
    DrawLine(BackendBinding2D<IDrawLineRenderer2D>.Borrowed(new DefaultDrawLineRenderer2D())),
    FillPath(BackendBinding2D<IFillPathRenderer2D>.Borrowed(new DefaultFillPathRenderer2D())),
    StrokePath(BackendBinding2D<IStrokePathRenderer2D>.Borrowed(new DefaultStrokePathRenderer2D())),
    DrawImage(BackendBinding2D<IDrawImageRenderer2D>.Borrowed(new DefaultDrawImageRenderer2D())),
    DrawCoverageMask(BackendBinding2D<IDrawCoverageMaskRenderer2D>.Borrowed(new DefaultDrawCoverageMaskRenderer2D())),
    DrawDistanceField(BackendBinding2D<IDrawDistanceFieldRenderer2D>.Borrowed(new DefaultDrawDistanceFieldRenderer2D())),
    DrawMesh(BackendBinding2D<IDrawMeshRenderer2D>.Borrowed(new DefaultDrawMeshRenderer2D())),
    DrawPaintGraph(BackendBinding2D<IDrawPaintGraphRenderer2D>.Borrowed(new DefaultDrawPaintGraphRenderer2D()))
];
using IRenderer2D renderer = definition.BuildRenderer(
    device, resources, resourceOwner, validateResources);
using PreparedDraw2D prepared = await renderer.PrepareAsync(list, target, cancellationToken);
// Record、Finish、Submit、完了確認、command の破棄後に prepared を破棄する。
```

既存の CreateDefault は互換 factory として残すが、新しい宣言の不足を埋めるためには呼び出さない。標準 backend はそれぞれ既存の単機能部品であり、11 個の子 Renderer2D や device を作るものではない。

### 構成と実行の境界

Lumyte.Composition と generator は宣言、factory、children indexer を生成する。Drawing2D の host は全 root を一緒に検査し、固定の DrawKind2D index に配置して DrawingCommandRenderers2D を作る。実行時は従来の switch が対象 interface を直接選び、root の列挙や無関係な predicate の評価をしない。

全 11 種別をちょうど一度ずつ指定する。欠落、重複、null の backend、共有名の衝突、不一致 device、無効な予算と引数表設定を構築前に拒否する。異種 children を C# の型だけで完全検証したとは扱わず、host が構築時に対応集合を照合する。

RouteContent2D は参照型で、配列をコピーし、null・重複 instance・別親所有を全件検証した後に親所有を確定する。一度の Initialize だけを許可する。最初の DescribeDataflow または BuildRenderer が device、明示した DrawingResources2D と宣言集合を固定し、異なる device への再利用と追加初期化を拒否する。required init 設定と不変の binding 値を使い、候補配列もコピーする。

### 同じ種別だけの FirstSupported

FillRect の例では矩形の候補だけを評価する。FillPath などの宣言は候補選択に参加しない。

```csharp
IFillRectRenderer2D selected = FirstSupported2D.FillRect(
    new((context, commands) => SupportsFastRect(context, commands), fastRect),
    new((context, commands) => SupportsGeneralRect(context, commands), generalRect));
var route = FillRect(BackendBinding2D<IFillRectRenderer2D>.Borrowed(selected));
```

SupportsFastRect と SupportsGeneralRect は呼出し側が指定する純粋な能力・状態判定であり、GPU 確保、書込み、記録、送信を行わない。判定単位は従来と同じ clip を共有する連続同種 batch とする。最初の true の backend を一回だけ PrepareAsync し、すべて false なら NotSupportedException とする。predicate の例外、不正入力、資源失効、実確保失敗、device failure を非対応に変換せず、次候補へ進めない。

矩形の適格性は paint、clip、transform を含む実入力で判断する。基準 SHA の `src/Graphics/Lumyte.Graphics.Drawing2D/Model/FillRectCommand2D.cs` は Rect/Paint/State を持つ。矩形を path に変換する backend を作者が用意する場合も同じ塗り領域と数値順序を維持し、塗りを輪郭に変えない。基準 SHA の `StrokeStyle2D.cs` は width/cap/join/miter のみを持つため、存在しない dash を推測しない。

### 設定の分割と実 instance の共有

同じ型という理由では共有しない。SharedBackend2D の同じ宣言を複数の BackendBinding2D から参照した場合だけ、一つの BuildRenderer の scope で factory を一回呼び、その実 instance を注入する。同名で異なる共有宣言は設定の不一致として拒否する。名前は graph 内だけで有効で、static registry、型名 singleton、弱参照による暗黙統合を設けない。

同じ作者の PathManager が IFillPathRenderer2D と IStrokePathRenderer2D を実装する場合は、次の二宣言で同じ実 manager を使う。factory と PathManager の実装は作者が用意する。

```csharp
var paths = new SharedBackend2D<PathManager>("paths", device => CreatePathManager(device));
var fill = FillPath(BackendBinding2D<IFillPathRenderer2D>.Shared(paths, manager => manager));
var stroke = StrokePath(BackendBinding2D<IStrokePathRenderer2D>.Shared(paths, manager => manager));
```

別の BuildRenderer 呼出しでは新しい scope と instance を作る。factory の戻り値と明示した resourceOwner が同じ実 instance の場合、構築成功時だけ外部 owner の一つの解放経路へ移す。構築失敗ではその caller 所有権を消費しない。異なる share key が同じ実 instance を返した場合も解放責任だけを参照 identity で一つにし、論理設定や型による実行の統合はしない。既存の借用 instance を明示する Borrowed は破棄しない。Shared の factory が返す instance は renderer 所有であり、host が direct の準備結果、retained の batch run、Graph の準備 owner に独立した使用権を持たせる。custom backend が context.RetainResources を呼ばなくても、記録と batch の破棄が成功するまで factory 所有の backend を保持する。回収に失敗した batch が残る間は使用権も保持し、retry 成功後に返す。context.RetainResources は従来の shader 等の借用資源を保持する用途として維持し、新しい backend 実装義務にはしない。

renderer ごとの DrawingResourceState2D、RetainedDrawingCache2D、所有者 ledger は一つとする。DrawingArgumentPlan2D は各 PrepareAsync ごとに一つで、全種別が同じ context を通じて登録し、最後に SealAsync する。別 frame の可変 scratch、mask、出力、登録中の引数表を共有しない。読取り geometry の世代共有や copy-on-write は個別 backend の不変資源契約に従う。

この共有は backend/service の構築単位であり、複数種別の全入力を収集して一回だけ評価する汎用 Share stage ではない。そのような一回評価、任意の CPU/GPU DAG、Compute/Sparse 方式、Fork/Join、fixture import は今回の API として公開しない。

### 組込みの実処理と結合

組込み leaf は元の DefaultFillRectRenderer2D などの実 instance のまま固定表へ入れる。IsBuiltinRenderer の具体型識別を壊す汎用 wrapper は挿入しない。連続した互換組込み命令は種別が異なっても TakeBuiltinBatch に入り、PreparedRecordBatch2D が同じ記録列を作る。

基準 SHA の `src/Graphics/Lumyte.Graphics.Drawing2D/Execution/Renderer2D.cs`、`Renderer2D.Retained.cs` と `PreparedRecordBatch2D.cs` の処理順を維持する。CPU 側で論理命令の順に record、geometry、paint と clip の参照を準備し、共通引数計画に登録する。引数表を確定し、準備結果の Record で必要な転送と draw を記録する。従来の fragment で形状 coverage と paint を評価する融合 pass を、存在しない独立 Coverage prepass に置き換えて説明しない。

custom leaf と明示 FirstSupported adapter は元の typed PrepareAsync を実行する。custom kind を組込みと誤認して bypass せず、隣の組込み区間だけを結合する。候補を包む adapter 自体は組込み具体型ではないため、その選択 batch の cross-kind 結合は行わない。Painter order、clip 境界、途中失敗時の既存 batch の回収を維持する。

### 同じ凍結構成からの処理一覧

DescribeDataflow は BuildRenderer が使う凍結 route 配列から、kind、入力 command 型、元の backend interface、共有名、具体 backend または宣言された manager 型、明示 FirstSupported の候補順位、CPU/転送/GPU 契約を返す。inspection は factory を実行せず、資源 owner を移動しない。静的な処理契約は実行済み pass 数や入力ごとの選択結果ではない。

公開する粒度は今回移植する typed backend 境界である。backend 内の任意 shader や CPU algorithm の分解、候補 predicate の実行結果、転送量を inspection から推測して表示しない。

### 公開 API 差分

以下は基準 SHA から追加・変更する宣言の抜粋である。生成 factory は既存 generator を使用し、generic factory property を新設しない。

```diff
 namespace Lumyte.Graphics.Drawing2D
 {
+    public interface IRenderer2D : IDisposable
+    {
+        IGraphicDevice Device { get; } // 借用 device。
+        DrawingRetentionStatistics2D RetentionStatistics { get; }
+        ValueTask<PreparedDraw2D> PrepareAsync(DrawList2D list, RenderTarget2D target,
+            CancellationToken cancellationToken = default);
+        ValueTask<PreparedDrawingFrame2D> PrepareAsync(DrawingSceneReadLease2D source,
+            RenderTarget2D target, CancellationToken cancellationToken = default);
+        ValueTask<PreparedDrawingFrame2D> PrepareAsync(DrawingSnapshot2D source,
+            RenderTarget2D target, CancellationToken cancellationToken = default);
+    }
-    public sealed partial class Renderer2D : IDisposable
+    public sealed partial class Renderer2D : IRenderer2D
     {
+        public IGraphicDevice Device { get; }
         // 従来の 5 引数・6 引数 constructor と準備・破棄の契約を維持。
     }
 }
+namespace Lumyte.Graphics.Drawing2D.Pipelines
+{
+    public sealed class SharedBackend2D<T>(string name, Func<IGraphicDevice, T> create)
+        where T : class, IDisposable;
+    public sealed class BackendBinding2D<T> where T : class
+    {
+        public static BackendBinding2D<T> Borrowed(T backend);
+        public static BackendBinding2D<T> Shared<TShared>(SharedBackend2D<TShared> shared,
+            Func<TShared, T> select) where TShared : class, IDisposable;
+    }
+    public sealed record CommandCandidate2D<TCommand, TBackend>(
+        Func<RenderPreparationContext2D, ReadOnlyMemory<TCommand>, bool> Supports,
+        TBackend Backend) where TCommand : DrawingCommand2D where TBackend : class;
+    public static class FirstSupported2D
+    {
+        public static IFillRectRenderer2D FillRect(params
+            CommandCandidate2D<FillRectCommand2D, IFillRectRenderer2D>[] candidates);
+        // 残り 10 種別も元の interface と command 型を一対一に対応させる。
+    }
+    public static partial class DrawingCompose
+    {
+        public static partial class Definitions
+        {
+            [Composable(Factory = "DrawingCompose")]
+            public partial class Renderer : IDisposable
+            {
+                [ComposeParameter] public required DrawingRetentionOptions2D Retention { get; init; }
+                [ComposeParameter] public required DrawingArgumentTableMode ArgumentTables { get; init; }
+                [ComposeContent] public RouteContent2D Children { get; } = new();
+                public IReadOnlyList<CommandRouteDescription2D> DescribeDataflow(IGraphicDevice device, DrawingResources2D resources);
+                public void Dispose(); // 未公開 scope の回収を再試行。構築済み renderer は独立。
+                // resources 必須。成功した構築だけが resourceOwner を引き取る。
+                public IRenderer2D BuildRenderer(IGraphicDevice device, DrawingResources2D resources,
+                    IDisposable? resourceOwner, Action? validateResources);
+            }
+            [Composable(Factory = "DrawingCompose")]
+            public partial class FillRect : CommandRoute2D
+            {
+                [ComposeParameter] public required BackendBinding2D<IFillRectRenderer2D> Backend { get; init; }
+            }
+            // 残り 10 種別は上の完全構成例と同じ型付き宣言。
+        }
+    }
+}
 namespace Lumyte.Graphics.Drawing2D.RenderGraph
 {
     public sealed class RenderGraphRenderer2D
     {
+        // 同じ table と資源 coordinator を借用。未対応実装は graph 変更前に拒否。
+        public RenderGraphRenderer2D(IRenderer2D renderer, RenderGraphDrawList2D list,
+            RenderTargetBinding2D target, DrawingGraphBindings2D bindings);
     }
 }
```

### Graph 接続と寿命

Graph 側は構築済み IRenderer2D を新 overload へ渡す。Drawing2D 内の friend interface が元の固定表と resource coordinator を接続し、公開 consumer runner や具体型への downcast を要求しない。元の table constructor と source factory callback、graph-local handle の所有権と記録 callback の guard は維持する。借用した host 間で backend の可変状態を同時使用しないよう、一つの renderer とそれから借用した Graph host の準備は呼出し側が直列化する。未対応の外部 IRenderer2D と異なる device は graph を変更する前に拒否する。source の破棄後も有効な host 借用を layer と backdrop の全 compositor に伝え、型付き描画だけを例外扱いしない。source と Graph host の両方を破棄しても、準備済み owner が参照する backend はその batch の回収完了まで保持する。

基準 SHA の `src/Graphics/Lumyte.Graphics.Drawing2D/Execution/PreparedDraw2D.cs` と `PreparedDrawingFrame2D.cs` の既存の所有権を維持する。retained frame は Record、Finish、Submit、AttachSubmission、実完了確認、command 破棄、Retire、Dispose の順に扱う。未記録は Dispose、記録済みで確実に未送信なら command 破棄後に AbortUnsubmitted を使う。

待機キャンセルを GPU の取消しとみなさない。Submit の受理が不明なら frame と参照資源を隔離し、実完了または確認済み device 世代退役まで保持する。renderer の破棄は生きている準備結果の資源を先に解放しない。構築途中の失敗で取得済み shared instance を回収し、回収失敗は定義が retry owner を保持して次回の構築前に再試行する。

## 検討した代替案

- 全描画を全 root の FirstSupported に渡す: 一つの候補列で表現できるが、無関係な種別の判定まで呼ばれる。先に固定 kind 配送を行う。
- 種別ごとに独立した Renderer2D を作る: 設定は分かれるが、共通引数表と順序付き記録の結合が失われる。全 root を一つの coordinator へ compile する。
- 同じ型の manager を自動的に一つにする: 設定や device 世代、所有者が異なる instance を誤って統合する。明示した graph-local identity のみを共有する。
- 現行実装を任意 CPU/GPU stage に分解したとみなす: 存在しない物理 pass や実行能力を保証してしまう。今回の境界は既存 typed backend とその共有・選択に限定する。

## 結果と影響

構築側は 11 種類と共有 identity を明示する責務を負うが、描画側は既存の Canvas と準備結果をそのまま使える。種別の設定が分かれても資源管理と Painter order は一つであり、組込みの結合と custom replacement を両立する。

受入れ条件は、全種別の元 interface の実呼出し、無関係な候補呼出しがゼロ、同種の選択順序、実 manager の共有利用と一回破棄、別構築の分離、途中失敗と retry、組込みの結合数と GPU 転送の同等性で確認する。direct・retained・Graph の資源寿命、既存 pixel oracle、AOT と trimming の契約も維持する。性能値や速度向上はこの判断の保証に含めない。
