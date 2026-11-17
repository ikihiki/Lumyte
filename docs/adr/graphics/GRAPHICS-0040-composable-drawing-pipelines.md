# ADR-GRAPHICS-0040: 記録形式と処理段階を型で接続し、描画の途中から検証できるようにする

- 状態: 提案
- 日付: 2026-11-17
- 基準コミット: `06c3f3d71aab42d4913d4634673fdc8e0162e495`
- 変更関係: 一部変更
- 変更元: [GRAPHICS-0018 の命令別配送](GRAPHICS-0018-2d-graphics-engine.md#命令別interfaceとdi)、[GRAPHICS-0033 の CPU 正規化](GRAPHICS-0033-drawing2d-normalized-clips.md#分類とスコープ)
- 変更対象: 全体の構成を選べる公開境界を追加し、専用構成では記録形式と clip の評価場所も選べるようにする。
- 継続する判断: 従来の Canvas・命令別 renderer、標準構成の CPU clip 数値規則、描画順、資源所有権を維持する。

## 背景

描画方式を比較・拡張する利用者は、命令別 renderer の交換だけでなく、記録形式、形状処理、転送、シーン全体の GPU 処理を組み合わせたい。現在の固定配送では、準備済みの形状や既知の GPU 入力から特定段階だけを試す境界を表現できず、方式ごとに異なる処理粒度も一つの命令別の境界では扱えない。

基準コミット `06c3f3d71aab42d4913d4634673fdc8e0162e495` の `src/Graphics/Lumyte.Graphics.Drawing2D/Execution/Renderer2D.cs` の `Renderer2D.PrepareAsync` は連続命令を `DrawingCommandRenderers2D` に配送する。同じ基準コミットの `src/Graphics/Lumyte.Graphics.Drawing2D/Model/Canvas2D.cs` の完了 API は `End()` であり、返り値は `DrawList2D` である。これらは有用な互換経路だが、全方式共通の必須中間表現にはしない。

## 決定

記録の出力、CPU 処理の入出力、GPU 計画の入出力をジェネリック型で接続する構成 API を追加する。利用者は適合する部品を交換でき、任意の公開段階へ準備済み入力を渡せる。方式ごとの処理グラフを一つの段階としても扱えるため、全シーン compute を命令別 rasterizer の交換へ押し込めずに済む。

### 利用者の変更前・変更後

例 1 は同じ矩形描画の比較である。変更前は基準コードの API、変更後および以下の全コードは提案である。device、target、描画先の状態遷移、送信・完了処理は呼出し側が用意し、後述の寿命規則で管理する。

```csharp
using Lumyte.Graphics.Drawing2D;

var canvas = new Canvas2D();
canvas.Begin();
canvas.FillRect(rect, paint); // rect: Rect2D、paint: PaintGraph2D は呼出し側の入力。
DrawList2D list = canvas.End();
using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
PreparedDraw2D oldFrame = await renderer.PrepareAsync(list, target, cancellationToken);
// oldFrame.Record(commands)、Finish、Submit、完了、commands.Dispose 後に oldFrame.Dispose。
```

変更後は入力形式を型で選び、CPU 準備と GPU 計画を別々に呼べる。例の `settings` は予算・数値契約・対象機能を確定した `PipelineSettings`、`cpu` は CPU 作業用 context、`gpu` は実 device と target を束縛した計画用 context とする。

```csharp
using Lumyte.Graphics.Drawing2D.Pipelines;

var pipeline = DrawingPresets.Classic(settings);
ClassicScene scene = pipeline.Recorder.Record(rect, paint);
PackedScene packed = pipeline.Preparation.Run(scene, cpu);
GpuPlan<ColorImage> plan = pipeline.Drawing.Build(packed, gpu);
// plan は未実行。下の例 11 の所有権手順で実行する。
```

### 構成の粒度と共通契約

以下の四つは提案する組合せであり、基準コードに存在する実装を列挙したものではない。

- 従来型: 型付き batch の記録、連続範囲の命令別配送、raster attachment 合成。
- 混成型: CPU geometry と GPU membership・raster。対応する境界だけ命令別配送を交換できる。
- 疎な合成型: CPU が strip を準備し、GPU が順序付きに合成する。
- 全 compute 型: 生曲線を含む記録から、シーン全体の geometry、binning、scan、順序付き合成を GPU pass 群で処理する。

全 compute 型も適合する compiler・binner・compositor を交換できるが、その単位はシーン全体である。矩形用 rasterizer の callback を全 compute の汎用拡張点とはしない。全方式へ CPU 平坦化や同じ四段階を強制せず、Canvas と DrawList は任意の互換 adapter とする。UI の論理木は複製せず、既存の更新・描画順情報を選択した recorder へ渡す。

CPU の `Run` は借用入力から独立した所有権を持つ不変成果物を返す。同じ不変格納の共有は明示した owner lease を出力が取得する場合に認め、深いコピーを必須にしないが、期限の切れた借用を保持してはならない。GPU の `Build` は資源と pass の依存を宣言するだけで、GPU 出力が初期化されたとは扱わない。直列接続の `Then<TNext>` は便宜機能であり、分岐・合流を持つ段階の内部は既存 RenderGraph に展開する。C# に可変長ジェネリックや高階型があるかのような万能 builder は設けない。

入力型の一致とは別に、shader ABI、binding layout、浮動小数点規則、サンプル格子、DPR、原点、extent、色空間、premultiplied alpha、fill・stroke 規則、所有者・世代、対象 device、機能と作業予算を検証する。検証は GPU 確保・送信より前に行い、非信頼入力の offset・count の加算乗算も checked で調べる。fixture も MaxWorking 上限や機能条件を迂回しない。ジェネリックは Slang・WGSL をコンパイルせず、これらの適合性を証明しない。

### 公開 API 差分の正本

以下は基準コードに対する追加と維持する宣言の抜粋である。共通の名前解決には `System`、`System.Threading`、`System.Threading.Tasks`、`Lumyte.Graphics.Abstractions`、`Lumyte.Graphics.Drawing2D` を使う。宣言の本体・データの全フィールドは省略するが、後続例で使う独自 helper の型もここで定義する。データ型の空の宣言はフィールドを省略した抜粋であり、入力検証・不変化を不要にするものではない。CPU 成果物型は backend 型や device handle を持たず、`CpuGeometry`、`PackedScene`、`RawScene`、`ClassicScene` は独立所有または明示した不変共有 lease を持つ DTO とする。

```diff
 namespace Lumyte.Graphics.Drawing2D
 {
     public sealed class Canvas2D : DrawingCanvas2D
     {
         public DrawList2D End();
     }
     public sealed partial class Renderer2D : IDisposable
     {
         public Renderer2D(IGraphicDevice device, DrawingCommandRenderers2D renderers,
             DrawingResources2D? resources = null, IDisposable? resourceOwner = null,
             Action? validateResources = null, DrawingRetentionOptions2D? retentionOptions = null);
         public Renderer2D(IGraphicDevice device, DrawingCommandRenderers2D renderers,
             DrawingResources2D? resources, IDisposable? resourceOwner, Action? validateResources);
         public ValueTask<PreparedDraw2D> PrepareAsync(DrawList2D list, RenderTarget2D target,
             CancellationToken cancellationToken = default);
     }
 }
+namespace Lumyte.Graphics.Drawing2D.Pipelines
+{
+    // 入力は Run 中だけ借用。出力は独立 owner または明示した不変共有 lease を持つ。
+    public interface ICpuStage<TIn, TOut> { TOut Run(TIn input, CpuContext context); }
+    public sealed class CpuChain<TIn, TOut> : ICpuStage<TIn, TOut>
+    {
+        public CpuChain(ICpuStage<TIn, TOut> first);
+        public CpuChain<TIn, TNext> Then<TNext>(ICpuStage<TOut, TNext> next);
+        public TOut Run(TIn input, CpuContext context);
+    }
+    // Build は計画だけを返す。native I/O は context の追跡下に限定する。
+    public interface IGpuStage<TIn, TOut>
+    {
+        GpuPlan<TOut> Build(TIn input, GpuPlanContext context);
+    }
+    public interface IRecorder<TScene> { } // 出力型の関連だけを宣言し、記録操作は各 recorder が定める。
+    public sealed record DrawingPipeline<TRecorder, TScene, TPacked, TOutput>(
+        TRecorder Recorder, ICpuStage<TScene, TPacked> Preparation,
+        IGpuStage<TPacked, TOutput> Drawing) where TRecorder : IRecorder<TScene>;
+    public sealed class ClassicRecorder : IRecorder<ClassicScene> { public ClassicScene Record(Rect2D rect, PaintGraph2D paint); }
+    public sealed class CurveRecorder : IRecorder<RawScene> { public RawScene Record(Rect2D rect, PaintGraph2D paint); }
+    public static class DrawingPresets
+    {
+        public static DrawingPipeline<ClassicRecorder, ClassicScene, PackedScene, ColorImage>
+            Classic(PipelineSettings settings, ClassicDispatch? dispatch = null);
+        public static DrawingPipeline<CurveRecorder, RawScene, RawScene, ColorImage>
+            Compute(PipelineSettings settings, ICpuStage<RawScene, RawScene>? compiler = null,
+                IGpuStage<RawScene, ColorImage>? scenePasses = null);
+    }
+    // Classic の連続 batch だけを配送する。表にない種類は標準実装を維持する。
+    public sealed record ClassicDispatch(ICpuStage<RectBatch, PackedScene> Rect,
+        ICpuStage<PathBatch, PackedScene> Path, ICpuStage<TextBatch, PackedScene> Text);
+    public sealed class CpuContext { }
+    public sealed record PipelineSettings;
+    public sealed record ClassicScene;
+    public sealed record RectBatch;
+    public sealed record PathBatch;
+    public sealed record TextBatch;
+    public sealed record RawScene;
+    public sealed record SceneGeometry; // 全シーン geometry と論理描画番号。
+    public sealed record SceneBins; // bin・scan 入力と同じ論理描画番号。
+    public sealed record CpuGeometry;
+    public sealed record PackedScene;
+    public sealed record ColorImage;
+    public sealed record SampleMask;
+    public sealed record PaintSamples;
+    public sealed record AppScene; // 利用者独自の記録形式。
+    public sealed class AppRecorder : IRecorder<AppScene>
+    { public AppScene Record(Rect2D rect, PaintGraph2D paint); }
+    public sealed class GpuPlan<TOutput> : IDisposable { }
+    public sealed class Planned<T> { } // 未実行の資源。native 参照として読めない。
+    public sealed class Initialized<T> : IDisposable { } // 完了証拠と読取り lease を所有。
+    // 定義を plan に取り込む時に byte 列と範囲列をコピーし、不変 snapshot として検証する。
+    public sealed record ResourceRange(int Slot, long Offset, long Length);
+    public sealed record GpuKernelContract(ReadOnlyMemory<byte> Shader, string EntryPoint,
+        ReadOnlyMemory<byte> BindingAbi, ReadOnlyMemory<ResourceRange> Reads,
+        ReadOnlyMemory<ResourceRange> Writes, ulong RequiredCaps, long MaxWorkingBytes);
+    public sealed record GpuPassDefinition<TIn, TOut>(GpuKernelContract Kernel);
+    public sealed record GpuJoinDefinition<TA, TB, TOut>(GpuKernelContract Kernel);
+    public sealed class GpuPlanContext : IDisposable
+    {
+        public GpuPlanContext(IGraphicDevice device, PipelineSettings settings);
+        public Planned<TOut> Pass<TIn, TOut>(GpuPassDefinition<TIn, TOut> definition,
+            Planned<TIn> input, string label);
+        public Planned<TOut> Join<TA, TB, TOut>(GpuJoinDefinition<TA, TB, TOut> definition,
+            Planned<TA> a, Planned<TB> b, string label);
+        public GpuPlan<T> Seal<T>(Planned<T> output); // 一回限り。以後の追加・Seal を拒否する。
+        public void Dispose(); // 未移動の計画資源だけを返す。plan は独立 owner。
+    }
+    public sealed class PreparedPipelineFrame<T> : IDisposable
+    {
+        public void Record(IGraphicsCommandBuffer commands); // 直接実行専用。Graph frame は拒否。
+        public void AttachSubmission(IGraphicsSubmission actualSubmit);
+        public void Retire(); // 完了確認と command disposal 後に実行 lease を返す。出力 owner は維持。
+        public void AbortUnsubmitted(); // 未送信を明示でき、command disposal 済みの場合だけ。
+        public Initialized<T> BorrowOutput(); // 成功した producer の Retire 後だけ。独立読取り lease。
+    }
+    public sealed class PipelineRunner
+    {
+        public PreparedPipelineFrame<T> Prepare<T>(GpuPlan<T> plan);
+        public GpuPlan<SampleMask> UploadAndMask(PackedScene input, GpuPlanContext context);
+        public GpuPlan<SampleMask> MaskOnly(Initialized<PackedScene> input, GpuPlanContext context);
+    }
+    // 以下は同じ型付き境界を使う例示用の部品。新しい scheduler や serializer ではない。
+    public static class ExampleParts
+    {
+        public static ICpuStage<RawScene, RawScene> CurveCompiler();
+        public static IGpuStage<RawScene, ColorImage> ScenePasses(
+            GpuPassDefinition<SceneGeometry, SceneBins> binner);
+        public static GpuPassDefinition<SceneGeometry, SceneBins> BuiltinBinner();
+        public static GpuPassDefinition<PackedScene, SampleMask> Membership();
+        public static GpuPassDefinition<PackedScene, PaintSamples> Paint();
+        public static GpuJoinDefinition<SampleMask, PaintSamples, ColorImage> Composite();
+        public static ICpuStage<RawScene, CpuGeometry> Flatten();
+        public static ICpuStage<CpuGeometry, PackedScene> Pack();
+        public static ICpuStage<RectBatch, PackedScene> Rect();
+        public static ICpuStage<PathBatch, PackedScene> Path();
+        public static ICpuStage<TextBatch, PackedScene> Text();
+        public static ICpuStage<DrawList2D, ClassicScene> CanvasAdapter();
+        public static CpuGeometry GeometryFixture();
+        public static PackedScene PackedFixture();
+        public static Planned<PackedScene> Upload(PackedScene input, GpuPlanContext context);
+        public static ICpuStage<AppScene, RawScene> AppCompiler();
+        public static ValueTask<Initialized<PackedScene>> UploadAndCompleteAsync(
+            PackedScene input, GpuPlanContext context, CancellationToken cancellationToken);
+        public static ValueTask CheckReadbackAsync<T>(GpuPlan<T> plan, CancellationToken token);
+        public static ValueTask TimeGpuPassAsync<T>(GpuPlan<T> plan, CancellationToken token);
+    }
+}
```

`CpuContext` は CPU の一時領域と有限予算、`PipelineSettings` は検証済み設定、`ColorImage` は出力の意味を表す型である。例示用 `ExampleParts` は部品の取得とテスト支援だけを担い、利用者独自の実装も同じ interface を実装できる。`GpuPassDefinition` と `GpuJoinDefinition` が利用者から渡す実部品である。shader バイナリ、entry point、binding ABI、slot ごとの読取り・書込み範囲、必要機能、有限作業量から用途・依存・barrier を導出し、context が型に対応する schema と照合する。label と EntryPoint はそれぞれ診断名と shader 内の入口名であり、部品を探す大域 registry ではない。ABI と範囲・機能・予算の不適合は確保・記録より前に拒否する。

これらの公開契約は Drawing2D assembly が所有し、GPU 実装は Abstractions の device・command・submission を使う。CPU 準備にジョブ配送が必要なら、基準 SHA の `src/Core/Lumyte.Execution/CommandPort.cs`、`src/Core/Lumyte.Execution/WorkflowPort.cs`、`src/Core/Lumyte.Execution/ModuleBuilder.cs` の型付き port と `Workflow` を adapter から利用する。資源ロードは同じ SHA の `src/Core/Lumyte.Resources/IResourceTransform.Typed.cs` の `IResourceTransform<TInput,TOutput,TRuntime>` と既存 manager に任せる。これらはスケジューリングと完了済み資源の境界であり、未実行 GPU 計画の依存辺とは異なるため、描画段階から別 scheduler・resource manager を作らない。

Graph adapter は別 assembly `Lumyte.Graphics.Drawing2D.RenderGraph` に置き、Drawing2D から Graph へ参照を追加しない。基準 SHA の `src/Graphics/Lumyte.Graphics.Drawing2D.RenderGraph/RenderGraphRenderer2D.cs` の `AppendAsync` は internal であり、外部段階の接続にそのまま使えるとは扱わない。追加する adapter の公開境界は次のとおりである。

```diff
+namespace Lumyte.Graphics.Drawing2D.RenderGraph
+{
+    public static class PipelineGraphAdapter
+    {
+        // plan は借用。返却 frame が実行資源を所有し、同じ寿命手順を要求する。
+        // 出力は frame 所有 allocation を import して observable にする。二重 Record は禁止。
+        public static PreparedPipelineFrame<T> Append<T>(
+            Lumyte.Graphics.RenderGraph.RenderGraph graph, GpuPlan<T> plan);
+    }
+}
```

Drawing2D が opaque な公開 plan の内部依存記述と実行 bridge を所有し、既存の `InternalsVisibleTo` により Drawing2D.RenderGraph だけへ公開する。adapter は frame 所有の出力 allocation と外部入力を import し、資源範囲・pass・依存を宣言する。adapter の preparation・pass callback・記録 hook は生存 producer の依存集合、実際の記録成否と正確な command を bridge に報告し、bridge が frame の command・actual submission・完了へ結び付ける。初期化は出力に必要な producer 全集合の成功と完了から判定する。公開 plan を native framework にせず、外部実装は型付き定義を渡し、internal 型へのアクセス・downcast・逆向き assembly 依存を必要としない。これは新しい内部接続と公開 API の追加である。

基準 SHA の `src/Graphics/Lumyte.Graphics.RenderGraph/IRenderGraphPreparation.cs` の Prepare と `src/Graphics/Lumyte.Graphics.RenderGraph/RenderGraphPreparationContext.cs` の LivePasses から生存集合を取得する。ただし既存 preparation は Graph 全体の記録完了を通知しないので、同じ SHA の `src/Graphics/Lumyte.Graphics.RenderGraph/Lumyte.Graphics.RenderGraph.csproj` に存在する adapter 向け friend 境界に次の内部 hook を追加する。

```diff
 namespace Lumyte.Graphics.RenderGraph
 {
     public sealed class RenderGraph : IDisposable
     {
+        // adapter 専用。登録は Compile 前。成功 compile で compiled owner へ移動する。
+        // end は全 Record の終了時に一度呼び、失敗例外も必ず渡す。Dispose 時は別通知。
+        internal void ObserveRecording(Action<IGraphicsCommandBuffer> begin,
+            Action<IGraphicsCommandBuffer, Exception?> end, Action disposed);
     }
 }
```

adapter がこの hook を Drawing2D 所有の内部 bridge へ接続する。begin は command の同一性、end は全体成否、disposed は callback が再実行不能な状態を確定する。bridge の descriptor は出力 schema・資源所有 lease・必要 producer 集合を持ち、Graph の native view を出力 owner と取り違えない。Graph と Drawing2D の間に新しい公開 backend framework は設けない。

### 部品を交換する例

以降の各コード片では `using Lumyte.Graphics.Drawing2D.Pipelines;` と `using System.Threading;` を共通に使う。`cpu`、`gpu`、`settings`、`cancellationToken` は例 1 と同じ意味の入力、`runner` は `PipelineRunner` とする。各例の gpu は同じ条件の新しい計画 scope とし、異なる plan の資源を無断で接続しない。全 plan は IDisposable であり、実行 frame が実行中の資源 lease を独立保持するため、frame 準備後に plan 自体は破棄できる。実行しない例の plan も検査後に破棄する。部品の実装ではなく、型付き接続の利用例を示す。

例 2: 従来型で矩形・パス・文字を交換する。

```csharp
var dispatch = new ClassicDispatch(ExampleParts.Rect(),
    ExampleParts.Path(), ExampleParts.Text());
var classic = DrawingPresets.Classic(settings, dispatch);
```

`ClassicDispatch` の各段階は型付きの該当種類の連続 batch を受け取り、元の論理描画番号を保持する。矩形 A、半透明文字 B、矩形 C を A・C・B へ並べ替えない。文字は shaping をここで再実行せず、記録済み glyph 情報を渡す。この拡張点は全 compute の形状別 callback ではない。

例 3: 全 compute のシーン単位 compiler と binner を交換する。

```csharp
var compute = DrawingPresets.Compute(settings,
    ExampleParts.CurveCompiler(), ExampleParts.ScenePasses(ExampleParts.BuiltinBinner()));
RawScene curves = compute.Recorder.Record(rect, paint);
RawScene encoded = compute.Preparation.Run(curves, cpu);
GpuPlan<ColorImage> wholeScene = compute.Drawing.Build(encoded, gpu);
```

利用者が同じ型の独自 binner を渡す場合は次のように構築する。customShader・customAbi は所有する byte 列、reads・writes は ResourceRange 列、requiredCaps は ulong、maxWorkingBytes は正の long として呼出し側が用意する。

```csharp
var kernel = new GpuKernelContract(customShader, "BinScene", customAbi,
    reads, writes, requiredCaps, maxWorkingBytes);
var customBinner = new GpuPassDefinition<SceneGeometry, SceneBins>(kernel);
var customCompute = DrawingPresets.Compute(settings,
    ExampleParts.CurveCompiler(), ExampleParts.ScenePasses(customBinner));
```

`ScenePasses` は全シーン geometry→binner→scan→順序付き compositor を持つ。SceneGeometry と SceneBins の論理描画番号、bin 配置 ABI、数値契約、順序・大域依存が適合する場合だけ交換できる。これは矩形ごとの callback ではなく、標準部品と利用者の実オブジェクトを同じ境界へ渡す例である。

例 4: 独自 recorder が独自の生曲線記録を生成し、Canvas と DrawList を通さない。

```csharp
var appPipeline = new DrawingPipeline<AppRecorder, AppScene, RawScene, ColorImage>(
    new AppRecorder(), ExampleParts.AppCompiler(), compute.Drawing);
AppScene ownIr = appPipeline.Recorder.Record(rect, paint);
RawScene normalized = appPipeline.Preparation.Run(ownIr, cpu);
GpuPlan<ColorImage> direct = appPipeline.Drawing.Build(normalized, gpu);
```

例 5: CPU の直列処理だけを接続する。

```csharp
var preparation = new CpuChain<RawScene, CpuGeometry>(ExampleParts.Flatten())
    .Then(ExampleParts.Pack());
PackedScene packedOnly = preparation.Run(normalized, cpu);
```

例 6: CPU で得た geometry を転送し、GPU で分岐・合流する段階の内部。

```csharp
CpuGeometry geometry = ExampleParts.Flatten().Run(normalized, cpu);
PackedScene payload = ExampleParts.Pack().Run(geometry, cpu);
Planned<PackedScene> uploaded = ExampleParts.Upload(payload, gpu);
Planned<SampleMask> membership = gpu.Pass(ExampleParts.Membership(), uploaded, "membership");
Planned<PaintSamples> paintValues = gpu.Pass(ExampleParts.Paint(), uploaded, "paint");
Planned<ColorImage> joined = gpu.Join(ExampleParts.Composite(),
    membership, paintValues, "ordered-composite");
GpuPlan<ColorImage> dag = gpu.Seal(joined);
```

この例は CPU fork も独立した geometry・paint 準備として段階内に配置できる。各成果物は同じ論理描画番号と依存版を持ち、join がその一致を検証する。全入力が揃う barrier と painter order の辺を保持し、単なる型の一致や資源の用意順で合成順を決めない。

### 準備済み入力から始める例

例 7: 保存した不変 CPU geometry から packing だけを試す。GPU は不要である。

```csharp
CpuGeometry saved = ExampleParts.GeometryFixture();
PackedScene repacked = ExampleParts.Pack().Run(saved, cpu);
```

例 8: 再配置可能な CPU packing fixture から upload と選んだ membership pass、readback を試す。frontend と geometry 生成を省くが、upload は省かない。

```csharp
PackedScene fixture = ExampleParts.PackedFixture();
GpuPlan<SampleMask> uploadAndMask = runner.UploadAndMask(fixture, gpu);
await ExampleParts.CheckReadbackAsync(uploadAndMask, cancellationToken);
```

例 9: producer の実行と完了を計時外で確認した GPU fixture から pass だけを計時する。

```csharp
using var uploadGpu = new GpuPlanContext(device, settings);
using Initialized<PackedScene> resident = await ExampleParts.UploadAndCompleteAsync(
    fixture, uploadGpu, cancellationToken);
using var maskGpu = new GpuPlanContext(device, settings);
using GpuPlan<SampleMask> maskOnly = runner.MaskOnly(resident, maskGpu);
await ExampleParts.TimeGpuPassAsync(maskOnly, cancellationToken);
```

uploadGpu と maskGpu は同じ device・世代で別の一回限り計画を所有する。helper は producer 計画を Seal して実行し、consumer は新しい scope で Seal する。MaskOnly は device・世代・ABI を検証し、入力の独立した読取り lease を plan と frame へ保持する。consumer の mask・scratch は新規 private 領域とし、resident 入力を上書きしない。

`UploadAndCompleteAsync` は実 upload を Record・Submit し、実 submission の完了確認、command disposal、retirement を終えてから lease を返す。`TimeGpuPassAsync` はその後の対象 pass の native timestamp query だけを測り、upload を含む例 8 と同じ費用とは呼ばない。CPU 準備、計画、確保、転送、記録、submit、待機、回収の費用も別に扱い、タイマーの外へ隠して全体改善を主張しない。

fixture はメモリ上の DTO、手続き的生成、必要なら既存 serializer の adapter を使い、新規専用ファイル形式やログ収集系を必須にしない。保存可能なのは記号資源 ID と schema・ABI・数値規則・格子・色・alpha・fill/stroke・原点・DPR・extent・依存 hash・呼出し側 texture 版である。native handle、device、opaque GPU reference は保存しない。実 device への再束縛と全検証を必須とし、checksum が一致しても現在の native 内容が初期化済みとは証明しない。

例 10: 型の接続誤りと実行時の適合失敗を区別する。

```csharp
var valid = new CpuChain<RawScene, CpuGeometry>(ExampleParts.Flatten())
    .Then(ExampleParts.Pack());
// コンパイル失敗例: Pack の入力は CpuGeometry、直前の出力は PackedScene。
// var invalid = valid.Then(ExampleParts.Pack());
// 同じ PackedScene 型でも ABI・格子・機能・予算が不適合なら Build が例外を返す。
GpuPlan<SampleMask> checkedPlan = runner.UploadAndMask(fixture, gpu);
```

不適合は `ArgumentException`、機能・予算超過は `NotSupportedException`、期限切れ所有権は `InvalidOperationException` とし、部分計画を公開しない。GPU 確保後の native 失敗は owner が確保済み資源を回収する。入力型を強引に cast して計画を通す fallback は設けない。

### 実行・回収と互換経路の例

例 11: 直接実行の成功経路。以下では Abstractions の `IGraphicsQueue queue`、記録中の `IGraphicsCommandBuffer commands` を呼出し側が用意する。

```csharp
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.Pipelines;

PreparedPipelineFrame<SampleMask> frame = runner.Prepare(checkedPlan);
frame.Record(commands);
commands.Finish();
IGraphicsSubmission actual = queue.Submit([commands]);
frame.AttachSubmission(actual);
await actual.WaitAsync(CancellationToken.None);
commands.Dispose();
frame.Retire();
using Initialized<SampleMask> completed = frame.BorrowOutput();
actual.Dispose();
frame.Dispose();
checkedPlan.Dispose();
```

送信前キャンセルでは、未記録なら `frame.Dispose()`、記録済みで確実に未送信なら `commands.Dispose(); frame.AbortUnsubmitted();` を使う。送信後の待機キャンセルは GPU の取消しではないので、actual と frame を保持して完了確認を再開する。Submit が例外を投げ、受理の有無が不明なら Abort を呼ばず、frame・command・参照資源を隔離し、実完了または確認済み device 世代退役まで owner が保持する。一般の Dispose はこの隔離を解除しない。

GPU 計画の出力は未実行、実行記録済み、送信追跡中を経て、producer の実行と完了を確認した場合だけ初期化済みとなる。Graph の culling、未実行、中断、キャンセル、未知の送信結果では resident として公開しない。adapter は実際に実行した producer 集合と actual submission を対応付ける必要があり、単なる `done=true` では代用できない。

Retire は完了済み実行の frame slot・scratch・記録用 lease だけを返し、成功した出力 allocation の owner は frame.Dispose まで frame が保持する。BorrowOutput は独立した読取り専用参照数 lease を増やし、frame や Graph を破棄した後も最後の lease の解放まで allocation が有効となる。解放済みメモリの再取得や可変 GPU cache の再公開ではない。未 retire・abort・culled・未実行・producer 失敗・送信失敗・隔離・世代無効・frame 破棄済みの BorrowOutput は InvalidOperationException とする。

出力公開と実行資源の回収は別の判定である。actual が Completed でも producer が失敗なら Retire による回収だけを許し、出力を公開しない。SubmissionStatus.Failed を正常な初期化完了へ読み替えず、安全な完了を確認できない資源は device 世代の確認済み退役まで隔離する。

不変 geometry は読取り lease で共有できるが、mask・scratch・出力の書込み領域は frame ごとに所有する。更新時は copy-on-write または別領域を使い、古い frame が生存中の領域を上書きしない。段階間の辺は既定で借用・独立出力とし、所有権の移動を伴う最適化は専用の consume API が明示される場合だけに限定する。

例 12: 旧 facade を使う利用者は変更不要で、内部では従来型 preset を使える。段階テストだけが adapter の後から始められる。

```csharp
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.Pipelines;

var facadeParts = DrawingPresets.Classic(settings);
ClassicScene adapted = ExampleParts.CanvasAdapter().Run(list, cpu);
PackedScene preparedCpu = facadeParts.Preparation.Run(adapted, cpu);
GpuPlan<ColorImage> facadePlan = facadeParts.Drawing.Build(preparedCpu, gpu);
// 例 7・8 の部分実行も同じ Preparation・Drawing を使い、別実装へすり替えない。
```

既存 Renderer2D の五・六引数 constructor、custom shader ABI、引数表の世代と機能 lease、source device の検証、resourceOwner の移動条件を維持する。互換 facade の内部移行時にもこれらを preset context へ明示的に渡し、新 API の既定値で消さない。既存の `PreparedDraw2D` と `PreparedDrawingFrame2D` を相互に同一型扱いせず、新しい frame 型の寿命規則を勝手に旧型へ遡及しない。

例 13: 同じ型付き plan を既存 Graph へ追加する。graphPlan は未実行の `GpuPlan<ColorImage>`、commands は記録中、queue は同じ device のものとする。

```csharp
using Lumyte.Graphics.Drawing2D.Pipelines;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Graphics.RenderGraph;

var graph = new RenderGraph(device);
PreparedPipelineFrame<ColorImage> graphFrame = PipelineGraphAdapter.Append(graph, graphPlan);
graphPlan.Dispose(); // frame が独立した実行・出力 owner を取得済み。
CompiledRenderGraph compiled = graph.Compile();
compiled.Record(commands); // callback が frame と正確な commands を結ぶ。
commands.Finish();
var graphSubmit = queue.Submit([commands]);
graphFrame.AttachSubmission(graphSubmit);
await graphSubmit.WaitAsync(CancellationToken.None);
commands.Dispose();
compiled.Dispose();
graph.Dispose();
graphFrame.Retire();
using Initialized<ColorImage> graphOutput = graphFrame.BorrowOutput();
graphFrame.Dispose(); // graphOutput の lease は引き続き有効。
graphSubmit.Dispose();
```

基準 SHA の `src/Graphics/Lumyte.Graphics.RenderGraph/RenderGraph.cs` の `Compile(long frameId = 0, RenderGraphCaptureOptions? capture = null)` と、`src/Graphics/Lumyte.Graphics.RenderGraph/CompiledRenderGraph.cs` の `Record(IGraphicsCommandBuffer)`・`Dispose` を使う。compiled は一時 allocation と view を所有し、Dispose は import の allocation 自体を破棄しない。本提案の Graph 接続は、frame が出力資源群を先に所有し、adapter がそれらを import して MarkOutput で観測可能にする方式に限定する。Graph 一時 texture を compiled.Dispose 後も生存すると仮定せず、汎用 T の出力 schema が必要な frame 所有資源群を列挙する。

Graph frame の Record を直接呼ぶと InvalidOperationException とする。Append の途中失敗では Graph を捨て、frame または失敗処理の owner が未送信資源を回収する。記録開始・終了の bridge は Graph 全体の成否と producer 集合を捕捉し、途中まで成功した callback だけで出力を公開しない。未実行・culled producer の出力は初期化されない。確実に未送信なら command を破棄し、compiled・Graph の callback owner を破棄した後で graphFrame.AbortUnsubmitted() を呼ぶ。callback が一度も実行されず command が結び付かなかった場合も、この呼出しは未送信の明示と Graph 破棄を要求する。未知の Submit 結果ではこの経路を使わず隔離を維持する。

## 検討した代替案

- 固定 renderer 表だけをジェネリックにする: 従来型の交換は簡潔だが、独自記録・全シーン依存・途中入力を表せない。命令別表は従来型の局所拡張として残す。
- 一つの mode 列挙と巨大 context に全実装を入れる: 選択箇所は少なくなるが、不適合な入出力も実行時まで接続できてしまう。型付き部品を構成する preset を選ぶ。
- 全方式を直列の固定段階へ正規化する: 計測点を揃えやすいが、分岐・合流や全シーン scan を不自然に分解する。段階内の DAG と既存 RenderGraph を使う。
- 既存の resource transform を GPU pass interface にする: ロード管理を再利用できるが、完了済み出力という契約と未実行計画が矛盾する。資源ロードと CPU ジョブ配送だけに既存基盤を使う。

## 結果と影響

利用者は記録と実行の両方を構成でき、同じ部品に CPU・GPU の既知入力を与えて検証できる。部品作者には型の接続に加えて数値・所有権・依存・予算を宣言する責任が増える。閉じたジェネリック構成を AOT へ事前登録し、粗い段階と共有実装でコード増加を抑える。実行時 plugin のコード生成や無制限な型の組合せは前提にしない。

### 意味の保存と受け入れ条件

描画順、clip と paint の 16 サンプル相関、NonZero・EvenOdd、平坦化誤差、group opacity、描画先の色と alpha を共有の意味契約とする。[GRAPHICS-0033 の数値規則](GRAPHICS-0033-drawing2d-normalized-clips.md#数値規則と-16-サンプル相関)の標準経路は維持する。専用 compute は同じ格子と意味を満たす独自評価を持てるが、数値許容差を構成に明記し、旧 CPU 正規化の bit 一致を無条件に約束しない。

一描画の色は同じ位置の shape・paint・clip を評価して平均する。描画ごとに平均して SrcOver する場合と、サンプルごとに全描画を合成して最後に平均する場合は一致しない。全 compute の融合はこの違いと attachment の描画ごとの丸め・最後の丸めを数値契約に含め、無条件な全方式 bit 一致を主張しない。

- 正の型付き接続と、例 10 の負の接続を実際の提案 API でコンパイル検査する。Markdown 検査はこの代替ではない。
- fixture の期待値は対象実装の出力コピーではなく、独立した CPU oracle・手計算・別段階の意味検証から作る。各 GPU pass に既知 buffer 入力を与え、geometry・mask・色の readback を検査する。
- CPU fixture と初期化済み GPU fixture のタイマー境界を検査し、native query と実 submission の対応も統合テストする。GPU 全体を一つの不透明な計測対象にしない。
- 変更なし・疎な変更・全面変更、透明描画の間への挿入、clip・paint・文字・group opacity を検証する。CPU と GPU の各境界で同じ意味が保存されることを調べる。
- 複数 frame、古い入力の可変配列を fixture 作成後に変更する場合、借用元の破棄、culling、未実行、キャンセル、未知送信、隔離・退役を検証する。共有した不変入力と frame 書込み領域の非干渉を確認する。
- 予算と機能の許容境界について、上限の一つ手前・一致・一つ超過を試し、offset 不正・count overflow・ABI と device 世代の不一致を確保前に拒否する。

実装・性能結果は本判断に含めない。driver の JIT、API より下の rasterization・メモリ可視性は mock だけでは証明できず、対応 backend の実機テストが必要である。
