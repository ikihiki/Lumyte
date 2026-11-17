# ADR-GRAPHICS-0040: パイプラインを構成して共通の IRenderer2D を作る

- 状態: 提案
- 日付: 2026-11-17
- 基準コミット: `06c3f3d71aab42d4913d4634673fdc8e0162e495`
- 変更関係: 一部変更
- 変更元: [GRAPHICS-0018 の命令別配送](GRAPHICS-0018-2d-graphics-engine.md#命令別interfaceとdi)、[GRAPHICS-0033 の CPU 正規化](GRAPHICS-0033-drawing2d-normalized-clips.md#分類とスコープ)
- 変更対象: renderer の構築境界に型付きパイプラインと共通 interface を追加し、専用構成では内部記録形式と clip の評価場所を選べるようにする。
- 継続する判断: Canvas、命令別 renderer、標準構成の CPU clip 数値規則、描画順、既存の準備結果と資源所有権を維持する。

## 背景

renderer の作者は、矩形・パス・文字の個別実装だけでなく、CPU の形状処理、GPU coverage、全シーン compute などを組み合わせて renderer を作りたい。描画するアプリケーションには、その違いによらず Canvas と同じ準備・実行手順を使わせたいが、現在の固定配送だけでは方式ごとに異なる処理粒度を構成できない。

基準コミット `06c3f3d71aab42d4913d4634673fdc8e0162e495` の `src/Graphics/Lumyte.Graphics.Drawing2D/Execution/Renderer2D.cs` の `PrepareAsync` は連続命令を `DrawingCommandRenderers2D` に配送する。`src/Graphics/Lumyte.Graphics.Drawing2D/Model/Canvas2D.cs` の `End()` は `DrawList2D` を返す。`src/Graphics/Lumyte.Graphics.Drawing2D/Execution/Renderer2D.Retained.cs` は scene の read lease と snapshot を受け取る overload を持つ。この基準の `Renderer2D` は具体型であり、以下の共通 interface は本提案による追加である。

## 決定

型付きの部品と処理グラフを構築時に組み合わせ、最終的に `IRenderer2D` を返す factory を追加する。パイプラインを扱うのは renderer の作者と構築コードであり、通常の描画コードは Canvas、`PrepareAsync`、既存の準備結果を使い続ける。方式ごとの記録変換と CPU・GPU の処理粒度を選べるようにしながら、その実行計画や中間データをアプリケーションへ渡さない。

### 利用者の変更前・変更後

例 1 は同じ矩形を描く比較である。変更前は基準コードの API、変更後と以降の宣言・構成例は提案である。device、target、rect、paint、cancellationToken は呼出し側の入力、options は後述の PipelineOptions とし、target の状態遷移と command・queue の生成は省略する。

```csharp
using Lumyte.Graphics.Drawing2D;

var canvas = new Canvas2D();
canvas.Begin();
canvas.FillRect(rect, paint);
DrawList2D list = canvas.End();
using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
PreparedDraw2D prepared = await renderer.PrepareAsync(list, target, cancellationToken);
// prepared.Record(commands)、Finish、Submit、完了、commands.Dispose、prepared.Dispose。
```

変更後は構築箇所だけで方式を選ぶ。共通 interface の導入に伴う受取り型の一般化はあるが、描画内容と `PrepareAsync` の引数・返り値・意味は変えない。

```csharp
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.Pipelines;

using IRenderer2D renderer = PipelinePresets.Classic().BuildRenderer(device, options);
var canvas = new Canvas2D();
canvas.Begin();
canvas.FillRect(rect, paint);
DrawList2D list = canvas.End();
PreparedDraw2D prepared = await renderer.PrepareAsync(list, target, cancellationToken);
// 同じ Record・送信・完了後解放の手順。CPU/GPU 中間段階を呼び出さない。
```

以下は全構成に共通の描画関数である。`RenderTarget2D` と `DrawList2D` は既存型であり、別の command hierarchy を導入しない。

```csharp
using System.Threading;
using System.Threading.Tasks;
using Lumyte.Graphics.Drawing2D;

static ValueTask<PreparedDraw2D> PrepareDrawingAsync(IRenderer2D renderer,
    DrawList2D list, RenderTarget2D target, CancellationToken ct)
    => renderer.PrepareAsync(list, target, ct);
```

### 構成の境界と記録の意味

互換入力は Canvas が記録した論理描画、scene read lease、snapshot である。この論理境界を、全方式共通の内部 IR や CPU 平坦化済み geometry と取り違えない。構築時の recording adapter が論理命令から方式専用の型付き IR へ変換する。連続 batch 化、曲線の保持、strip 化、filter と lowering の融合を行え、全方式へ重複した CPU の全量 DrawList を追加生成させない。

recording adapter を変更してもアプリケーションの Begin・FillRect・End は変えない。retained 入力は既存の凍結済み snapshot と資源 lease を使い、read lease は PrepareAsync の await 完了まで借用するだけとする。非同期完了後まで caller の可変配列や期限切れ guard を保持せず、独立所有または明示した不変共有 lease により内部 IR を保持する。

処理グラフは任意個の CPU・GPU 段階を持つ。直列接続、CPU 分岐、GPU 分岐、join、全シーン scan を型付き部品として構成でき、全方式へ固定した段階数や命令別 callback を強制しない。全 compute の交換単位はシーン全体の geometry・binning・scan・compositor であり、矩形の raster callback ではない。

型の一致は接続可能性の一条件である。構築時に shader ABI、binding layout、数値規則、サンプル格子、DPR、原点、extent、色空間、alpha、fill・stroke・group opacity、論理描画順、機能、有限予算を検証する。入力依存の count・offset・世代は各 PrepareAsync で追加検証し、不適合は GPU 確保・記録前に拒否する。未対応機能を黙って省略せず、明示した品質条件を満たす構成への再構築、または例外とする。

### 公開 API 差分の正本

基準コードに対する抜粋を以下に示す。C# の説明用宣言であり、コンパイル済み実装ではない。共通の名前解決には System、System.Threading、System.Threading.Tasks、Lumyte.Graphics.Abstractions、Lumyte.Graphics.Drawing2D を使う。既存 constructor の省略可能引数、custom shader と資源の契約は変更しない。

```diff
 namespace Lumyte.Graphics.Drawing2D
 {
+    // 通常の描画側が受け取る共通境界。内部 Submit/Wait は行わない。
+    public interface IRenderer2D : IDisposable
+    {
+        IGraphicDevice Device { get; } // 借用。renderer は破棄しない。
+        DrawingRetentionStatistics2D RetentionStatistics { get; }
+        // 一回の記録用 owner。完了と command 破棄後に Dispose する。
+        ValueTask<PreparedDraw2D> PrepareAsync(DrawList2D list, RenderTarget2D target,
+            CancellationToken cancellationToken = default);
+        // source は await 完了まで借用。返却 frame は既存の Attach/Retire 契約。
+        ValueTask<PreparedDrawingFrame2D> PrepareAsync(DrawingSceneReadLease2D source,
+            RenderTarget2D target, CancellationToken cancellationToken = default);
+        ValueTask<PreparedDrawingFrame2D> PrepareAsync(DrawingSnapshot2D source,
+            RenderTarget2D target, CancellationToken cancellationToken = default);
+    }
-    public sealed partial class Renderer2D : IDisposable
+    public sealed partial class Renderer2D : IRenderer2D
     {
+        public IGraphicDevice Device { get; }
         public Renderer2D(IGraphicDevice device, DrawingCommandRenderers2D renderers,
             DrawingResources2D? resources = null, IDisposable? resourceOwner = null,
             Action? validateResources = null, DrawingRetentionOptions2D? retentionOptions = null);
         public Renderer2D(IGraphicDevice device, DrawingCommandRenderers2D renderers,
             DrawingResources2D? resources, IDisposable? resourceOwner, Action? validateResources);
         // 既存の三つの PrepareAsync、RetentionStatistics、Dispose は維持する。
     }
 }
+namespace Lumyte.Graphics.Drawing2D.Pipelines
+{
+    // 以下は構築専用。実行 context を通常の描画側に要求しない。
+    public sealed record PipelineOptions
+    {
+        public long MaxWorkingBytes { get; init; } = 256L * 1024 * 1024; // 正数。
+        public DrawingRetentionOptions2D Retention { get; init; } = new();
+        public DrawingResources2D? Resources { get; init; }
+        public IDisposable? ResourceOwner { get; init; } // 成功した構築だけが移動する。
+        public Action? ValidateResources { get; init; }
+    }
+    public interface IRecordingAdapter<TInput> { }
+    public interface IPipelineStage<TInput, TOutput> { }
+    public sealed class RenderingPipeline<TInput, TOutput>
+    {
+        public RenderingPipeline(IRecordingAdapter<TInput> recording,
+            IPipelineStage<TInput, TOutput> stages);
+        public RenderingPipeline<TInput, TNext> Then<TNext>(
+            IPipelineStage<TOutput, TNext> next);
+        // 最終出力の ColorTarget2D schema を検証。他の最終型は構築時に拒否。
+        // options を snapshot 化。失敗時には caller の ResourceOwner を取得しない。
+        public IRenderer2D BuildRenderer(IGraphicDevice device, PipelineOptions options);
+    }
+    public static class PipelinePresets
+    {
+        public static RenderingPipeline<LogicalBatches2D, ColorTarget2D> Classic(
+            ClassicParts? parts = null);
+        public static RenderingPipeline<RawCurves2D, ColorTarget2D> Compute(
+            IPipelineStage<SceneGeometry2D, SceneBins2D>? binner = null,
+            IPipelineStage<ScannedScene2D, ColorTarget2D>? compositor = null);
+    }
+    public sealed record ClassicParts(
+        IPipelineStage<RectBatch2D, RasterBatch2D> Rect,
+        IPipelineStage<PathBatch2D, RasterBatch2D> Path,
+        IPipelineStage<TextBatch2D, RasterBatch2D> Text);
+    public static class PipelineParts
+    {
+        public static IRecordingAdapter<LogicalBatches2D> LogicalRecording();
+        public static IRecordingAdapter<RawCurves2D> RawCurveRecording();
+        public static IRecordingAdapter<CompactScene2D> CompactRecording();
+        public static IPipelineStage<CompactScene2D, RawCurves2D> DecodeCurves();
+        public static IPipelineStage<RectBatch2D, RasterBatch2D> RectInstances();
+        public static IPipelineStage<PathBatch2D, RasterBatch2D> CpuPathTessellation();
+        public static IPipelineStage<PathBatch2D, RasterBatch2D> AnalyticPathShader();
+        public static IPipelineStage<TextBatch2D, RasterBatch2D> TextAtlas();
+        public static IPipelineStage<RawCurves2D, CpuGeometry2D> CpuGeometry();
+        public static IPipelineStage<CpuGeometry2D, PackedGeometry2D> Pack();
+        public static IPipelineStage<PackedGeometry2D, Coverage2D> UploadAndCoverage();
+        public static IPipelineStage<Coverage2D, ColorTarget2D> RasterPaint();
+        public static IPipelineStage<RawCurves2D, SparseStrips2D> CpuSparseStrips();
+        public static IPipelineStage<SparseStrips2D, ColorTarget2D> GpuStripComposition();
+        public static IPipelineStage<CpuGeometry2D, ColorTarget2D> MaskPaintBranches();
+        public static IPipelineStage<TIn, TOut> Cpu<TIn, TOut>(
+            CpuStageDefinition<TIn, TOut> definition);
+        public static IPipelineStage<TIn, TOut> Gpu<TIn, TOut>(
+            GpuStageDefinition<TIn, TOut> definition);
+        public static IPipelineStage<TIn, TOut> ForkJoin<TIn, TA, TB, TOut>(
+            IPipelineStage<TIn, TA> a, IPipelineStage<TIn, TB> b,
+            GpuJoinDefinition<TA, TB, TOut> join);
+    }
+    // 全フィールド・配置・数値契約・有限展開上限を記述する不変 schema。
+    public sealed record TypedSchema<T>(ReadOnlyMemory<byte> LayoutAndSemantics);
+    public sealed record CpuStageDefinition<TIn, TOut>(TypedSchema<TIn> Input,
+        TypedSchema<TOut> Output, Func<TIn, TOut> Transform, long MaxWorkingBytes);
+    public sealed record ResourceRange(int Slot, long Offset, long Length);
+    public sealed record GpuKernelDefinition(ReadOnlyMemory<byte> Shader, string EntryPoint,
+        ReadOnlyMemory<byte> BindingAbi, ReadOnlyMemory<ResourceRange> Reads,
+        ReadOnlyMemory<ResourceRange> Writes, ulong RequiredCaps, long MaxWorkingBytes);
+    public sealed record GpuStageDefinition<TIn, TOut>(TypedSchema<TIn> Input,
+        TypedSchema<TOut> Output, GpuKernelDefinition Kernel);
+    public sealed record GpuJoinDefinition<TA, TB, TOut>(TypedSchema<TA> A,
+        TypedSchema<TB> B, TypedSchema<TOut> Output, GpuKernelDefinition Kernel);
+}
```

LogicalBatches2D、RawCurves2D、CompactScene2D、各 batch、geometry、strip、coverage、bin、scan、ColorTarget2D は構築用の型付きデータ schema を表す抜粋名である。各 schema は論理描画番号と依存版を持ち、CPU 成果物は backend handle を持たない。coverage は画素・サンプルと paint の評価に必要な論理参照を保持する。最終出力は target の Load/Clear と色・alpha・extent を満たす書込み計画を意味し、アプリケーションへ返す画像 owner ではない。

Compute は raw curves→GPU geometry→指定 binner→scan→指定 compositor を接続する。省略時は各位置の標準実部品を使う。Classic は省略時に矩形 instance・CPU path tessellation・text atlas を使い、表にない命令の標準処理を維持する。Then は既存グラフと次の部品をつなぐだけで、内部の分岐や段階数を固定しない。

### 部品の定義と内部接続

marker interface は入出力型の関連を公開し、任意の外部実装を呼ぶ実行 vtable ではない。外部作者は CpuStageDefinition、GpuStageDefinition、GpuJoinDefinition と PipelineParts により実装を注入する。未知の marker 実装は BuildRenderer で拒否する。独自 recording adapter の外部実装 protocol は対象外とし、示した adapter は提供側の構築部品とする。CPU delegate は入力を呼出し中だけ借用し、出力は独立所有または不変共有 lease を持つ。

構築時に shader、ABI、range、schema の byte 列をコピーして所有し、その後の caller の変更を参照しない。entry point はバイナリ内の入口名であり、部品探索用の文字列 registry ではない。型一致とは別に slot・用途・範囲・barrier・有限展開量を検証する。custom provider はこの定義値を返せばよく、新たな shader module 管理基盤を要求しない。C# の型は shader をコンパイルせず、ABI の適合性を証明しない。不適合は ArgumentException、機能・予算超過は NotSupportedException、期限切れ所有権は InvalidOperationException とし、部分 renderer を公開しない。native 確保後の失敗も取得済み私有資源を回収する。

構築済み renderer は Drawing2D assembly 内で入力変換、計画、資源 owner、実行 bridge を所有する。通常の public 戻り値をたどっても、計画 context、初期化 token、汎用 runner、新しい frame 型へ到達しない。既存の PreparedDraw2D と PreparedDrawingFrame2D の内部 batch を拡張し、compute を含む合法な command と target 遷移を記録できるようにする。これは内部契約の追加であり、interface の利用者に allocator、format cast、追加 abstract member を要求しない。

CPU ジョブ配送は基準 SHA の `src/Core/Lumyte.Execution/CommandPort.cs`、`src/Core/Lumyte.Execution/WorkflowPort.cs`、`src/Core/Lumyte.Execution/ModuleBuilder.cs` の既存 port と workflow を使える。資源ロードは `src/Core/Lumyte.Resources/IResourceTransform.Typed.cs` の既存 transform と manager に任せる。GPU 計画の依存を、別の scheduler やロード完了 token へ置き換えない。

### 完全なパイプラインの構成例

例 2〜11 は renderer の構築コードである。各片は独立し、共通に `using Lumyte.Graphics.Drawing2D;` と `using Lumyte.Graphics.Drawing2D.Pipelines;` を使う。device は借用 device、options は PipelineOptions とし、ResourceOwner を指定する場合は各成功構築へ別の owner を用意する。全例の結果は同じ PrepareDrawingAsync に渡せる IRenderer2D である。

例 2: 矩形 instance、CPU path tessellation、文字 atlas の従来型。

```csharp
var parts = new ClassicParts(PipelineParts.RectInstances(),
    PipelineParts.CpuPathTessellation(), PipelineParts.TextAtlas());
using IRenderer2D renderer = PipelinePresets.Classic(parts).BuildRenderer(device, options);
```

例 3: パスだけを解析的 shader へ交換する。矩形と文字は同じ部品を使う。

```csharp
var parts = new ClassicParts(PipelineParts.RectInstances(),
    PipelineParts.AnalyticPathShader(), PipelineParts.TextAtlas());
using IRenderer2D renderer = PipelinePresets.Classic(parts).BuildRenderer(device, options);
```

命令別部品は同じ種類の連続範囲だけを処理し、半透明文字 B を挟む矩形 A・C を A・C・B に並べ替えない。文字 shaping は再実行せず記録済み glyph を使う。

例 4: CPU geometry、GPU coverage、raster paint の混成型。

```csharp
using IRenderer2D renderer = new RenderingPipeline<RawCurves2D, CpuGeometry2D>(
    PipelineParts.RawCurveRecording(), PipelineParts.CpuGeometry())
    .Then(PipelineParts.Pack()).Then(PipelineParts.UploadAndCoverage())
    .Then(PipelineParts.RasterPaint()).BuildRenderer(device, options);
```

例 5: CPU が疎な strip を作り、GPU が順序付きに合成する。

```csharp
using IRenderer2D renderer = new RenderingPipeline<RawCurves2D, SparseStrips2D>(
    PipelineParts.RawCurveRecording(), PipelineParts.CpuSparseStrips())
    .Then(PipelineParts.GpuStripComposition()).BuildRenderer(device, options);
```

例 6: raw curves を GPU geometry・bin・scan・大域 compositor へ渡す全 compute 型。

```csharp
using IRenderer2D renderer = PipelinePresets.Compute().BuildRenderer(device, options);
```

例 7: 全 compute の binner を独自 shader 定義へ交換する。geometrySchema と binsSchema は対応する TypedSchema、customShader・customAbi は byte 列、reads・writes は ResourceRange 列、requiredCaps は ulong、maxWorkingBytes は正の long とする。

```csharp
var kernel = new GpuKernelDefinition(customShader, "BinScene", customAbi,
    reads, writes, requiredCaps, maxWorkingBytes);
var binner = PipelineParts.Gpu(new GpuStageDefinition<SceneGeometry2D, SceneBins2D>(
    geometrySchema, binsSchema, kernel));
using IRenderer2D renderer = PipelinePresets.Compute(binner: binner)
    .BuildRenderer(device, options);
```

同様に GpuStageDefinition<ScannedScene2D, ColorTarget2D> から作る実部品を compositor 引数へ渡せる。bin 配置、scan の大域依存、論理番号、描画順、色の評価契約の一致を構築時に検査する。

例 8: 構築時の recording adapter と encoder を compact IR に変更する。アプリケーション側の Canvas は同じである。

```csharp
using IRenderer2D renderer = new RenderingPipeline<CompactScene2D, RawCurves2D>(
    PipelineParts.CompactRecording(), PipelineParts.DecodeCurves())
    .Then(PipelineParts.CpuSparseStrips()).Then(PipelineParts.GpuStripComposition())
    .BuildRenderer(device, options);
```

例 9: CPU 準備済み geometry を固定入力として結び、packing 以降を検証する。

```csharp
using Lumyte.Graphics.Drawing2D.Pipelines.Testing;

using IRenderer2D renderer = FixturePipelines.FromCpuGeometry(geometryFixture,
    expectedInput, PipelineParts.Pack()).Then(PipelineParts.UploadAndCoverage())
    .Then(PipelineParts.RasterPaint()).BuildRenderer(device, options);
```

例 10: 計時外で実 upload と完了を確認した GPU fixture から coverage と paint を構成する。

```csharp
using Lumyte.Graphics.Drawing2D.Pipelines.Testing;

using ResidentGeometryFixture resident = await FixturePipelines.UploadAndCompleteAsync(
    device, packedFixture, expectedInput, options, cancellationToken);
using IRenderer2D renderer = FixturePipelines.FromResidentGeometry(resident, expectedInput)
    .Then(PipelineParts.RasterPaint()).BuildRenderer(device, options);
```

この upload helper の options は資源 owner を移動しない検証・予算設定だけを参照する。通常の BuildRenderer と同じ ResourceOwner を二重に取得しない。upload の実完了確認、command 破棄、producer の退役を終えてから handle を返し、consumer の準備・記録はその後に行う。CPU 準備・確保・転送・記録・送信・待機・回収を計時外へ置いた場合も、それを全体改善とは呼ばない。

例 11: 不変 CPU geometry を共有し、mask と paint の GPU 分岐を join する構成。

```csharp
using IRenderer2D renderer = new RenderingPipeline<RawCurves2D, CpuGeometry2D>(
    PipelineParts.RawCurveRecording(), PipelineParts.CpuGeometry())
    .Then(PipelineParts.MaskPaintBranches()).BuildRenderer(device, options);
```

MaskPaintBranches は ForkJoin による型付き定義の標準組合せで、新しい実行器ではない。join は両枝の論理番号、依存版、格子と読み取り範囲を照合し、barrier と painter order の辺を保持する。CPU の独立した geometry・paint 準備も部品内部で分岐できる。

### 固定入力の検証契約

例 9・10 の宣言は次のとおりである。geometryFixture は独立所有の CpuGeometry2D、packedFixture は PackedGeometry2D、expectedInput は期待する論理入力と描画条件を凍結した manifest とする。

```diff
+namespace Lumyte.Graphics.Drawing2D.Pipelines.Testing
+{
+    public sealed class FixtureInputManifest
+    {
+        public static FixtureInputManifest Capture(DrawingSnapshot2D source,
+            RenderTarget2D target);
+    }
+    public sealed class ResidentGeometryFixture : IDisposable { public void Dispose(); }
+    public static class FixturePipelines
+    {
+        public static RenderingPipeline<RawCurves2D, PackedGeometry2D> FromCpuGeometry(
+            CpuGeometry2D geometry, FixtureInputManifest expected,
+            IPipelineStage<CpuGeometry2D, PackedGeometry2D> pack);
+        // テスト支援のみが Submit/Wait する。実 upload の成功・完了後だけ返す。
+        public static ValueTask<ResidentGeometryFixture> UploadAndCompleteAsync(
+            IGraphicDevice device, PackedGeometry2D packed, FixtureInputManifest expected,
+            PipelineOptions options, CancellationToken cancellationToken = default);
+        // 独立 consumer 計画で coverage を行う。upload は含めない。
+        public static RenderingPipeline<RawCurves2D, Coverage2D> FromResidentGeometry(
+            ResidentGeometryFixture fixture, FixtureInputManifest expected);
+    }
+}
```

fixture renderer も同じ三つの PrepareAsync を受け付ける。ただし全論理入力、clip、paint、glyph・画像参照版、描画順、schema、DPR、原点、extent、格子を manifest と照合し、不一致は GPU 操作前に拒否する。consumer 入力を無視する renderer にはしない。checksum は比較の補助にすぎず、count・offset・資源 lease 検証や GPU 初期化の証拠を代替しない。

upload 用と consumer 用の内部計画 context は別々である。resident は実 producer の記録、送信、正常完了と device 世代を証明する owned handle であり、単なるバッファ参照や Record 済みフラグではない。最終 renderer は fixture と引数表・入力資源の独立 lease を取得するため、元 handle を先に Dispose しても安全である。異なる device、無効な世代、期限切れ lease は拒否する。consumer の mask、scratch、出力は private な書込み領域とし、fixture を上書きしない。

fixture は DTO、手続き的生成、既存 serializer の adapter を使え、新しい保存形式を要求しない。保存する場合は記号資源 ID と schema・数値条件・依存版だけを持ち、native handle と device は保存せず、再束縛時に全検証する。

CPU fixture は構築時に全範囲を検証して独立所有または不変共有 lease を取得する。readback を行うテスト支援は出力 allocation の owner を保持し、独立読取り lease の最後の解放まで存続させる。実行資源の回収と出力 owner の破棄を分けるが、通常の準備結果に新しい出力借用 API は追加しない。期待値は対象実装の出力コピーではなく独立した oracle で作る。

### 通常の実行と寿命

例 12: どの構成でも retained 描画の実行は同じである。drawingSnapshot は caller が await 完了まで保持する既存 snapshot、commands は記録中、queue は同じ device のものとする。scene read lease も同じ PrepareAsync 手順を使う。

```csharp
PreparedDrawingFrame2D frame = await renderer.PrepareAsync(
    drawingSnapshot, target, cancellationToken);
frame.Record(commands);
commands.Finish();
var actual = queue.Submit([commands]);
frame.AttachSubmission(actual);
await actual.WaitAsync(CancellationToken.None);
commands.Dispose();
frame.Retire();
frame.Dispose();
actual.Dispose();
```

基準 SHA の `src/Graphics/Lumyte.Graphics.Drawing2D/Execution/PreparedDraw2D.cs` と `src/Graphics/Lumyte.Graphics.Drawing2D/Execution/PreparedDrawingFrame2D.cs` の所有権を維持する。一回描画の PreparedDraw2D は完了と command 破棄後に Dispose し、retained frame と同じ型・手順とみなさない。retained は未記録なら Dispose、記録後で確実に未送信なら command 破棄後に AbortUnsubmitted を使う。

待機キャンセルは GPU の取消しではない。送信後は actual と owner を保持して完了確認を再開する。Submit が例外を投げて受理が不明な場合、未送信扱いにせず command・frame・参照資源を隔離し、実完了または確認済み device 世代退役まで保持する。一般の Dispose や失敗した submission を正常完了の証拠にしない。

不変 geometry は読取り lease で共有でき、frame ごとの書込み領域は分離する。更新は別版または copy-on-write とし、古い frame の生存領域を上書きしない。既存の host frame 数と max-in-flight 上限を変更しない。構築成功時だけ ResourceOwner を移動し、部分失敗で取得した私有資源は factory が回収する。回収失敗の owner と retry を失わず、caller 所有資源の二重解放を避ける。

### 同じ renderer の Graph 接続

Graph 呼出し側も構成済み IRenderer2D を渡すだけとする。基準 SHA の `src/Graphics/Lumyte.Graphics.Drawing2D.RenderGraph/Drawing2DRenderGraphExtensions.cs` の Append2D は RenderGraphRenderer2D を受け取り、`src/Graphics/Lumyte.Graphics.Drawing2D.RenderGraph/RenderGraphRenderer2D.cs` が命令表を保持する。本提案では既存入口を維持し、共通 interface を受け取る overload を追加する。基準 API が既に interface を受け取るという意味ではない。

```diff
 namespace Lumyte.Graphics.Drawing2D.RenderGraph
 {
     public static class Drawing2DRenderGraphExtensions
     {
+        // 同一 device・対応 renderer を Graph 変更前に検証する。
+        // owner は GPU 完了、command と compiled graph の破棄後に解放する。
+        public static ValueTask<PreparedDrawingPasses2D> Append2D(
+            this Lumyte.Graphics.RenderGraph.RenderGraph graph, IRenderer2D renderer,
+            RenderGraphDrawList2D list, RenderTargetBinding2D target,
+            DrawingGraphBindings2D bindings, CancellationToken cancellationToken = default);
     }
 }
```

例 12 の Graph 経路も同じ renderer を使い、既存 Graph のコンパイル・記録・送信手順を変えない。graphList、graphTarget、bindings は同じ graph に属する既存型の入力とする。

```csharp
using Lumyte.Graphics.Drawing2D.RenderGraph;

PreparedDrawingPasses2D passes = await graph.Append2D(renderer,
    graphList, graphTarget, bindings, cancellationToken);
var compiled = graph.Compile();
compiled.Record(commands); // renderer を別途 Record しない。
commands.Finish();
var actual = queue.Submit([commands]);
await actual.WaitAsync(CancellationToken.None);
commands.Dispose();
compiled.Dispose();
graph.Dispose();
passes.Dispose();
actual.Dispose();
```

内部 descriptor と実行 bridge は Drawing2D が所有する。Drawing2D.RenderGraph は基準 SHA の `src/Graphics/Lumyte.Graphics.Drawing2D/Lumyte.Graphics.Drawing2D.csproj` にある friend 境界から取得し、資源範囲、pass、依存、出力を既存 Graph へ宣言する。Drawing2D から Graph への参照や公開計画引数を追加しない。外部の任意 IRenderer2D 実装は隠れた protocol を満たすと約束せず、未対応なら Graph の変更前に拒否する。

基準 SHA の `src/Graphics/Lumyte.Graphics.RenderGraph/IRenderGraphPreparation.cs` と `src/Graphics/Lumyte.Graphics.RenderGraph/RenderGraphPreparationContext.cs` の live pass 情報だけでは Graph 全体の記録成否を証明できない。`src/Graphics/Lumyte.Graphics.RenderGraph/Lumyte.Graphics.RenderGraph.csproj` の friend 境界を使い、adapter 向け内部通知を追加する。

```diff
 namespace Lumyte.Graphics.RenderGraph
 {
     public sealed class RenderGraph : IDisposable
     {
+        // Compile 前に登録し compiled owner へ移動。end は例外時も必ず通知する。
+        internal void ObserveRecording(Action<IGraphicsCommandBuffer> begin,
+            Action<IGraphicsCommandBuffer, Exception?> end, Action disposed);
     }
 }
```

bridge は必要な生存 producer 集合、実際の command 同一性、全 Record の成否、callback owner の破棄を対応付ける。直接実行と Graph 実行は排他的とし、二重記録を拒否する。Append 途中失敗では graph を捨て、未送信なら callback と command の破棄後に回収する。不明な送信結果では隔離を維持する。

Graph 経路で実 submission の正常完了通知を得ない限り、cross-frame GPU residency を公開しない。culling、未実行、中断、producer の部分成功、単なる Dispose を初期化証拠にしない。fixture upload helper は直接経路で実完了を確認する。Graph の一時 allocation を compiled.Dispose 後まで所有すると仮定せず、テスト用出力共有には renderer 側 owner の allocation を import し、独立 lease を保持する。

## 検討した代替案

- 命令別表だけをジェネリックにする: 従来型の局所交換には適するが、全シーン compute と方式専用 IR を表せない。表は従来型の構成部品として残す。
- 描画側に段階の実行を公開する: 中間入力は直接渡せるが、方式ごとに手順・資源寿命が変わる。固定入力も renderer を構築する境界へ置く。
- 一つの mode 列挙で内部を選ぶ: 選択は簡単だが、型付き独自部品・分岐・join を表せない。preset は部品の実オブジェクトを受け取る。
- 全方式を共通 CPU 平坦化と固定段階へ正規化する: 境界は揃うが、生曲線からの GPU 処理と strip 型を制約する。論理入力だけを共通にする。

## 結果と影響

renderer の作者は異なる粒度の部品を構成でき、アプリケーションは生成された IRenderer2D と既存の描画・寿命手順を使える。共通 interface と Graph overload は公開追加であり、既存 concrete constructor・引数表世代・機能 lease・custom shader ABI・source device 検査を維持する。構築用公開型は増えるが、通常の呼出しへ中間形式や実行 context を持ち込まない。

### 意味の保存と受け入れ条件

[GRAPHICS-0033 の数値規則](GRAPHICS-0033-drawing2d-normalized-clips.md#数値規則と-16-サンプル相関)の標準 CPU 経路は維持する。専用方式も描画順、NonZero・EvenOdd、stroke・fill、group opacity、同一位置の shape・paint・clip の 16 サンプル相関を保存する。CPU/GPU predicate、曲線近似、attachment の描画ごとの丸めと最終丸めの差は数値契約へ明記し、全方式の bit 一致を無条件に約束しない。

一描画のサンプルを平均してから SrcOver する場合と、全描画をサンプルごとに合成して最後に平均する場合は一致しない。全 compute の融合でもこの違いを扱い、品質条件を満たせない構成は拒否する。原点、DPR、格子、色、premultiplied alpha と target の Load/Clear を勝手に変更しない。

- 同じ Canvas・list・snapshot・read lease の consumer コードを別 assembly から検証し、全構成の最終返り値が IRenderer2D であることを公開署名から追跡する。
- 正しい段階接続のコンパイルと、geometry へ text batch を接続する負例のコンパイル失敗を検査する。shader ABI・格子・数値条件の不一致は別途構築時に拒否する。
- fixture の入力一致・不一致、独立 oracle による geometry・mask・色の readback、upload と consumer の境界を検証する。checksum の一致だけでは合格にしない。
- 疎な更新、全面更新、半透明描画間への挿入、文字・clip・paint・group opacity、CPU/GPU 丸め差と宣言した許容差を検証する。
- 複数 frame、入力の変更・破棄、copy-on-write、device 世代、未実行・culling・記録失敗・待機キャンセル・未知送信・隔離退役と Graph の二重記録拒否を検証する。
- 正数予算、offset/count の checked 演算、stroke の cap/join/miter と暗黙閉辺を含む展開量、bin/scan/scratch の累積上限を確保前に検証する。上限直前・一致・超過と factory 部分失敗の回収 retry を試す。

閉じたジェネリック構成を AOT の対象にし、無制限な実行時コード生成は前提にしない。実装・性能結果は本判断に含めない。Markdown 検査は C# コンパイルや GPU の受け入れ試験を代替せず、driver の可視性と rasterization は対応 backend の実機検証を必要とする。
