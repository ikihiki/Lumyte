# ADR-GRAPHICS-0046: 描画・型付き処理・画像効果を一つのGPU実行計画へ統合する

- 状態: 採用
- 日付: 2026-11-27
- 基準コミット: `fcde70b3c84a620ab776ca19982ff678fe142315`
- 変更関係: 一部変更
- 変更元: 下記「過去の判断との関係」に対象節と継続範囲を示す。
- 変更対象: GPU処理の展開、資源と引数登録表の所有、準備時期、送信と退役を共通の計画へ集約する。
- 継続する判断: 不変の描画記録、描画順、画素と色の意味、C#転送配置、明示的な送信、GPU完了までの資源保持。

## 背景

2D・3D描画と画像効果を組み合わせる作者が、入口ごとに分散したGPU準備と所有権を一つの計画で扱えるようにする。

- **利用者の負担**: 入出力だけでなく、資源宣言・引数登録・準備・解放を接続する必要があり、部品ごとにGPU資源とtableを所有すると効果実装と寿命管理が重複する。
- **基準コードの効果**: 基準コミットの src/Graphics/Lumyte.Graphics.ImageEffects/ImageEffectDefinition.cs の AppendPassesはRenderGraphを受け取る。EffectGpuPass.csが一時画像とpassを作り、EffectPreparationContext.csが固定資源を保持する。
- **基準コードの2D・Graph**: src/Graphics/Lumyte.Graphics.Drawing2D/Execution/DrawingArgumentPlan2D.cs が描画引数とupload、src/Graphics/Lumyte.Graphics.RenderGraph/GraphShaderResourceOwner.cs がlive passのtableを準備する。
- **統合する理由**: 共通ArgumentTablePlanの利用だけでは資源identity・生存期間・完了判定は揃わないため、呼出し口を残しつつ計画と実行機構を共通化する。

## 決定

RenderGraph・型付きPipeline・画像効果・2Dを、同じ資源と引数の宣言を持つ共通GPU実行計画へ変換する。

- **入口**: RenderGraphは資源とpass、Pipelineは型付き算法、効果と2Dは画像・描画の意味を宣言する。
- **変換**: 効果は領域と色を解析した後、一つのLower実装から共通passを宣言する。
- **実行の責務**: 資源、引数table、barrier、準備、送信、実完了、退役を共通実行基盤へ集約する。
- **移行**: 既存実装と利用側を直接変更し、ソース・バイナリ互換、旧実行器、旧効果実装、互換facade、不透明な旧callback wrapperを残さない。
- **低水準境界**: Graphics抽象とnative backendは基盤内部で使う。管理対象componentに独立allocator・table・送信経路を作らせない。

### 利用者の変更前・変更後

同じ画像へぼかしを適用する操作を、共通frameへ宣言する形へ変える。

- **比較元**: 変更前は基準コードの実在API、変更後は本ADRが追加・変更するAPIである。
- **省略**: 初期化済みdevice・入力画像・shader・予算、GraphicsExecutionModuleのExecutionRuntimeへの登録、開始とowner phaseのpumpを省略する。

変更前はRenderGraphへ効果を展開し、callback記録中に一時資源と参照を準備する。送信と完了待機は利用側が行う。

~~~csharp
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.ImageEffects;

EffectImage output = parent.AppendGaussianBlur(source, 3, 3);
parent.MarkOutput(output.Texture);
using var plan = parent.Compile();
using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
plan.Record(commands);
commands.Finish();
using IGraphicsSubmission submitted = device.Queue.Submit([commands]);
await submitted.WaitAsync();
// GPU完了後、commands、planの順で解放する。
~~~

変更後は同じframeへ全componentを宣言し、共通compileと区間ごとのprepareを行う。

- **Record**: 資源確保と登録をしない。
- **ExecuteAsync**: 利用側が明示的に呼び、記録・送信・実完了待機を引き受けさせる。

~~~csharp
using Lumyte.Graphics.Planning;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.ImageEffects;

var session = new GraphicsDeviceSession(graphicsOwner, device, graphicsBudget);
var frame = session.CreateFrame();
var graph = frame.AsRenderGraph(); // 同じframe.Planを使う上位APIのview。
PlanImage input = graph.ImportImage(sourceView, sourceUseLease, sourceDescription);
PlanImage output = graph.AppendEffect(new GaussianBlurEffect(3, 3), input);
graph.Export(output);
using var compiled = await frame.CompileAsync(ct);
var prepared = await compiled.PrepareAsync(ct);
var use = session.CreateExecution(prepared);
await use.ExecuteAsync(ct);
await use.RetireAsync(); // 実完了後。ExecuteAsyncが退役済みなら同じownerで無操作。
await prepared.RetireAsync();
compiled.Dispose();
await frame.RetireAsync();
await session.RetireAsync();
~~~

借用画像とGPU仕事の寿命は、独立使用leaseとsessionの退役契約で管理する。

- **借用**: sourceUseLeaseは独立した使用権であり、allocation自体の所有権ではない。
- **失敗・待機取消し**: sessionが実行と未回収ownerを保持する。利用側は状態を確認し、DrainAsyncまたは信頼されたdevice世代退役後の回収へ進む。
- **同期Dispose**: 暗黙待機せず、使用中・隔離中の解放を拒否する。

型付き部品として構成する場合も、ぼかしのGPU実装は同じである。型付きdevice値は配置とproducer依存を表し、CPUから画素を読めるという意味ではない。

~~~csharp
using Lumyte.Pipelines;
using Lumyte.Graphics.Planning;
using Lumyte.Graphics.ImageEffects;

var g = new PipelineGraph<Device<ImageSchema>>();
var blurred = g.Input.Then(Effects.Stage(new GaussianBlurEffect(3, 3)));
using var template = session.Build(g.Complete(blurred));
var value = graph.Invoke(template, graph.ImageValue(input));
PlanImage output = graph.Image(value);
// Export、CompileAsync、PrepareAsync、明示ExecuteAsyncは上と同じ。
~~~

Graph経由とtyped構築で、同じ効果定義を共有する。

- **例の関係**: 上の二例は同じ目的の別構築であり、同じ入力を二回実行する指定ではない。
- **共通実装**: AppendEffectとEffects.Stageから同じLowerを宣言し、入口ごとにAppendPassesとEncodeを実装しない。

効果作者はAppendPassesを、解析と宣言だけを行うAnalyze/Lowerへ置き換える。

- **型の接続**: kernelとfield tokenはartifactの型付きABIへ結び付ける。
- **不変設定**: sampler・render state・color形式はkernelのdescriptorへ明示する。

~~~csharp
using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.ImageEffects;
using Lumyte.Graphics.Planning;
public sealed record TintEffect(Vector4 Scale,
    RasterKernel<TintArguments> Kernel, SamplerHandle Sampler) : ImageEffectDefinition
{
    public override int InputCount => 1;
    public override EffectAnalysis Analyze(EffectInputs inputs, ImageRegion requested)
        => EffectAnalysis.Preserve(inputs.Single, requested);

    public override PlanImage Lower(EffectLoweringContext context,
        ReadOnlySpan<PlanImage> inputs, EffectAnalysis analysis)
    {
        var pass = context.Raster("tint", Kernel);
        var arguments = pass.Arguments()
            .Bind(TintFields.Source, pass.Sample(inputs[0]))
            .Bind(TintFields.Sampler, pass.Sampler(Sampler))
            .Set(TintFields.Scale, Scale);
        PlanImage output = pass.ColorOutput(analysis.Output, AttachmentLoadOp.Clear);
        pass.Draw(arguments, vertexCount: 3);
        return output;
    }
}
~~~

このLower一つを全ての入口から使用する。

- **省略**: TintArguments・TintFieldsとkernel/samplerの初期登録を省略する。
- **責務の境界**: Lowerはnative table・資源allocation・GPU参照・送信を操作しない。

### 入口から実行までの見取り図

各入口の記述を個々の資源・pass・引数へ展開し、同じframeの共通計画に合流させる。

~~~text
RenderGraphのresource/pass宣言 ─────────────────────┐
Effect定義 → 要求領域・色の解析 → 一回のLower ──────┤
Canvasの不変2D記録 → 描画順・layer解析 → typed算法 ─┼→ 共通の資源version/use/pass/argument宣言
PipelineのStage/Then → template展開 → processor ───┘
    → Compile: culling・依存順・配置とbinding計画
    → Prepare: native資源・table・payload・PSO/binding
    → Record → 明示Submit → 実GPU完了 → commandとownerの退役
~~~

- **共通IR**: native commandではなく、producer、内容version、view/range、stage/access/layout、typed argument recipeを持つ宣言である。
- **CPUとGPUの違い**: CPU stageはCPU値を計算する。Graphics stageはGPU仕事を宣言し、この時点ではGPU上でdraw/dispatchしない。
- **実行主体**: 既存ExecutionRuntimeがCPUのdispatch・await後の再開・owner phaseを担い、Graphics実行hostが明示送信と完了待機を担う。
- **例の読み方**: 以下は本ADRの変更後の契約を説明するコードである。初期化済みのframe、kernel、schema、入力の使用leaseを省略し、上位のAsRenderGraphやEffects.Stageと、低水準の宣言APIを区別する。基準ソースの実在APIは「変更前」と明記する。

### RenderGraphを直書きした場合の変換

RenderGraphの資源とpassを共通宣言へ写し、Graph自体を一つの不透明な実行nodeにはしない。

- **変更前の入口**: 基準コミットの src/Graphics/Lumyte.Graphics.RenderGraph/RenderGraph.cs のAddPassとCompile、CompiledRenderGraph.cs のRecordを使う。CompileはGPU allocationを行わないが、旧Recordは記録だけでなく一時資源と参照の準備も引き受ける。
- **変換時に残す情報**: resourceの同一性、version、read/write範囲、実行stage、access、texture layout、明示依存、観測出力をそのまま共通計画へ渡す。
- **callbackの置換**: 生command callbackが後から宣言を増やす形を、Compileが検査できるstructured copy/draw/dispatchへ置き換える。
- **準備・記録**: Lower後に共通Prepareが確保とbindingを行い、Recordは準備済み操作だけを記録する。Graph専用のallocator/table/Submitは持たない。

変更前のcopy宣言は、同じgraphにある4要素以上のsourceとreadback用destinationを使用する。

~~~csharp
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;

parent.AddPass("copy", context =>
    context.Commands.CopyBuffer(
        context.GetBuffer(source).SliceElements(0, 4),
        context.GetBuffer(destination).SliceElements(0, 4)))
    .UseBuffer(source, new(PipelineStage.Copy, ResourceAccess.CopyRead))
    .UseBuffer(destination, new(PipelineStage.Copy, ResourceAccess.CopyWrite));
parent.ExportBuffer(destination, new(PipelineStage.Host, ResourceAccess.HostRead));
// parent.Compile → plan.Record → 明示Submit/Waitは変更前のぼかし例と同じ。
~~~

変更後はGraph入口が同じ操作を次の共通宣言へ変換する。

~~~csharp
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Planning;

// sourceは同じframeの初期化済みPlanBuffer<int>で、4要素以上を持つ。
PlanBuffer<int> storage = frame.Plan.CreateBuffer<int>(new()
{
    Count = 4, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback
});
PlanPass copy = frame.Plan.AddPass("copy");
copy.Read(source, new PlanRange(0, 4),
    new(PipelineStage.Copy, ResourceAccess.CopyRead));
PlanBuffer<int> output = copy.Write(storage, new PlanRange(0, 4),
    PlanWriteMode.Discard, new(PipelineStage.Copy, ResourceAccess.CopyWrite));
copy.Copy(source, new PlanRange(0, 4), output, new PlanRange(0, 4));
frame.Plan.Export(output, new(PipelineStage.Host, ResourceAccess.HostRead));
// この後だけframe全体をCompile/Prepareし、明示ExecuteAsyncで送信する。
~~~

- **内部結果**: 一つのcopy command、sourceのread use、新versionのwrite use、export rootが共通計画に見える。旧contextやcompiled Graphを保持して後からRunする宣言ではない。
- **利用上の違い**: 利用者がGraph viewを使っても、この低水準宣言を直接書いても、version/barrier/所有権の検査を共通化できる。

### 画像効果と2D記録の内部変換

効果と2Dを画像の意味が見える宣言へ展開し、同じLowerと共通準備を使用する。

- **基準の効果**: src/Graphics/Lumyte.Graphics.ImageEffects/ImageEffectDefinition.cs のAppendPassesからEffectGpuPass.Appendを通して親Graphへ一時画像とpassを追加する。
- **基準の2D**: src/Graphics/Lumyte.Graphics.Drawing2D.RenderGraph/Drawing2DRenderGraphExtensions.cs のAppend2DがRenderGraphRenderer2Dへ渡し、batch/layer/effectを親Graphへ追加する。元からnested Graphを別compile/executeする方式ではない。
- **変える点**: 親Graphへ載せること自体ではなく、component別の準備・引数table・ownerを共通IR/Prepare/退役へ集約する。
- **効果の展開**: ぼかしなら解析したhaloと必要入力ROIを保持して、水平pass→中間version→垂直pass→出力versionをLowerする。Tintなら前のTintEffect.Lower一つを使い、Graph入口とPipeline入口で別の実装を書かない。
- **2Dの展開**: 不変描画記録からpainter順、clip、layer、背景snapshot、effect、opacity、合成を取り出し、選択した型付き算法を同じframeへ展開する。

変更前は2Dが返す準備ownerも、親GraphのGPU完了まで利用者が保持する。

~~~csharp
using Lumyte.Graphics.Drawing2D.RenderGraph;

using PreparedDrawingPasses2D drawing = await parent.Append2D(renderer, ct);
// parent全体のCompile/Record/Submit/Waitは一回だけ。
// drawingはGPU完了とplan解放まで保持する。
~~~

変更後の2D算法は、作者が供給する型付きstageの組合せとして記述する。

~~~csharp
using Lumyte.Pipelines;
using Lumyte.Graphics.Planning;
using Lumyte.Graphics.Drawing2D;

// stagesは作者が選んだIStage群。下の型を持つものを初期化済みとする。
// Geometry: DrawRequest2D → Host<GeometrySchema>
// Coverage: Host<GeometrySchema> → Device<CoverageSchema>
// Paint: (Device<CoverageSchema>, DrawRequest2D) → Device<SamplePaintSchema>
// Resolve: Device<SamplePaintSchema> → Device<ImageSchema>
var g = new PipelineGraph<DrawRequest2D>();
var geometry = g.Input.Then(stages.Geometry);
var coverage = geometry.Then(stages.Coverage);
var paint = g.Stage(stages.Paint, coverage, g.Input);
var image = paint.Then(stages.Resolve);
using var algorithm = session.Build(g.Complete(image));
var renderer = new Renderer2D(session, algorithm);
PlanImage output = renderer.Declare(frame, immutableScene);
// layer/effect/compositeもframe.Planへ宣言し、最後に共通compileへ進む。
~~~

- **型の意味**: GeometrySchema等は算法作者のschema例である。HostはCPUが読めるcanonical内容、DeviceはGPU資源の配置とproducer宣言であり、画素配列ではない。
- **描画の制約**: coverageとpaintを別stageにしてもresolve位置は自由に動かせない。sample相関、色、clip、group opacity、backdrop置換は後述の意味契約を守る。
- **算法交換**: CPU geometry→GPU coverageはこの例で選んだ構成である。共通実行基盤が別方式へ自動fallbackする指定ではない。

### Pipelineを直接書いた場合の変換

Stage/ThenとInvokeで作った型付きDAGから、各processorが同じframeへGPU宣言を追加する。

- **定義の保存**: Completeはfactoryと不変パラメーター、型付きedge、schema/extent、producer依存を固定する。
- **templateの挿入**: Invokeは同じgraphへnodeを複製しproducerを付け直す。別pipelineを一nodeとして実行し、結果leaseだけを渡す方式ではない。
- **backendの仕事**: PipelineBuildHostがfactoryを検証・生成し、GraphicsPipelineBackendがframeへ束縛した有限能力を渡す。backendそのものが別のDefine/Compileを実行するわけではない。
- **二種類のPrepare**: pipeline.PrepareAsyncはCPU processor評価とGPU宣言の追加、compiled.PrepareAsyncはnative資源とbinding準備であり、同じ操作名でも対象が異なる。

次はRenderGraphを使わず、copy部品を二回同じframeへ展開する利用例である。

~~~csharp
using Lumyte.Pipelines;
using Lumyte.Graphics.Planning;

var t = new PipelineGraph<Device<IntSchema>>();
var definition = t.Complete(t.Input.Then(new CopyStage()));
var g = new PipelineGraph<Device<IntSchema>>();
var first = g.Invoke(definition, g.Input);
var expanded = g.Complete(g.Invoke(definition, first));

using var build = new PipelineBuildHost(
    runtime, pipelineBudget, new GraphicsPipelineBackend(frame));
using var pipeline = build.Build(expanded);
// inputはframeに属する4要素bufferのDevice<IntSchema>宣言。
using var result = await pipeline.PrepareAsync(input, ct);
frame.Plan.Export(frame.Plan.Resource(result.Value));

using var compiled = await frame.CompileAsync(ct);
var prepared = await compiled.PrepareAsync(ct);
var execution = session.CreateExecution(prepared);
await execution.ExecuteAsync(ct);
// 正常時は共通の退役契約に従う。失敗時もsessionが未回収ownerを保持する。
~~~

CopyStageのprocessorはnative copyを即時実行せず、sourceと出力versionを宣言して公開する。

~~~csharp
using Lumyte.Pipelines;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Planning;

sealed class CopyStage : IStage<Device<IntSchema>, Device<IntSchema>>,
    IStageFactory<Device<IntSchema>, Device<IntSchema>>
{
    public IStageFactory<Device<IntSchema>, Device<IntSchema>> Factory => this;
    public StageRequirements Describe(StageInputDescriptions inputs)
        => new(4096, [new GraphicsStageRequirement(1, 1)])
            { OutputMaxElements = 4 };
    public IStageProcessor<Device<IntSchema>, Device<IntSchema>>
        Create(StageBuildContext context) => new Processor();

    sealed class Processor : IStageProcessor<Device<IntSchema>, Device<IntSchema>>
    {
        public ValueTask<ValueLease<Device<IntSchema>>> PrepareAsync(
            StagePreparationContext context, ValueLease<Device<IntSchema>> input,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var gpu = (GraphicsStageCapabilities)context.Capabilities;
            var source = (PlanBuffer<int>)gpu.Read(input); // 宣言を読む。GPU bytesは読まない。
            var storage = gpu.Plan.CreateBuffer<int>(new()
            {
                Count = 4,                Usage = BufferUsage.CopySource | BufferUsage.CopyDestination
            });
            var pass = gpu.Plan.AddPass("copy");
            pass.Read(source, source.Range,
                new(PipelineStage.Copy, ResourceAccess.CopyRead));
            var output = pass.Write(storage, storage.Range, PlanWriteMode.Discard,
                new(PipelineStage.Copy, ResourceAccess.CopyWrite));
            pass.Copy(source, source.Range, output, output.Range);
            return ValueTask.FromResult(gpu.Publish<IntSchema, int>(
                output, new(4, new Dictionary<string, object>())));
        }

        public void Dispose() { }
    }
}
~~~

- **省略した定義**: IntSchemaはint要素、stride sizeof(int)のISchemaを返す作者schemaとする。runtime/moduleの開始とowner pump、入力import・独立使用leaseは上位例と同じく呼出し側が用意する。
- **共通計画からの見え方**: 2個のcopy passとその間のresource依存として検査できる。processorはinvocationの可変状態を保持せず、宣言scope終了後の追加を拒否する。

### CPUとGPUを混在させた場合の変換

CPUがGPU結果を読む箇所を明示Download境界にし、実GPU完了後にCPU値を公開して次のGPU区間へ進む。

- **CPU値**: `Host<TSchema>`はcanonicalなCPU内容であり、CPU stageがscopeのReadHost等を通して読む。
- **Device値**: `Device<TSchema>`はopaqueなGPU配置・内容version・producer依存である。Graphics能力のReadは宣言を読むだけで、CPU Readによる画素・bytes取得は許さない。
- **転送**: UploadとDownloadは依存DAG上の明示stageである。Deviceを必要に応じて暗黙readbackしたり、GPU効果をCPU処理へ置換したりしない。
- **待機**: 非同期GPU待機中に固定ownerを占有せず、実完了後のmap/copy・必要退役を経てHostを公開する。待機取消しだけでGPU資源を返さない。

次は混合実行の依存を示す設計例であり、transfer/cpuは作者から渡すIStage群を表す。

~~~csharp
using Lumyte.Pipelines;
using Lumyte.Graphics.Planning;
using Lumyte.Graphics.ImageEffects;

// cpu.Normalize / Adjust: Host<ImageSchema> → Host<ImageSchema>
// transfer.Upload: Host<ImageSchema> → Device<ImageSchema>
// transfer.Download: Device<ImageSchema> → Host<ImageSchema>
var g = new PipelineGraph<Host<ImageSchema>>();
var cpuInput = g.Input.Then(cpu.Normalize);
var gpuInput = cpuInput.Then(transfer.Upload);
var gpuResult = gpuInput.Then(Effects.Stage(new GaussianBlurEffect(3, 3)));
var cpuResult = gpuResult.Then(transfer.Download);
var adjusted = cpuResult.Then(cpu.Adjust);
var nextGpuInput = adjusted.Then(transfer.Upload);
var final = nextGpuInput.Then(Effects.Stage(new TintEffect(scale, kernel, sampler)));
var definition = g.Complete(final);
// 全構造を先にcompileし、Downloadを跨ぐnative準備だけを区間ごとに行う。
~~~

実行hostはこの宣言を、次の依存順を持つ区間に変換する。

~~~text
初期CPU Normalize
  → Upload → GPU区間0（Blur）→ Submit → 実完了
  → Downloadのmap/copy・producerの必要退役 → Host公開
  → CPU Adjust
  → 次のUploadの値確定 → GPU区間1のPrepare（Tint）
  → Record → Submit → 実完了 → 最終退役
~~~

- **compileする対象**: 後段stage・資源の種類・転送taskは先に宣言する。GPU結果に応じて未知の効果や任意の資源を後付けしない。
- **区間ごとの値**: GPUで得た個数などは事前ExtentExprと最大値の範囲で次のallocation/dispatchに使う。CPU結果が揃う前に後段payloadを確定しない。
- **失敗の範囲**: 区間1の失敗は区間0の外部書込みを取り消さない。全体atomic性が必要ならprivate targetと最終commitを明示する。
- **単一区間との区別**: 最初のCPU計算→Upload→GPU完了だけなら一GPU区間である。Download→CPU→Uploadを含む契約を、一回のnative Prepareや一つのopaque callbackで代用しない。

### 過去の判断との関係

主要判断はGPU実行基盤の統合である。次の一部を変更し、既存ADRの判断本文は保持する。

- **[GRAPHICS-0015](GRAPHICS-0015-render-graph.md#決定)の「責務と実装範囲」「実用版の所有権とculling」**
  - **変更**: RenderGraph独自のcompile・一時資源・記録ownerを共通実行計画へ移す。
  - **維持**: 宣言に基づく依存・barrier・生存判定、明示送信。
- **[GRAPHICS-0017](GRAPHICS-0017-image-effects.md#決定)の「定義とRenderGraphへの変換」「エフェクト自身に固定のパス実装を持たせる」「資源宣言・所有権・失敗」**
  - **変更**: Graph依存AppendPassesとEffectPreparationContextを解析・単一Lowerへ置換する。
  - **維持**: 画像効果の再利用、領域・色・入力数、不変パラメーター。
- **[GRAPHICS-0018](GRAPHICS-0018-2d-graphics-engine.md#決定)の「命令別interfaceとDI」「準備・記録・所有権と復旧」**
  - **変更**: 固定命令配送とrenderer固有の資源管理を型付き部品と共通scopeへ移す。
  - **維持**: CPU記録、painter順、形状・paint・clipの意味。
- **[GRAPHICS-0022](GRAPHICS-0022-2d-render-graph-layers.md#決定)の「Effectsを親RenderGraphの部分ビューとして渡す」「通常batchとgraph compositorの二つの接続」**
  - **変更**: layer効果を共通計画の意味論nodeへ記録し、実行経路を一本化する。
  - **維持**: layer/effect/opacity/clipの順序、背景の取得と置換。
- **[GRAPHICS-0032](GRAPHICS-0032-drawing2d-shared-argument-tables.md#決定)の「Pass 宣言、容量、参照の契約」「Materialize、module、所有期間」**
  - **変更**: pass全体固定の容量単位とRecord時materializeを、宣言済draw/dispatch単位とepoch Prepareへ変更する。
  - **維持**: 不可分実行の容量検査、描画順、間接参照のtable coherence、GPU完了まで保持。
- **[GRAPHICS-0035](GRAPHICS-0035-csharp-authoritative-shader-transfer.md#決定)の「共通の参照情報とbackend固有の所有」「グラフの参照準備と実行時照合」**
  - **変更**: symbolic参照recipeと共通の登録・後確定を追加し、component別所有を除く。
  - **維持**: C#転送配置、数値copy、既存wire参照ABI、whole-table binding、低水準の明示操作。

### 共通計画と所有者

意味論scene、型付き値依存、GPU資源依存を区別したまま、同じ実行基盤で照合する。

- **入口の責務**: sceneは何を描くか、Pipelineはどの算法を接続するか、RenderGraphはGPU資源とpassをどう使うかを宣言する。一意味論nodeと一GPU passの一対一対応は要求しない。
- **sessionの所有**: GraphicsDeviceSessionがdevice世代、artifact/pipeline/sampler cache、資源identity、予算、退役台帳を所有する。global singletonにはしない。
- **frameの所有**: GraphicsFrameScopeが一frameの宣言、内容version、使用lease、実行区間、登録要求を保持する。subscopeは制限viewであり、独立allocator・登録表plan・送信権を持たない。
- **依存方向**: 一般的な型付きDAGとCPU値処理はCoreへ置き、基準コミットの src/Core/Lumyte.Execution/ExecutionRuntime.cs のgraph/workflow実行を利用する。画像・device型をCoreへ逆依存させない。
- **接続**: Graphics部品は共通builderへ初期CPU計算、GPU処理、明示Upload/Downloadを宣言する。独立runのleaseを再帰的なPrepareで継ぎ足さない。
- **部品の拡張**: 公開IStageは不変factoryを持つ。factoryの要求申告を検証してからprocessorを作る。CPU processorは入力leaseを読み不変出力を公開し、Graphics processorは宣言scopeへkernel・resource use・argumentsを追加する。外部作者にも同じ契約を適用し、既知型switchで組込み専用経路を作らない。- **構築と再利用**: Stageと単項Thenは同じnode追加で、呼出しごとに別nodeを作る。同じPortはproducerを共有し、多入力Stageで分岐・合流する。foreign graph、cycle、freeze後の変更を拒否する。Buildは算法・schema・artifactを固定し、frameのInvokeは同一scopeへ有限個のinstanceを作る。

### Coreの公開backend境界

型付き宣言を作る側と、実行能力を供給するbackendを公開契約で分離する。

- **実行host**: CPU hostは明示的に借用したExecutionRuntimeを使い、起動・停止や別scheduler作成を引き受けない。GPU宣言backendも同じfactoryと型付き依存を使い、Graphics型をCoreへ持ち込まない。
- **宣言の受渡し**: PipelineDefinitionのvisitorはcaptured factoryそのものと型付き入出力edgeを宣言順に渡す。型名や診断descriptionからfactoryを再構築せず、不変パラメーターを保持する。
- **template展開**: 別構築へ挿入するtemplateは同一graphへ展開してproducerを付け直す。独立PrepareAsyncの結果leaseを再帰的に接続しない。
- **backendとCoreの責務**: backendはbuild時・invocation時の能力を明示供給する。Coreは要求検証、processor生成、入力admission、出力検証、scope閉鎖と失敗時回収を所有する。processorはrun間共有なのでawaitを跨いでも再入可能とし、invocation状態を可変fieldへ保持しない。
- **CPUとopaque値**: CPUのRead/Publishとbackendの不透明な宣言参照を分ける。backend scopeはhostが渡した正確な能力instanceだけへ結び付け、失効scope・foreign input・二重出力を拒否する。
- **検証の迂回禁止**: generic Read/Publishもbackendの値検証を通す。Graphics便宜APIを使わなくても全opaque子のframe所属とread producer依存を照合する。独立ownerは公開前に失敗しても回収台帳へ残す。
- **再帰bundle**: DeviceとDeviceを含むbundleはCPU可読にしない。任意objectをreflection探索せず、対応するbundleが全ての子を列挙する。IImmutablePipelineInputは不変metadataの宣言であり、Device不在の証明ではない。
- **CPU作者契約**: 独自payloadはICpuPipelineValueで、Host・Device・ValueLease・ownerなど所属や寿命を持つ値を再帰的に含まないことを申告する。Deviceを含む構造はbundle契約で渡す。
- **宣言viewの寿命と予約**: GPU宣言viewとpassもinvocationへ束縛する。終了後の追加を拒否し、各操作の前に宣言数と保持量を予約する。宣言失敗時はframeを無効化し、部分計画を実行しない。

### 効果の解析と一回のLower

要求領域を解析して収束させた後、確定した各効果instanceを一回だけ共通passへLowerする。

- **freezeと解析**: Completeが意味論宣言をfreezeする。CompileAsyncは初期CPU producerを実行し、出力要求からbounds・必要入力ROI・色・sample・resolveを解析する。
- **共有**: 同contextの共有nodeは要求ROIをunionする。異contextや別ROIへ分ける場合はinstanceと予算を先に固定する。Lower後はGPU実行構造をfreezeする。
- **解析の制約**: Analyzeは副作用・資源確保なしで要求収束まで繰り返せる。一つのLower実装を共有することと、各invocationで一回だけLowerすることを区別する。Lower後にROIを変えて再展開しない。
- **画像の情報**: PlanImageは全論理bounds、格納ROI、pixel原点と写像、format/view、color/alpha、内容versionを保持する。UnknownをEmptyにせず有限target全域へ保守化し、blur halo、filter領域、maskのalpha/輝度を区別する。
- **解析結果**: 出力領域、必要入力領域、入力damageからの出力damage、色変換、予算を含める。0入力generatorの色semanticはcontextへ明示し、入口で暗黙に変えない。
- **算法の選択**: 固定shaderとpass構成は効果が選ぶ。runtimeは暗黙のshader融合や算法fallbackを行わない。

### 資源versionと物理identity

内容versionを不変にし、物理allocationの別名・view・使用範囲を共通identityへ正規化する。

- **保持情報**: 共通IRは論理ID、version、view/range、producerとread/write use、kernel、argument recipe、観測可能な出力を持つ。importはdevice世代とphysical allocation identityで正規化し、同一実体の別名を独立snapshotにしない。
- **write**: 入力version・範囲・full discardまたはpreserveを明示し、新versionを返す。旧handleを最新内容へ更新しない。partial preserveは未変更領域も読むRMW、partial discardの範囲外はUndefinedとして読出しを拒否する。
- **競合**: 同versionの複数writerを禁止する。非重複更新は範囲検査付き明示mergeで後継versionへ合流し、重複writeは直列化する。同passのtexture sample/write feedbackを禁止し、buffer RMWは能力に合う一つのReadWrite useにまとめる。
- **旧内容の保存**: version追加は自動copyではない。旧readerをwrite前へ順序付けできれば同allocationを使う。上書き後も旧内容が必要なら別配置または明示copyで保存し、保存不能・cycle・予算超過はcompileを失敗させる。
- **frame間と外部利用**: sessionがframe間import競合を追跡し、実行hostが受理済submission依存と未送信frameのticket順を守る。session外にはacquire/releaseと実completionを持つinterop leaseを要求し、追跡不能な競合利用は排他的貸出に制限する。
- **分類と課金**: imported・transient・persistent・exported・artifactを分ける。借用allocationを破棄せず使用leaseを保持する。export保持画像やGPU使用中cacheをalias/evictionしない。bytesはowner単位一回、仕事量はinstance単位で課金する。

### native準備と記録の分離

table確定とtyped uploadのUnmap後、固定owner上でnative root・PSO・bindingを準備してからRecordへ渡す。

- **準備内容**: PrepareRender/PrepareCompute/PrepareMeshでattachment形式、depth、render state、index形式、表の世代、型付き引数を照合し、root storage・PSO・descriptor/bind groupを準備する。失敗時もcommandが新ownerを保持し回収を再試行できる。
- **不変identity**: NativePreparationIdentityは不変wire内容と参照の識別tokenである。native ownerでも、他commandへ準備物を移す権限でもない。
- **記録経路**: 通常generic SetArgumentsがcommand-local準備済みentryを選ぶ。Recordはstatic ABI getterやwriterを再実行せず、資源確保・map/upload・PSO/descriptor生成へfallbackしない。
- **zero-rootと拒否**: root bytesがなくてもentryを選ぶ。bytes省略と準備省略を混同せず、未知・期限切れtoken、型・state・table世代の不一致は記録を失敗させる。
- **退役順**: commandをtable・view・allocationより先に退役する。未実行preparationにexportが残る場合も最後のlease解放を同じ再試行可能なcommand-first回収へ通す。待機取消しやexport保持をGPU完了証明に代用しない。

### 引数登録と物理binding

共通BindingPlannerが全live taskを検査し、不可分なdraw/dispatchごとに安全なtable groupを確定する。

- **入力と分割**: shader ABI、参照closure、view/range、実行kind、attachment、backend上限を受け取る。共通管理はnative table一枚を意味せず、安全に共有できる単位だけまとめる。group変更で描画順を変えない。
- **再利用と所有**: 既存ArgumentTablePlanの予約・重複除去・partition・一括公開を再利用する。ShaderResourcesは一draw/dispatchの制限viewとする。componentはtableを作成・所有・直接登録しない。tableはownerや同期器ではなく、共通台帳と同identityを使う登録機構である。
- **数値と参照の転送**: C#転送配置と既存wire ABIを維持する。生成器へsymbolic typed field token、resource use、argument recipe、relocation情報を加える。数値は既存配置へcopyし物理参照だけ後確定する。reflectionとの一致を検査する。数値の別配置はreflectionから再構築しない。
- **alternate codec**: 元の型付きshader-data bufferを保持し、同wire契約のcodecから転送する。CopyFromはRootParameter・ShaderTypeName・TransferSize・ReferenceCountを事前照合し、writerも領域と完了を検査する。同配置の出力はcodec作者契約、TransferFieldsは任意診断とする。CPU転送はUpload mapping中だけ許可し、記録前にUnmapする。
- **依存の権威**: shader fieldのBindで型・scope・version・view/range・shader stage/accessを照合し、依存とbindingの共通の権威とする。attachment、copy、vertex/index、indirectのshader field外使用は別宣言し、定数Setで参照を偽装しない。
- **動的参照とcoherence**: 動的resource配列の全到達先集合・索引範囲・最大要素数・upload容量を事前宣言する。shader-data中の推移的参照もcodegen/relocation metadataで列挙し、集合外参照・無制限探索を拒否する。同patched payloadを読む単位は同table domainを要求し、domain変更時はcanonical内容から別payloadへ再patchする。
- **容量検査**: logical capacityとphysical descriptor数を分ける。slot span、RO/RW、root/remap/helper、stage/combined/attachment、range size/alignment、実ABIを含める。一draw/dispatch単独の上限超過はtable分割だけでは解消しない。合法な別kernel構成を明示選択するか失敗とし、精度・処理を黙って変えない。
- **whole-table**: 選択tableの書込み済み全登録を物理bindする。未使用descriptorも個数・alias検査から除外しない。共有tableがattachment/sample競合などを露出する場合は別tableにし、pass-local subsetへの自動投影を仮定しない。
- **物理露出と生存**: semantic ResourceUseとは別に全登録からPhysicalBindingUseを作る。allocatorは両保持区間のunionでallocation/view/registrationをpinし、state・usage・alias・予算を検査する。未使用RW descriptorから実writeやversionを作らず、semantic依存と明示rootでcullし、物理露出でdead producerを復活させない。
- **persistent payload**: canonical内容から再patchするか、参照先allocation/table generationをまとめた独立leaseを保持する。tableだけ交換してbufferを使い回さず、in-flight payload/slotを再番号付けしない。immutable table再利用は全登録ownerをpinし保持量を計上する。

### compile・prepare・実行区間

全体の構造を先にcompileし、CPUがGPU結果を必要とする境界ごとに実行区間（epoch）を分けてprepareする。

- **宣言と解析**
  1. 確保前に予約し、意味論DAGとtemplate invocationを不変captureする。
  2. 初期CPU producerを実行し、要求ROIを解析・合流してinstanceを確定する。
  3. 各instanceを一回Lowerし、出力・外部write・present・KeepAlive・明示CacheCommitをrootとしてcullする。
- **依存と配置**
  1. 型付き依存、RAW/WAR/WAW、painter順を整列し、readbackで区間を切る。
  2. 仮のallocation生存期間とtable groupへPhysicalBindingUseを加え、保持とbindingを再検証する。
  3. 不成立なら合法なgroup分割か非alias配置へ有限回で保守化する。
- **区間の準備**
  1. 現区間のallocation/view/rangeをmaterializeする。
  2. native table登録→実GpuRef/projection→argument payload patchの順に確定する。
  3. Lower時に最大容量を予約したupload/copy taskを埋め、barrier・load/store・記録台帳とnative準備を確定する。
  4. 全準備成功後だけrecord-readyとし、明示実行hostが記録・送信する。
- **記録の制約**: materialize後に未知の資源・edge・taskを追加しない。Recordは登録・allocation・I/Oをせず、開いたencoderは例外時も閉じる。
- **readback境界**: GPU→Download→CPU→Upload→GPUでは実完了、map/copy、producerの必要退役を確認してHost値を公開する。CPU処理後に次区間をprepareする。
- **動的な個数**: GPU結果の個数は事前宣言したExtentExprと最大値内でallocation/dispatchへ適用する。新効果・未知資源を生む構造変更は拒否する。
- **失敗とatomic性**: 後続prepare失敗で先行GPU書込みをrollbackしない。全体atomicな外部出力が必要ならprivate targetと最終commit passを明示し、追加予算を予約する。単一区間のprepare保証を全区間へ広げない。

### 完了・cache公開・退役

実GPU完了とcommand退役を証明した後にだけ、最終内容をcacheへ公開しownerを返す。

- **状態とticket**: 準備・記録・送信受理・GPU完了・command退役を分ける。ticketをplan、device世代、正確なcommand identity、producer集合へ結び付ける。stage・effect・RenderGraph入口は独自送信せず、待機取消しをGPU取消しと扱わない。
- **受理不明と回収**: Submit受理不明なら依存owner・table・payload・予約を隔離する。既知submissionのDrainと信頼されたdevice世代退役証明による回収を区別する。証明なしの安全な有限時間回収は保証せず、cleanup失敗を同owner台帳へ残し成功済み返却を繰り返さず再試行する。
- **cache候補**: 最終ResourceVersion/view、必要producer集合、実行区間、allocation/payload/registration lease、意味論・artifact keyを保持する。候補化だけでliveにせず、cache生成目的はCacheCommitを明示rootにする。途中producerだけliveの場合や後段のalias上書き時は公開しない。
- **公開条件**: 全必要producer成功、実GPU完了、command退役、最終内容とregistration保持が揃ってから一transactionでResidentへ公開する。部分record失敗・受理不明・cleanup失敗は公開しない。区間ごとの一部成功公開は独立候補を明示したpolicyだけ許し、既定は全体成功とする。
- **keyと予算**: CPU内容cache、配置依存geometry、sample coverage、paint plan、GPU常駐資産、完成画像を分ける。結果に必要なtransform/DPI/sample/color、palette/foreground、背景と資源publication、artifact/device世代だけをkeyに含める。保持量・実行回数・同時peakを別計上し、全in-flight/cache共通ledgerで追加・確保前に予約する。

### 2D・画像効果・地図・文字の境界

CPU記録とGPU算法を分離し、共通計画へ変換してもpainter順・sample・色・各形式の意味を維持する。

- **Canvasとcoverage**: CanvasのCPU記録は不変内容と描画順を保持する。shape/clipは同sample位置で評価し、未resolveのmembership/paint、ResolvedImage、raw MaskImageを別schemaにする。半pixelを覆う不透明描画二回はdrawごとresolveしてsource-overするprofileではalpha 0.75であり、scene末尾一回resolveへ移して0.5に変えない。- **合成と背景効果**: paint内の隔離Composite、背景とのlayer blend、完成groupへのopacity、背景snapshotのeffect置換を別演算にする。背景効果の境界はeffect*c + original*(1-c)で、単純source-overへ置換しない。
- **SVG・文字**: SVG用paint/filterとCOLR paintの補間空間・alpha・座標・ClipBox・有限領域を保持し、無条件に同じ画像演算へ潰さない。usvgのgroup/paint/clip/mask/filter/patternをadapterで維持し、文字のpaint DAG・foreground座標・同内容の別配置を区別する。
- **地図**: 全tileのcasing→全tileの内線というglobal順、camera-relative精度、ownership clipとAA clipの区別、dash距離、pattern位相を渡す。style評価・label衝突回避・font解析は共通実行基盤に持ち込まない。
- **明示的な融合**: 統合するのは所有と計画であり、自動shader融合ではない。明示fused実装にも分離実装と同じtyped入出力、sample/resolve、color、precision、資源契約を要求する。

### 契約と API 差分

判断対象の公開契約を、基準コミットとの差分として示す。

- **追加の範囲**: Planning/Coreを含む以下の追加は本ADRの変更後APIであり、基準時点の実装として扱わない。上位frontendの利用例も同じ設計契約を表す。
- **抜粋**: descriptor/field tokenの全accessorと低水準backend APIの全体は省略する。
- **移行**: 旧APIを委譲して残すのではなく、呼出し側も新しい宣言・所有契約へ移行する。

~~~diff
 namespace Lumyte.Graphics.ImageEffects
 {
     public abstract record ImageEffectDefinition
     {
         public abstract int InputCount { get; }
-        public abstract EffectPixelRect GetOutputBounds(ReadOnlySpan<EffectPixelRect> inputs);
-        public abstract EffectPixelRect GetRequiredInputBounds(int inputIndex, EffectPixelRect output);
-        public virtual EffectColorSemantic GetOutputColorSemantic(EffectColorSemantic input);
-        public abstract EffectImage AppendPasses(EffectPreparationContext context,
-            RenderGraph plan, ReadOnlySpan<EffectImage> inputs);
+        // 副作用/確保なし。入力数・色・領域不一致を拒否し、必要ROIと予算を返す。
+        public abstract EffectAnalysis Analyze(EffectInputs inputs, ImageRegion requested);
+        // 確定解析後に一回だけ宣言する。独立owner、table作成、送信は禁止。
+        public abstract PlanImage Lower(EffectLoweringContext context,
+            ReadOnlySpan<PlanImage> inputs, EffectAnalysis analysis);
     }
-    public sealed class EffectPreparationContext : IRenderGraphLifetime
-    {
-        public IGraphicDevice Device { get; }
-        public void Own(IDisposable resource);
-    }
-    public readonly record struct EffectImage(RenderGraphTexture Texture,
-        EffectPixelRect Bounds, EffectColorSemantic ColorSemantic);
+    public sealed class EffectLoweringContext
+    {
+        // 共通builderの制限view。native資源や独立ownerを公開しない。
+        public RasterPlanPass<TArguments> Raster<TArguments>(string name, RasterKernel<TArguments> kernel)
+            where TArguments : struct;
+    }
+    public static class Effects
+    {
+        // 単項画像用。多入力/0入力も同じ基盤で入力数を検証する。
+        public static IStage<Device<ImageSchema>, Device<ImageSchema>> Stage(ImageEffectDefinition effect);
+    }
 }
 namespace Lumyte.Pipelines
 {
+    public interface IStage<A, O> { IStageFactory<A, O> Factory { get; } }
+    public interface IStageFactory<A, O>
+    {
+        StageRequirements Describe(StageInputDescriptions inputs);
+        IStageProcessor<A, O> Create(StageBuildContext context);
+    }
+    public interface IStageProcessor<A, O> : IDisposable
+    {
+        ValueTask<ValueLease<O>> PrepareAsync(StagePreparationContext context,
+            ValueLease<A> input, CancellationToken cancellationToken);
+    }
+    // CPU専用hostにも明示backendを供給できる。native ownerをstageへ公開しない。
+    public interface IPipelineBackend
+    {
+        void Validate(int node, StageRequirements requirements);
+        IStageCapabilities BuildCapabilities(int node);
+        IStageCapabilities PrepareCapabilities(int node, StageRequirements compiled, StageRequirements actual);
+    }
+    public interface IStageBackendValueValidation
+    {
+        // generic scope経由でも全opaque子の同一scopeとread producerを照合する。
+        void ValidateRead<T>(T value);
+        void ValidatePublication<T>(T value);
+    }
+    public interface IStageBackendCapabilities : IStageCapabilities, IStageBackendValueValidation;
+    // 不変metadataの宣言とは別に、opaque値を再帰的に含まない作者契約を表す。
+    public interface ICpuPipelineValue;
+    public interface IPipelineBundle
+    {
+        // default値を含む全ての子を一回ずつ列挙する。探索は深さ16・総数64へ制限する。
+        void VisitChildren(IPipelineBundleVisitor visitor);
+    }
+    public interface IPipelineBundleVisitor { void Child<T>(T value); }
+    public readonly record struct PipelineBundle<A, B>(A First, B Second) : IPipelineBundle;
+    public sealed class StageBackendScope
+    {
+        public T Read<T>(ValueLease<T> value);
+        public ValueLease<T> Publish<T>(T value);
+        public void Own(IDisposable owner);
+    }
+    public sealed class PipelineBuildHost : IDisposable
+    {
+        public PipelineBuildHost(ExecutionRuntime execution, PipelineBudget budget, IPipelineBackend? backend = null);
+        public IPipeline<A, O> Build<A, O>(PipelineDefinition<A, O> definition);
+        public void Dispose();
+    }
+    public interface IPipeline<A, O> : IDisposable
+    {
+        // CPU実行または同scopeへの宣言準備。GPU送信を暗黙に行わない。
+        ValueTask<PipelineResult<O>> PrepareAsync(A input, CancellationToken cancellationToken = default);
+    }
+    public interface IPipelineDefinitionVisitor
+    {+        // 2〜6入力も同じgeneric契約を持つ。edge番号はこのdefinition内だけで有効。
+        void Stage<A, O>(IStageFactory<A, O> factory, PipelineEdge<A> inputs, PipelineEdge<O> output);
+    }
+    public readonly record struct PipelineEdge<T>(int Index);
+    public sealed class PipelineDefinition<A, O>
+    {
+        public PipelineEdge<O> OutputEdge { get; }
+        public void Visit(IPipelineDefinitionVisitor visitor);
+    }
+    public sealed class PipelineGraph<TInput>
+    {
+        public Port<TInput> Input { get; }
+        public Port<O> Stage<A, O>(IStage<A, O> stage, Port<A> input);
+        public Port<O> Stage<A, B, O>(IStage<A, B, O> stage, Port<A> a, Port<B> b);
+        public Port<O> Invoke<A, O>(PipelineDefinition<A, O> definition, Port<A> input);
+        public PipelineDefinition<TInput, O> Complete<O>(Port<O> output);
+    }
+    public sealed class Port<T>
+    {
+        public Port<O> Then<O>(IStage<T, O> stage);
+    }
 }
 namespace Lumyte.Graphics.Abstractions
 {
     public interface IShaderArguments
     {
+        // nullは既存raw経路。不変内容のnon-null tokenはcommand-local準備済み経路。
+        object? NativePreparationIdentity => null;
     }
     public interface IGraphicsCommandBuffer
     {
+        void PrepareRender<T>(RenderPassDesc pass, IGraphicsPipeline pipeline,
+            GraphicsRenderStateDesc state, IArgumentTable? table, in T arguments,
+            IndexFormat? indexFormat = null) where T : struct, IShaderArguments;
+        void PrepareCompute<T>(IGraphicsComputePipeline pipeline,
+            IArgumentTable? table, in T arguments) where T : struct, IShaderArguments;
+        void PrepareMesh<T>(RenderPassDesc pass, IMeshPipeline pipeline,
+            GraphicsRenderStateDesc state, IArgumentTable? table, in T arguments)
+            where T : struct, IShaderArguments;
     }
     public interface IGraphicsShaderDataBuffer<T> : IDisposable where T : struct, IShaderArguments
     {
+        // 元bufferの型を保つ。static wire契約不一致は書込前に拒否する。
+        // sourceは同期的に消費し、mapped Upload以外を拒否。未対応実装は明示失敗。
+        void CopyFrom<TSource>(ReadOnlySpan<TSource> source, ulong elementOffset = 0)
+            where TSource : struct, IShaderArguments;
     }
 }
 namespace Lumyte.Graphics.Planning
 {
+    // 以下は直接Pipeline/低水準copy例で使う本ADRの追加API。
+    public sealed record GraphicsStageRequirement(int MaximumPasses,
+        int MaximumResources) : IStageExtensionRequirement;
+    public sealed class GraphicsPipelineBackend : IPipelineBackend
+    {
+        // 一つのbuildを同じframeへ束縛。別frameの宣言やnative送信は行わない。
+        public GraphicsPipelineBackend(GraphicsFrameScope frame);
+        public void Validate(int node, StageRequirements requirements);
+        public IStageCapabilities BuildCapabilities(int node);
+        public IStageCapabilities PrepareCapabilities(int node,
+            StageRequirements compiled, StageRequirements actual);
+    }
+    public sealed class GraphicsStageCapabilities : IStageBackendCapabilities
+    {
+        // invocation限定。CPU bytesではなく同frameの宣言を読みproducerを登録する。
+        public GraphicsDeclarationScope Plan { get; }
+        public PlanResource Read<TSchema>(ValueLease<Device<TSchema>> input)
+            where TSchema : ISchema;
+        public ValueLease<Device<TSchema>> Publish<TSchema, T>(
+            PlanBuffer<T> resource, SchemaExtent extent)
+            where TSchema : ISchema where T : unmanaged;
+    }
+    public readonly record struct PlanRange(ulong Offset, ulong Count);
+    public abstract class PlanResource { public PlanRange Range { get; } }
+    public sealed class PlanBuffer<T> : PlanResource where T : unmanaged;
+    public sealed class GraphicsPlanBuilder
+    {
+        public PlanBuffer<T> CreateBuffer<T>(BufferDesc<T> description) where T : unmanaged;
+        public PlanPass AddPass(string name);
+        public PlanResource Resource<TSchema>(Device<TSchema> value) where TSchema : ISchema;
+        public void Export(PlanResource resource);
+        public void Export(PlanResource resource, BarrierScope finalScope);
+    }
+    public sealed class GraphicsDeclarationScope
+    {
+        // 操作前にpass/resource数を予約。scope失効・超過・宣言失敗を拒否する。
+        public PlanBuffer<T> CreateBuffer<T>(BufferDesc<T> description) where T : unmanaged;
+        public GraphicsPassScope AddPass(string name);
+    }
+    public sealed class PlanPass
+    {
+        public void Read(PlanResource resource, PlanRange range,
+            BarrierScope scope, TextureState? textureState = null);
+        public PlanBuffer<T> Write<T>(PlanBuffer<T> input, PlanRange range,
+            PlanWriteMode mode, BarrierScope scope) where T : unmanaged;
+    }
+    public sealed class GraphicsPassScope
+    {
+        // 同じ宣言をinvocationの制限viewとして行い、失効後の追加を拒否する。
+        public void Read(PlanResource resource, PlanRange range,
+            BarrierScope access, TextureState? state = null);
+        public PlanBuffer<T> Write<T>(PlanBuffer<T> input, PlanRange range,
+            PlanWriteMode mode, BarrierScope access) where T : unmanaged;
+        public void Copy<T>(PlanBuffer<T> source, PlanRange sourceRange,
+            PlanBuffer<T> destination, PlanRange destinationRange) where T : unmanaged;
+    }
+    public static class StructuredPlanCommands
+    {
+        public static void Copy<T>(this PlanPass pass, PlanBuffer<T> source,
+            PlanRange sourceRange, PlanBuffer<T> destination, PlanRange destinationRange)
+            where T : unmanaged;
+    }
+    // いずれも不変descriptor/所有scopeへ結び付き、利用者がnative参照を捏造しない。
+    public sealed class PlanImage;
+    public sealed class PlanValue<T>;
+    public sealed class ResourceVersion<T>;
+    public sealed class PlanSampledImage;
+    public sealed class PlanSampler;
+    public sealed class ArgumentField<TArguments, TField>;
+    public sealed class ArgumentRecipe<TArguments> where TArguments : struct
+    {
+        public ArgumentRecipe<TArguments> Bind<TField>(ArgumentField<TArguments, TField> field, TField reference);
+        public ArgumentRecipe<TArguments> Set<T>(ArgumentField<TArguments, T> field, T constant) where T : unmanaged;
+        // 全到達先・最大要素・索引範囲を事前宣言し、集合外を拒否する。
+        public ArgumentRecipe<TArguments> BindArray<T>(ArgumentField<TArguments, PlanResourceArray<T>> field,
+            PlanResourceArray<T> references, int maximumCount, IndexRange reachable);
+    }
+    public sealed class RasterPlanPass<TArguments> where TArguments : struct
+    {
+        public PlanSampledImage Sample(PlanImage image);
+        public PlanSampler Sampler(SamplerHandle sampler);
+        public ArgumentRecipe<TArguments> Arguments();
+        public PlanImage ColorOutput(ImageDescription description, AttachmentLoadOp load);
+        public void Draw(ArgumentRecipe<TArguments> arguments, uint vertexCount);
+    }
+    public interface IGraphicsPlanSource<A, O>
+    {
+        // 意味論node/templateを同scopeへ宣言。effectのGPU Lowerはここで先行実行しない。
+        PlanValue<O> Declare(GraphicsPlanBuilder builder, PlanValue<A> input);
+    }
+    public sealed class GraphicsDeviceSession : IDisposable
+    {
+        // graphicsOwnerは既存ExecutionRuntimeへ登録済みの固定owner。runtime自体は借用する。
+        public GraphicsDeviceSession(GraphicsExecutionModule execution, IGraphicDevice device,
+            GraphicsPlanningBudget? budget = null);
+        public GraphicsFrameScope CreateFrame();
+        public GraphicsPipelineTemplate<A, O> Build<A, O>(PipelineDefinition<A, O> definition);
+        public GraphicsExecution CreateExecution(PreparedGraphicsExecution prepared);
+        public ValueTask DrainAsync();
+        public ValueTask RetireAsync();
+        // 使用中/隔離中は拒否。暗黙wait/device破壊はしない。
+        public void Dispose();
+    }
+    public sealed class GraphicsFrameScope : IDisposable
+    {
+        // 上位のRenderGraph/効果型へ逆依存しない共通builder。
+        public GraphicsPlanBuilder Plan { get; }
+        public ValueTask<CompiledGraphicsPlan> CompileAsync(CancellationToken cancellationToken);
+        public ValueTask RetireAsync();
+    }
+    public sealed class CompiledGraphicsPlan : IDisposable
+    {+        // 現実行区間のallocation→registration→payload patchを完了して公開する。
+        public ValueTask<PreparedGraphicsExecution> PrepareAsync(CancellationToken cancellationToken);
+    }
+    public sealed class GraphicsExecution : IDisposable
+    {
+        // 明示的にrecord/submitする。取消しでも受理済仕事とownerはsessionに残る。
+        public ValueTask ExecuteAsync(CancellationToken cancellationToken);
+        public ValueTask RetireAsync();
+        public GraphicsRecoveryState Recovery { get; }
+        public void Recover(GraphicsDeviceRetirementProof proof);
+        public void Dispose();
+    }
 }
 namespace Lumyte.Graphics.RenderGraph
 {
+    public static class RenderGraphFrameExtensions
+    {
+        // 新rootを作らずframe.Planを参照するviewを返す。
+        public static RenderGraph AsRenderGraph(this GraphicsFrameScope frame);
+    }
     public sealed class RenderGraph
     {
-        public RenderGraph(IGraphicDevice? device = null, string name = "render-graph",
-            IRenderGraphObserver? observer = null, RenderGraphExtensions? extensions = null);
+        // frame.AsRenderGraph()で取得し、同scopeを受ける内部constructorで生成する。
-        public RenderGraphPass AddPass(string name, Action<RenderGraphContext> record);
-        public CompiledRenderGraph Compile(long frameId = 0, RenderGraphCaptureOptions? capture = null);
+        // 同frame scopeの宣言view。生command callbackの拡張口は置換する。
+        public PlanImage ImportImage(IGraphicsTextureView view, IDisposable useLease, ImageDescription description);
+        public PlanImage AppendEffect(ImageEffectDefinition effect, PlanImage input);
+        public PlanValue<O> Invoke<A, O>(IGraphicsPlanSource<A, O> source, PlanValue<A> input);
+        public PlanValue<Device<ImageSchema>> ImageValue(PlanImage image);
+        public PlanImage Image(PlanValue<Device<ImageSchema>> value);
+        public void Export(PlanImage image);
     }
-    public sealed class CompiledRenderGraph : IDisposable
-    {
-        // Record中に一時資源と登録表を準備する。
-        public void Record(IGraphicsCommandBuffer commands);
-    }
-    public sealed class RenderGraphContext;
 }
 namespace Lumyte.Graphics.Drawing2D.RenderGraph
 {
     public sealed record LayerOptions2D
     {
-        public Func<ImageEffectSubgraph, EffectImage>? Effects { get; init; }
+        // 不変な意味論DAGを記録する。即時Graph callbackは受け取らない。
+        public ImageEffectGraph? Effects { get; init; }
     }
     public sealed record BackdropEffectOptions2D
     {
-        public required Func<ImageEffectSubgraph, EffectImage> Effects { get; init; }
+        public required ImageEffectGraph Effects { get; init; }
     }
-    public sealed class RenderGraphRenderer2D : IDisposable;
-    public sealed class PreparedDrawingPasses2D : IDisposable;
 }
 namespace Lumyte.Graphics.Drawing2D
 {
-    public sealed class DrawingArgumentPlan2D : IDisposable;
-    public sealed class DrawingCommandRenderers2D;
-    public sealed class PreparedDraw2D : IDisposable
-    {
-        public void Record(IGraphicsCommandBuffer commands);
-        public void Dispose();
-    }
-    public sealed class PreparedDrawingFrame2D : IDisposable
-    {
-        public void Record(IGraphicsCommandBuffer commands);
-        public void AttachSubmission(IGraphicsSubmission actualSubmit);
-        public void Retire();
-        public void AbortUnsubmitted();
-        public void Dispose();
-    }
     public sealed partial class Renderer2D
     {
-        public Renderer2D(IGraphicDevice device, DrawingCommandRenderers2D renderers,
-            DrawingResources2D? resources = null, IDisposable? resourceOwner = null,
-            Action? validateResources = null, DrawingRetentionOptions2D? retentionOptions = null);
-        public Renderer2D(IGraphicDevice device, DrawingCommandRenderers2D renderers,
-            DrawingResources2D? resources, IDisposable? resourceOwner, Action? validateResources);
-        public ValueTask<PreparedDraw2D> PrepareAsync(DrawList2D list, RenderTarget2D target,
-            CancellationToken cancellationToken = default);
-        public ValueTask<PreparedDrawingFrame2D> PrepareAsync(DrawingSceneReadLease2D source,
-            RenderTarget2D target, CancellationToken cancellationToken = default);
-        public ValueTask<PreparedDrawingFrame2D> PrepareAsync(DrawingSnapshot2D source,
-            RenderTarget2D target, CancellationToken cancellationToken = default);
-        public DrawingArgumentTableMode ArgumentTableMode { get; init; }
-        public DrawingRetentionStatistics2D RetentionStatistics { get; }
+        // 構築済みの型付き処理を直接借用。資源・table・実行ownerはsessionへ集約する。
+        public Renderer2D(GraphicsDeviceSession session, GraphicsPipelineTemplate<DrawRequest2D, Device<ImageSchema>> pipeline);
+        public PlanImage Declare(GraphicsFrameScope frame, DrawingSceneSnapshot2D scene);
     }
 }
~~~

共通契約は、template・上位frontend・低水準backendの全てへ適用する。

- **template**: GraphicsPipelineTemplateはIGraphicsPlanSourceを実装する。factory/processorは公開契約であり、外部作者も同じ宣言・検証を通す。DeviceをCPU可読にせず、Deviceを含むbundleも再帰的に制限し、Graphics能力のviewから宣言を追加する。
- **効果と2Dの移行**: ImageEffectGraphは再利用可能な不変DAGとして保持するが実行器やownerにしない。ImageEffectSubgraph、EffectImage、AppendEffectsWithContext/AppendEffectWithContext、旧RenderGraphContextのGPU参照取得、PreparedDraw2D/PreparedDrawingPasses2Dの分散所有をPlanImageと共通executionへ移す。全Renderer2D constructorと旧Prepare/Recordを利用側も含めて変更し、旧overloadを残さない。
- **低水準**: IArgumentTable、IGpuRef、IShaderArguments、Graphics command APIは共通基盤が使用する。既存wire ABIを変えず、componentへ独立所有を戻さない。session外ではinterop境界を守る。

## 検討した代替案

- 独立した実行器をadapterで接続する: 既存の実装境界を保てるが、効果実装、資源配置、table確定、完了判定の分散が残るため選ばない。
- allocatorだけ共通化する: 確保の重複を減らせるが、生存判定と登録表の世代、payload、送信証跡が別計画のままで不十分である。
- 全処理を一つのtableへ登録する: 管理は単純だが、whole-tableの物理露出、attachment競合、binding上限、不要な保持を解決できない。
- 効果へGraph用Appendとpipeline用Encodeを実装する: shaderを共有しても領域・引数・useと所有が二重になる。単一Lowerを選ぶ。
- typed処理をなくしRenderGraphだけにする: resource依存は表せるが、CPU値を含む型付き部品交換と非描画処理の再利用を失う。入口を分け、実行基盤だけを統合する。
- 全効果を自動でshader融合する: pass削減の余地はあるが、色・resolve・clip・filter順序と精度の別判断になる。本判断では明示構成を使う。

## 結果と影響

効果の実装を一つにし、各入口が同じ資源・引数・完了契約で構成できるようにする。

- **得られる効果**: 重複管理と不整合な所有境界をなくす。table数やpass数を一律に減らす保証ではない。
- **移行の負担**: 実装と利用側を一緒に変更し、custom effect、独自batch、DI登録、shader payload接続も書き換える。互換adapterや二重実行経路を維持しない。
- **依存方向**: Coreの型付き構築・Execution→共通Graphics計画→RenderGraph・画像効果・Drawing2Dとする。RenderGraphとtyped Graphicsの循環を作らず、Resources連携は共通sessionへartifact leaseとpublication snapshotを供給する。
- **構造の受入条件**: Graph・typed構築・2D・単独実行が同じ効果のLower/kernel recipeを使い、効果ごとの別recordを持たない。2D→effect→composite→3D/postprocessが共通計画に見え、dead taskはallocation/table/uploadを作らない。
- **資源の受入条件**: sharingと異context/ROI、旧version保持、partial RMW、frame間import競合、range/view差を検証する。全table登録のPhysicalBindingUse、RO/RW、attachment/combined制限、binding域を跨ぐshader-data、relocation、immutable table全owner保持を含める。
- **画素の受入条件**: 二つの半coverage描画のalpha 0.75、shape/clip相関、group opacity、mask、色補間差、blur halo、backdrop置換、地図の順序・継ぎ目を独立期待値で検証する。
- **失敗の受入条件**: 部分prepare/record失敗、後続区間失敗、待機取消し、送信受理不明、device世代退役、export/cache eviction、cleanup再試行を検証する。
- **検証の分離**: CPU構造・所有権、native統合、GPU画像、Browser/AOTを別に確認する。文書検査だけで実装成立・画素一致・速度改善を認定しない。
- **不足時の扱い**: 数値予算、対応backend、filter集合は構築時に明示する。黙った省略や別方式への切替を認めない。
