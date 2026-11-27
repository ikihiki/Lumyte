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

- **Stageの依存**: 各StageがGPU・Execution等の必要能力をcontext型で宣言し、Pipeline本体は具象サービスに依存しない。
- **入口**: RenderGraphは資源とpass、Pipelineは型付き算法、効果と2Dは画像・描画の意味を宣言する。
- **変換**: 効果は領域と色を解析した後、一つのLower実装から共通passを宣言する。
- **実行の責務**: 資源、引数table、barrier、準備、送信、実完了、退役を共通実行基盤へ集約する。
- **移行**: 既存実装と利用側を直接変更し、ソース・バイナリ互換、旧実行器、旧効果実装、互換facade、不透明な旧callback wrapperを残さない。
- **低水準境界**: Graphics抽象とnative backendを共通基盤と明示interop segmentで使う。管理対象componentに独立allocator・table・送信経路を作らせず、直接systemも共同hostの台帳から送信する。

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
PlanImage input = graph.ImportImage(sourceView, sourceUseLease, sourceDescription,
    sourceState, sourceContentVersion);
PlanImage output = graph.AppendEffect(new GaussianBlurEffect(3, 3), input);
graph.Export(output);
await frame.ExecuteAsync(ct); // Compile→Prepare→実行→内部ownerとframeの退役。
await session.RetireAsync();
~~~

借用画像とGPU仕事の寿命は、独立使用leaseとsessionの退役契約で管理する。

- **借用**: sourceUseLeaseは独立した使用権であり、allocation自体の所有権ではない。sourceStateは提供者が確定したInteropState（incoming stage/access/layout・initialized）、sourceContentVersionはその使用権に束縛した内容versionであり、viewだけから推測しない。
- **失敗・待機取消し**: sessionが実行と未回収ownerを保持する。利用側は状態を確認し、DrainAsyncまたは信頼されたdevice世代退役後の回収へ進む。
- **同期Dispose**: 暗黙待機せず、使用中・隔離中の解放を拒否する。

型付き部品として構成する場合も、ぼかしのGPU実装は同じである。型付きdevice値は配置とproducer依存を表し、CPUから画素を読めるという意味ではない。

~~~csharp
using Lumyte.Pipelines;
using Lumyte.Graphics.Planning;
using Lumyte.Graphics.ImageEffects;

var g = new PipelineGraph<IGraphicsStageContext, Device<ImageSchema>>();
var blurred = g.Input.Then(Effects.Stage(new GaussianBlurEffect(3, 3)));
using var template = session.Build(g.Complete(blurred));
var value = graph.Invoke(template, graph.ImageValue(input));
PlanImage output = graph.Image(value);
// Export後のframe.ExecuteAsyncは上と同じ。毎回新しいframeを取得する。
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
- **実行主体**: 各Stageが型付きcontextから必要なサービスを要求し、CPU処理やExecutionへの配送を選ぶ。Pipeline本体はGPU・Executionの具象型へ依存しない。Graphics実行hostは別接続の固定ownerから明示送信と完了待機を担う。
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
// 宣言完了後のframe.ExecuteAsyncが全体Compile/Prepareと明示実行を一回だけ行う。
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

変更後は、command種類ごとの型付き既定経路と、同じ種類の個々の記録への明示overrideを、不変の経路登録表で選ぶ。

- **比較元**: 基準コミットの src/Graphics/Lumyte.Graphics.Drawing2D/Execution/DrawingCommandRenderers2D.cs はFillRect、FillRoundedRect、FillCircle、DrawLine、FillPath、StrokePath、DrawImage、DrawCoverageMask、DrawDistanceField、DrawMesh、DrawPaintGraphの11個の型付きslotを持つ。種類別選択は既存であり、追加するのはtyped Pipeline templateへの接続と記録単位のoverrideである。
- **記録の型**: 同コミットの src/Graphics/Lumyte.Graphics.Drawing2D/Model/DrawingCommand2D.cs はcaptured Stateを持つ不変record、同ディレクトリのFillRectCommand2D.cs はRect・Paint・Stateを持つsealed recordである。geometryだけへ落としてPaintやclipを失わない。

### 2Dコマンド別の経路と再利用

作者が一度Buildしたtemplateを複数command・複数frameから借用し、既定経路またはsnapshotへ束縛したoverrideから同じframeへ展開する。

- **登録と型**: `Register<TCommand>`は正確な`DrawingCommandPipelineRequest2D<TCommand>`入力、DrawingCommandResult2D出力、TContextを持つtemplateから型付きkeyを返す。`SetDefault<TCommand>`で種類ごとの既定を一つ指定してFreezeする。異なるcommand入力や不足したcontext能力はコンパイルエラーとし、object選択やruntime castへ逃がさない。登録・Freezeは送信しない。
- **個別選択**: DrawingPipelineOverrideBuilder2D(scene, routes)はsnapshot identityと不変registry identity/revisionへ束縛する。`Set<TCommand>`(ordinal, key)はscene全体の記録順ordinalで対象を指し、actual command型を検証する。Freezeしたmanifestへ型付きroute keyとordinalを保存し、immutable command payloadへnative ownerやIDisposable templateを格納しない。同じindexの別snapshotへの再利用は禁止する。
- **検証と失敗**: default値key、未登録・foreign・stale key、異session/世代、異registry、範囲外ordinal、actual command型不一致、重複Set、重複default、missing routeを明示的に拒否する。builderで可能な検査を即時に行い、Declareは全command・manifest・templateの所属と必要能力を部分宣言より前に再検証する。未対応commandを飛ばしたり単一算法へfallbackしたりしない。拡張commandも明示登録を要する。
- **背景入力とprojection**: frame-local requestはCPU可読metadataとoptional `Device<ImageSchema>` Backdrop、その対応BackdropVersionを持つopaque IPipelineBundleとする。immutable sceneへDeviceを格納しない。共通側が必要な背景snapshot copyを同じframeのparent passとして宣言し、そのproducerからrouteの背景readへ依存を張る。RequiredBackdropVersionと実入力version・frame/target/ROIを一致させ、背景が必要なのに欠ける場合は拒否する。背景なしはImageとVersionの双方なしとして表す。DrawingCommandInputs2D.Metadata/Backdropは本ADRで追加する公開typed projection stageであり、前者だけがCPU可読metadata portを公開し、後者はopaque背景portを保持する。CPU geometryはmetadata portのみを受け、request全体をlifecycle.Readで読むことは禁止する。
- **公開境界**: projectionのprocessorはGraphicsStageCapabilities.ReadBundle(lifecycle, input)を使う。Planning assembly側がCore assemblyの公開ReadDeclarationへBind時の正確なauthorityを渡し、Coreがleaseのrun/node/scopeを確認してValidateDeclarationReadで全opaque子のframe/世代・producerを検証する。通常Readはこの宣言view経路を使わずDevice/bundleを拒否し続ける。Metadata projectionは検証済みmetadataだけを新しいCPU値として公開し、背景projectionは依存を引き継ぐopaque値を公開する。任意CPU stageが背景を画素として読めるAPIではなく、GPU bytesは明示Downloadなしに得られない。
- **入力scope**: `DrawingCommandPipelineRequest2D<TCommand>`はcommandとcaptured DrawRequest2Dを含むMetadataを持つ。後者はdrawing state、clip ancestry、sample座標とtarget、color/alpha、ROI、painter order、必要backdrop versionを全て含む不変の意味論入力である。Geometry/Coverage/Paintの分離でもこの入力を必要stageまで渡し、geometry-only入力で代用しない。
- **出力と合成**: 全routeはDrawingCommandResult2Dへ揃える。これは`Device<ImageSchema>` Image、明示的なComposition2D（SourceOver / ReplaceWithBackdrop）、BackdropVersion?を持つopaque IPipelineBundleであり、CPU可読ではない。共通合成側が実際のsample/target、ROI、color/alpha、resolve scope、順序とbackdrop versionを照合してから合成する。SourceOverは背景を含まないresolved画像、ReplaceWithBackdropは要求された正確な背景versionを既に含む画像とする。背景不足・古いversion・矛盾するtagを拒否し、単なる`Device<ImageSchema>`ではこの区別を隠さない。unresolved coverageはこの結果型に入れず、route内部でper-draw resolveを完了する。
- **共通の描画意味**: clip、save/restore、layer、group opacity、backdrop、painter orderは共通2D contextが所有する構造であり、leaf routeが独立実行・再配列しない。同一routeごとにscene全体をsortせず、隣接commandまたは独立性を証明できるものだけbatch化する。重なる二つの半coverage不透明描画はper-draw resolve後のsource-overでalpha 0.75を維持する。既存11 leafにDrawTextはなく、文字はglyphをpath/image/mask等へ正規化した後のroute、または明示登録した拡張型で扱う。
- **所有と失効**: registryはtemplateを借用する不変metadataである。session・template一式をloop外で保持し、frameのbindingと使用leaseは正常退役または失敗後のdrain/回収まで保持する。batch/cache identityはregistry identity/revision、選択routeとtemplate identity、snapshot/override identity、captured state、device世代を含み、route変更ではsnapshotに関連する準備結果とretained cacheをstaleにして再評価する。新registryを同じsnapshotへ使う場合もmanifestを作り直し、cache missにする。
- **有限性と実行**: registry/manifest挿入、snapshotコピー、template展開の前に有限予算を予約する。宣言中の失敗はframeを無効化して部分実行を拒否し、開始済み仕事とownerを台帳で保持・回収する。独立tableやexecutorは作らず、最後のframe.ExecuteAsyncだけが一回の実行を所有する。

~~~text
不変scene（snapshot identity + 全体ordinal + captured state）
  → 構造解析（clip/layer/backdrop/orderを保持）
  → 全選択の検証 → 個別override優先、なければcommand型のdefault
  → parent背景snapshot copy producer → frame-local opaque request（metadata + 背景/version）
  → Metadata projection（CPU可読） / Backdrop projection（opaque、producer read依存）
  → 借用templateのGeometry/Coverage/Paint/Resolve/Contribute
  → DrawingCommandResult2D → versionと合成modeの検証
  → 元のpainter順で共通layer/effect/composite → 同じframe.Plan
  → frame.ExecuteAsync一回 → 実完了・退役（templateは次frameでも借用）
~~~

次の変更後例ではFillRectの高速・精密経路を同じ入力型で構成する。stagesは作者が供給する初期化済みIStage群、schemaは作者定義である。

~~~csharp
using Lumyte.Pipelines;
using Lumyte.Graphics.Planning;
using Lumyte.Graphics.Drawing2D;

// metadata = DrawingCommandMetadata2D<FillRectCommand2D>（CPU可読）
// Geometry: metadata → Host<GeometrySchema>
// Coverage: (Host<GeometrySchema>, metadata) → Device<CoverageSchema>
// Paint: (Device<CoverageSchema>, metadata) → Device<SamplePaintSchema>
// Resolve: Device<SamplePaintSchema> → Device<ImageSchema>
// Contribute: (Device<ImageSchema>, metadata, DrawingBackdrop2D) → DrawingCommandResult2D
// 背景を必要とする算法はopaque背景portをPaint等にも明示的に接続する。
// Contributeも作者提供stage。合成mode/versionを明示し共通結果へ揃える。
var g = new PipelineGraph<IGraphicsStageContext,
    DrawingCommandPipelineRequest2D<FillRectCommand2D>>();
var metadata = g.Input.Then(DrawingCommandInputs2D.Metadata<FillRectCommand2D>());
var backdrop = g.Input.Then(DrawingCommandInputs2D.Backdrop<FillRectCommand2D>());
var geometry = metadata.Then(stages.Geometry);
var coverage = g.Stage(stages.Coverage, geometry, metadata);
var paint = g.Stage(stages.Paint, coverage, metadata);
var image = paint.Then(stages.Resolve);
var contribution = g.Stage(stages.Contribute, image, metadata, backdrop);
var fastFillTemplate = session.Build(g.Complete(contribution));
// preciseFillDefinitionは同じrequest/result型、imageDefinitionはDrawImageCommand2D入力。
var preciseFillTemplate = session.Build(preciseFillDefinition);
var imageTemplate = session.Build(imageDefinition);
var registry = new DrawingPipelineRoutesBuilder2D<IGraphicsStageContext>(session);
var fastFill = registry.Register<FillRectCommand2D>(fastFillTemplate);
var preciseFill = registry.Register<FillRectCommand2D>(preciseFillTemplate);
var drawImage = registry.Register<DrawImageCommand2D>(imageTemplate);
registry.SetDefault<FillRectCommand2D>(fastFill);
registry.SetDefault<DrawImageCommand2D>(drawImage);
var drawingRoutes = registry.Freeze();
var renderer = new Renderer2D<IGraphicsStageContext>(session, drawingRoutes);
// template三つとdrawingRoutesは、以後の全frameのdrain/回収までloop外で保持する。
~~~

次の例ではsceneのordinal 0がFillRect、1がDrawImage、2がFillRectである。0だけ精密経路にし、1と2は種類別の既定を使う。scene生成と独立使用lease取得は省略する。

~~~csharp
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Planning;

var choices = new DrawingPipelineOverrideBuilder2D<IGraphicsStageContext>(immutableScene, drawingRoutes);
choices.Set<FillRectCommand2D>(0, preciseFill);
var overrides = choices.Freeze();
var frame = session.CreateFrame();
var contexts = new GraphicsContextFactory(frame); // frameごとに新しく束縛する。
PlanImage output = renderer.Declare(frame, immutableScene, contexts, overrides);
frame.AsRenderGraph().Export(output);
await frame.ExecuteAsync(ct); // precise FillRect → DrawImage → fast FillRectの順。
contexts.Dispose(); // 正常退役後。失敗時はDrain/世代退役の回収まで保持する。
// 次frameは新しいframeとfactoryを作る。同一snapshot/routesのmanifestは再利用できる。
~~~

- **例の寿命**: 失敗・取消し時もfactoryとbindingを未回収ownerより先に解放せず、アプリの回収経路へ保持する。factoryはDeclareへ渡したframeと同じframeへ束縛し、result leaseとdrainまで保持する。
- **型と算法の意味**: HostはCPUが読めるcanonical内容、DeviceはGPU配置とproducer宣言であり画素配列ではない。この例のCPU geometry→GPU coverageは作者の明示選択であり、sample相関・色・clip・group opacity・backdrop契約を保つ。resolve位置を自由に移す指定でも自動fallbackでもない。
- **型の拡張**: 自由なStage contextは`Renderer2D<TContext>`と`DrawingPipelineRoutes2D<TContext>`まで保持する。複合contextのrouteには同じTContextのfactoryを明示供給し、既定GraphicsContextFactoryが追加能力を供給するとは扱わない。

### Pipelineを直接書いた場合の変換

Stage/ThenとInvokeで作った型付きDAGから、各processorが同じframeへGPU宣言を追加する。

- **定義の保存**: Completeはfactoryと不変パラメーター、型付きedge、schema/extent、producer依存を固定する。
- **templateの挿入**: Invokeは同じgraphへnodeを複製しproducerを付け直す。別pipelineを一nodeとして実行し、結果leaseだけを渡す方式ではない。
- **hostの仕事**: PipelineBuildHostがfactoryを検証・生成し、GraphicsContextFactoryが同frameへ束縛した型付きサービスを呼出しごとに貸す。別のDefine/Compileを実行しない。
- **宣言と実行**: pipeline.PrepareAsyncはCPU processor評価とGPU宣言の追加である。その後のframe.ExecuteAsyncが全体compile、区間のnative準備、記録・送信・実完了待機をまとめる。高度な低水準経路のcompiled.PrepareAsyncとは役割を区別する。

次はRenderGraphを使わず、copy部品を二回同じframeへ展開する利用例である。

~~~csharp
using Lumyte.Pipelines;
using Lumyte.Graphics.Planning;

var t = new PipelineGraph<IGraphicsStageContext, Device<IntSchema>>();
var definition = t.Complete(t.Input.Then(new CopyStage()));
var g = new PipelineGraph<IGraphicsStageContext, Device<IntSchema>>();
var first = g.Invoke(definition, g.Input);
var expanded = g.Complete(g.Invoke(definition, first));

using var build = new PipelineBuildHost(pipelineBudget);
using var contexts = new GraphicsContextFactory(frame);
using var pipeline = build.Build(expanded);
// inputはframeに属する4要素bufferのDevice<IntSchema>宣言。
using var result = await pipeline.PrepareAsync(contexts, input, ct);
frame.Plan.Export(frame.Plan.Resource(result.Value));

await frame.ExecuteAsync(ct);
// 正常完了後は次のframeを取得できる。失敗時はsessionの回収契約へ進む。
~~~

CopyStageのprocessorはnative copyを即時実行せず、sourceと出力versionを宣言して公開する。

~~~csharp
using Lumyte.Pipelines;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Planning;

sealed class CopyStage : IStage<IGraphicsStageContext, Device<IntSchema>, Device<IntSchema>>,
    IStageFactory<IGraphicsStageContext, Device<IntSchema>, Device<IntSchema>>
{
    public IStageFactory<IGraphicsStageContext, Device<IntSchema>, Device<IntSchema>> Factory => this;
    public StageRequirements Describe(StageInputDescriptions inputs)
        => new(4096, [new GraphicsStageRequirement(1, 1)])
            { OutputMaxElements = 4 };
    public IStageProcessor<IGraphicsStageContext, Device<IntSchema>, Device<IntSchema>>
        Create(StageBuildContext context) => new Processor();

    sealed class Processor : IStageProcessor<IGraphicsStageContext, Device<IntSchema>, Device<IntSchema>>
    {
        public ValueTask<ValueLease<Device<IntSchema>>> PrepareAsync(
            StagePreparationContext lifecycle, IGraphicsStageContext services,
            ValueLease<Device<IntSchema>> input,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var gpu = services.Graphics;
            var source = (PlanBuffer<int>)gpu.Read(input); // 宣言を読む。GPU bytesは読まない。
            var storage = gpu.Plan.CreateBuffer<int>(new()
            {
                Count = 4, Usage = BufferUsage.CopySource | BufferUsage.CopyDestination
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

// cpu.Normalize / AdjustはIPipelineContext（またはIExecutionStageContext）を要求。
// transferはIGraphicsStageContextを要求。graphは両者を明示的に合成する。
// cpu.Normalize / Adjust: Host<ImageSchema> → Host<ImageSchema>
// transfer.Upload: Host<ImageSchema> → Device<ImageSchema>
// transfer.Download: Device<ImageSchema> → Host<ImageSchema>
var g = new PipelineGraph<IGraphicsExecutionContext, Host<ImageSchema>>();
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

### frameごとの一回限りの実行

毎回取得したframeに宣言を集め、offscreenの単独実行はframe.ExecuteAsync一回でCompile・Prepare・実行と正常時の退役を完了する。表示との共同送信は後述のrecord-only経路を使う。

- **統合する処理**: 全体compile→必要な区間のprepare→Record/Finish/Submit→実GPU完了確認→内部command/prepared/compiledとframeの退役を順に進める。資源と登録の準備をRecordへ戻さない。
- **高々一回**: 有効なframeへの最初の呼出しで、最初のawaitより前に実行権を原子的に消費する。成功後・失敗後・取消し後も同じframeを再実行しない。同時呼出しの敗者と二度目はInvalidOperationExceptionで拒否し、同じ仕事の別Taskを作らない。既に取消されたtokenでの呼出しもframeを消費する。
- **宣言の終了**: 実行開始時に宣言をfreezeし、以後のpass/resource/argument追加を拒否する。typed宣言が未完了なら実行を拒否し、壊れた部分計画を実行しない。
- **毎回の取得**: 次の描画にはsession.CreateFrameで新しいscopeを得る。完了したframeをResetして再使用する方式にはしない。sessionと不変templateは再利用する。
- **低水準との排他**: CompileAsync等を使う明示的な低水準経路は保持できるが、同じframeの実行権を共有する。手動Compile後に統合ExecuteAsyncを呼んだり、統合実行中に手動Compileへ入り直したりできない。compiled/preparedの再利用で再送信する迂回も認めない。
- **正常完了**: Taskの成功は単なる送信受理ではなく、実GPU完了と統合経路の内部owner/frame退役までを含む。使用中のleaseが残る場合は保持規則を優先し、安全に退役できる前に次frameの枠を解放しない。
- **失敗と取消し**: 再実行ではなくDrain・Retire・信頼されたdevice世代退役の回収へ進む。受理不明・送信済未完了・進行中mapのownerをsessionに残す。cleanup失敗で主例外を隠さず、同台帳から回収を再試行する。回収の再試行はframeの実行回数に含めず、compile/submitをやり直さない。
- **戻り値と出力**: 統合メソッドはValueTaskで完了を返す。Plan.Exportの宣言だけを独立した使用leaseの取得と扱わない。準備済export leaseの取得や手動の送信接続が必要な利用は、保持・回収を明示する低水準経路を選び、同frameで両経路を混在させない。

~~~csharp
// 各描画要求で新しいframeを取得する。sessionとrendererはloop外で構築済み。
var frame = session.CreateFrame();
var contexts = new GraphicsContextFactory(frame);
PlanImage image = renderer.Declare(frame, immutableScene, contexts);
frame.AsRenderGraph().Export(image);
await frame.ExecuteAsync(ct);
contexts.Dispose(); // 失敗時はdrain/回収まで保持する。
// await frame.ExecuteAsync(ct); // 二度目は拒否。失敗/取消し後の再呼出しも不可。
// 次回は新しいframeへ宣言する。
~~~

### 直接command bufferと共通計画の共存

GraphicsFrameScopeはsurfaceを持たない一回限りのGPU仕事scopeとし、表示アプリの共同送信hostが有限個のsegmentを同じ台帳へ登録して送信する。

- **二種類の記録**: RenderGraph・2D・画像効果・typed Graphicsは共通IRへstructuredに展開する。直接command bufferを使うsystemは実際のIGraphicsCommandBufferへ記録でき、その算法をStageやRenderGraphへ書き換える必要はない。直接記録はIR内の不透明な旧Graph.Run nodeではなく、別のfinished bufferを持つ明示interop segmentである。
- **共同host**: アプリがGraphicsSubmissionBatchを組み立て、surface acquire、segment順、共有資源のversionと状態、実Submitとreceipt、Present、GPU完了、surface release、退役を統括する。batchはsessionの同じidentity・予算・回収台帳を使う制限境界であり、第二のallocatorやcomponent別送信器ではない。
- **有限manifest**: 直接systemはrecord前に実command identity、device世代、全資源の正規化identity・subresource範囲、read/write、入力と出力version、initialized、entry/exitのstage/access/layout、参照closure、独立使用lease、準備物のownerを申告する。shader-dataの推移的参照、whole-table物理露出、attachment、copy、vertex/index、indirectもclosureへ含める。内部barrierは直接作者の責務、segment間barrierは共同hostだけの責務とし、同じ遷移を双方が挿入しない。
- **検査**: batchは別名をphysical allocation identityへ正規化し、closure不足、未知状態、未保持参照、別世代・別command向け準備、未申告write、重複所有、上限超過を記録前に拒否する。未知callbackを安全認定しない。native作者の完全申告は契約であり、任意nativeコードのGPU挙動を静的証明するsandboxではない。
- **状態と順序**: 全manifestとstructured宣言のimport/exportを先に固定し、全体のRAW/WAR/WAW、旧version保持、entry/exit状態を一度だけ解決してからbound recordへ入る。同queue順序だけでメモリbarrierが不要とはしない。境界barrierはhost所有のbridge bufferへ記録し、実command列の一部に含める。cross-queueは明示syncと所有移管が必要であり、本節の単一queue batchでは拒否する。
- **外部との授受**: 資源はexport/acquire leaseで渡す。leaseには内容version・producer依存・状態・範囲・世代と保持責任を含める。未送信の予約からqueued/in-flight、command退役までalias再利用・破棄を禁止する。外部allocationは借用し、allocation ownerを二重移管しない。共通管理componentの独立allocator/table禁止は継続するが、直接systemが既に所有するnative資源・tableの使用leaseを検証して受け入れることは禁止しない。

~~~text
raw scene ── finished CB A ─┐
                          │ entry/exit・version・closureを共同台帳へ登録
Graph postprocess → 2D UI ─┼─ structured IR → Compile/Prepare → finished CB B
raw debug overlay ─ CB C ─┘
    → A → bridge → B → bridge → C → Present状態へのbridge
    → Queue.Submit(全buffer, acquire wait, render-done signal) 一回
    → receiptを全segmentへ一度だけ接続 → surface.Present(render-done)
    → GPU完了 と surface release → command-first退役
~~~

record-only経路はCompile/Executeと同じ一回限りの権利を消費し、送信せずに共同hostへ所有ticketを渡す。

- **RecordForSubmissionAsync**: Sealによる全体Compileを経て、当該segmentのPrepare・Record・Finishを行い、finished buffer列と解放可能なownership ticketを持つPreparedSubmissionSegmentを返す。Submit・Present・CPUのGPU完了待機を行わない。失敗・取消しでも再実行できず、部分ownerはsession台帳に残す。
- **計画の順序**: batch.SealAsyncは最初のawaitより前に全structured frameを同じatomic one-shot権利で予約し、全manifestと初期状態を検証して全体compileと境界解決を完了する。以後のRecordForSubmissionAsyncはその予約の唯一の継続であり、二回目のclaimではない。単独CompileAsyncやExecuteAsyncとの競合はSeal段階で拒否する。RecordForSubmissionAsyncが先に呼ばれた場合も同じSeal/claimを行い、予約済batchでは高々一回のrecord継続だけを認める。準備開始後にmanifestやversionを追加しない。
- **送信試行前の移行**: batch.Submitは正確な順序付きcommand列、expected segment coverage、device/queue/epochを検証し、全参加ticketを原子的にsubmit-attempted/quarantineへ移してから実Queue.Submitを呼ぶ。例外で受理が不明でも全ownerを保持する。戻り後のAttachだけを安全境界にしない。
- **receipt**: 一回の実Queue.Submit成功で得たIGraphicsSubmissionはbatchだけが所有する。内部attachは全対象ticketへ一度だけ行い、subset・別buffer・二重attach・同ticketの再送信を拒否する。segmentはreceiptの借用参照だけを持ち、IGraphicsSubmissionを個別Disposeしない。submit前に全attach予約を済ませ、受理後の接続失敗でもbatchからownerを失わない。
- **退役**: 実submissionと全commandの完了後（表示imageの使用leaseはhostがsurface releaseも確認した後）、全segmentとbridgeのcommandを先に退役し、共有submission handleを一回解放してからprepared/table/view/allocation/import/frameの順で返す。exportが残れば必要ownerを保持する。未送信ticketはAbortAsyncで安全に返せるが、送信試行後は実完了または信頼されたdevice世代退役証明まで返さない。cleanup失敗は同台帳で再試行する。
- **単独経路**: offscreenのframe.ExecuteAsyncも同じsegment/ticket/receipt内部機構を使い、必要epochごとのSubmit・Waitと最後の退役まで行う。複数epochでもframeのclaimは一回であり、公開RecordForSubmissionAsyncを同frameへ繰り返し呼ばない。surface acquireやPresentは所有しない。同frameでExecuteと共同送信を混在させない。
- **複数epochの範囲**: 本節の共同batchとRecordForSubmissionAsyncは単一GPU epochだけを受け入れ、GPU→Download→CPU→Upload→GPUを含むframeは送信・記録前に拒否する。複数epoch契約を一回Submitへ圧縮しない。その仕事は別のoffscreen frameの単独実行で各epochを完了し、明示export leaseを保持して、新しい単一区間の表示frameへimportする。各frameのclaimは高々一回である。準備という名前のメソッドに隠れたSubmitを入れない。独立exportが必要なoffscreen仕事には次の公開予約・独立使用権経路を使い、frame.ExecuteAsyncの完了だけでexport leaseを取得できたと扱わない。

独立exportはcompile済みの最終出力を予約し、全epochの成功と必要command退役後にだけ型付きviewと状態を公開する。

- **予約の時点**: source frameで出力をExport rootにして全体Compileした後、Prepare前にCompiledGraphicsPlan.ReserveImageExportを呼ぶ。予約は論理出力・最終producer/epoch・最大extentと保持予算を固定する。後段epochのallocationがまだない時点でも予約でき、未知の出力やdead/foreign outputは拒否する。
- **準備の時点**: CompiledGraphicsPlan.PrepareAsyncは一回だけ呼び、実行全体を代表するPreparedGraphicsExecutionと最初の実行区間の準備を返す。戻り値からAcquireImageExportで予約済の独立export ownerを取得する。最終epochの資源がまだmaterializeされていなくてもownerと予算予約を取得し、viewやValueは未公開とする。
- **実行の時点**: session.CreateExecutionはpreparedを一回だけclaimする。GraphicsExecution.ExecuteAsyncが一回の実行claimで必要epochを進め、各GPU完了→Download/CPU→Upload→次epochの準備を内部で行う。PrepareAsyncやCreateExecutionをepochごとに再呼出ししない。Prepare/AcquireImageExportはSubmitを行わず、明示Executeだけが必要な各epochを送信する。
- **公開と保持**: GraphicsImageExportLease.Valueは最終内容の全producer成功・GPU完了・必要command退役後だけ読める。View、Description、正規化identity/range、確定したOutgoingStateとContentVersionを一緒に返す。部分成功・取消し・受理不明では公開しない。AcquireUseはこの公開済内容に対する独立した使用leaseを発行し、export元ownerをpinする。元export leaseを返しても独立useの保持は失われない。
- **受渡しと回収**: 表示batchのAcquireImageへ独立useとその同じValueを一回移管する。受渡し前の失敗ではアプリがuseを保持し、成功後はbatchだけが返す。表示のcommand退役までsource allocation/view/registrationをpinし、最後のexport/use解放後にsourceのprepared・compiled・frameを同じcommand-first台帳で回収する。複数の読者はそれぞれ独立useを取得し、writeとの競合は同じidentity/version台帳で検査する。

~~~csharp
using Lumyte.Graphics.Planning;
using Lumyte.Graphics.RenderGraph;

// sourceFrame/outputは前節GPU→Download→CPU→Upload→GPUの宣言済み最終出力。
// すべてのlocal ownerは取得直後にアプリの回収台帳へ登録し、失敗時も保持する。
sourceFrame.AsRenderGraph().Export(output);
CompiledGraphicsPlan compiled = await sourceFrame.CompileAsync(ct);
GraphicsImageExportReservation reserved = compiled.ReserveImageExport(output);
PreparedGraphicsExecution prepared = await compiled.PrepareAsync(ct); // 一回だけ。
GraphicsImageExportLease exported = prepared.AcquireImageExport(reserved);
GraphicsExecution execution = session.CreateExecution(prepared); // 一回だけclaim。
await execution.ExecuteAsync(ct); // 各epochのSubmit/Waitとcommand退役。最終内容を確定。
GraphicsImageExportUseLease use = exported.AcquireUse();
GraphicsExportedImage value = use.Value; // prepared取得時には読めない。
var displayBatch = session.CreateSubmissionBatch(maximumSegments: 1);
InteropImageLease inputLease = displayBatch.AcquireImage(value.View, use,
    value.Description, value.OutgoingState, value.ContentVersion);
// 成功したのでuseの返却責任はdisplayBatchへ移った。
var displayFrame = session.CreateFrame();
PlanImage input = displayFrame.AsRenderGraph().ImportImage(inputLease.InitialVersion);
// このinputを単一区間の表示frameで取得targetへ合成し、前述のbatch手順で表示する。
// displayBatchのGPU完了・surface release・Retire後にsource側を回収する。
await exported.RetireAsync(); // 独立useが残っていれば元allocationをまだ返さない。
// 全consumer use返却後だけ：
await compiled.RetireAsync();
await sourceFrame.RetireAsync();
~~~

- **例の範囲**: 最後の三つの退役は表示batchの回収後に行う順序を示す。コメント位置で表示を省略したままsourceを解放する指定ではない。待機・cleanup失敗時は全ownerを台帳に残し、成功済返却を繰り返さず再試行する。新しい表示frameへimportするので、消費済sourceFrameを再実行しない。

次は取得済みの同じtargetをraw scene→Graph postprocess→2D UI→raw overlayの順に使う変更後の例である。directSourcesは後述の公開DirectSegmentSource契約でmanifestと実記録能力を渡すアプリの二systemである。Scene(target)とOverlay(uiOutput)は必要なtarget viewを解決し、native資源とcommand-local準備を完了してからsourceを返す。これらは宣言・準備だけを行い、送信・GPU待機をしない。

~~~csharp
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Planning;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.Drawing2D;

// hostが保持するacquiredを借用する。失敗時もbatchはsessionに残る。
async Task PrepareMixedAsync(GraphicsSubmissionBatch batch, IGraphicsSurfaceFrame acquired,
    DrawingSceneSnapshot2D uiSnapshot, Action onSurfaceRegistered, CancellationToken ct)
{
    InteropImageLease target = null!;
    DirectSegmentSource sceneSource = null!;
    await graphicsOwner.InvokeAsync(() =>
    {
        target = batch.AcquireSurfaceImage(acquired);
        onSurfaceRegistered(); // アプリのno-throw代入。成功登録直後、次の失敗点より前。
        sceneSource = directSources.Scene(target); // 実command-local準備も固定owner上。
    });
    DirectSegmentHandle scene = batch.AddDirect(sceneSource);
    InteropImageVersion sceneOutput = batch.Output(scene, target);

    var frame = session.CreateFrame();
    var graph = frame.AsRenderGraph();
    PlanImage sceneImage = graph.ImportImage(sceneOutput);
    PlanImage post = graph.AppendEffect(postEffect, sceneImage);
    PlanImage composed = graph.CopyTo(post, sceneOutput); // 同targetの新versionを作る。
    var contexts = new GraphicsContextFactory(frame);
    batch.Own(contexts); // 同じ回収台帳への一回の移管。
    PlanImage withUi = renderer.DeclareOnto(frame, uiSnapshot, contexts, composed);
    InteropImageVersion uiOutput = batch.AddFrame(frame, withUi);
    DirectSegmentSource overlaySource = null!;
    await graphicsOwner.InvokeAsync(() => overlaySource = directSources.Overlay(uiOutput));
    DirectSegmentHandle overlay = batch.AddDirect(overlaySource);
    batch.ExportForPresent(batch.Output(overlay, target));

    await batch.SealAsync(ct); // 全manifest・importの実初期状態→全体compile→順序とbarrier。
    PreparedSubmissionSegment a = await batch.RecordDirectAsync(scene, ct);
    PreparedSubmissionSegment b = await frame.RecordForSubmissionAsync(batch, ct);
    PreparedSubmissionSegment c = await batch.RecordDirectAsync(overlay, ct);
    // ここまではSubmitなし。別finished bufferとticket。次節のhostが共同送信する。
}
~~~

- **準備と使用**: AddDirectのsourceは未記録の実commandを含む。Seal後、RecordDirectAsyncは固定owner上でそのcommandを渡して同期recordし、Finishする。command-local準備は記録前に完了し、callback中の追加allocation/table登録を認めない。structuredのnative準備も同じcompile済みclosureを使う。
- **入力状態**: AcquireSurfaceImageはbackendから実際の取得image identityと初期state/accessを受け取り、独立した使用leaseを発行する。初回Undefinedか再利用Presentかをアプリが推測しない。hostは元のIGraphicsSurfaceFrameを保持し、使用lease返却だけでsurfaceを解放しない。一般importにもinitialized/content versionとincoming access scopeを必須とし、layoutだけのimportでraw producerのwrite依存を失わない。
- **同targetの書込み**: CopyToとDeclareOntoは前versionを読み必要範囲をpreserveし、新versionを返す。2DのLoad/painter/backdrop規則を維持し、UIだけtargetをclearし直さない。raw overlayもuiOutputをpreserveするmanifestを持つ。
- **private出力の構成**: 各systemが別offscreen出力を作り、export/importして最後に合成する構成も選べる。同じidentity/version/使用leaseを渡し、独立snapshotと偽らない。同batch内のinterop versionは未完了producerの依存を伝える宣言handleであり、GPU完了・退役後にValueを公開する独立export leaseとは区別する。旧version保存のcopy・非alias配置は共通台帳が一度だけ決定する。

直接systemのRecordは、次のように実commandのencoderを操作する。これは共通IRに保存するcallbackではなく、Seal済の別segmentを記録する作者コードである。

~~~csharp
// directSources.Sceneが返すsourceの構成例。ここは固定owner上で呼ばれる。
// sceneCommandsはこのtarget view用にPrepareRender済みの未記録command。
// scenePass/pipeline/state/table/argumentsと全準備ownerは起動資源から明示供給する。
// otherUses/closure/useOwnersはそれら全参照を列挙済みの有限・不変配列。
DirectSegmentSource Scene(InteropImageLease target)
{
    var attachment = new InteropState(
        new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite),
        TextureState.ColorAttachment, Initialized: false);
    return new DirectSegmentSource
    {
        Commands = sceneCommands,
        Uses = [new(target.InitialVersion, target.Range, InteropAccess.WriteDiscard,
            attachment, attachment with { Initialized = true }), ..otherUses],
        ResourceClosure = [target, ..closure],
        UseOwners = useOwners,
        Record = commands =>
        {
            IRenderEncoder encoder = commands.BeginRenderPass(scenePass);
            try
            {
                encoder.SetPipeline(scenePipeline);
                encoder.SetRenderState(sceneState);
                encoder.SetArgumentTable(sceneTable);
                encoder.SetArguments(sceneArguments);
                encoder.SetViewport(sceneViewport);
                encoder.SetScissor(sceneScissor);
                encoder.Draw(vertexCount: 3);
            }
            finally { encoder.End(); } // 例外時もencoderを閉じ、未送信仕事は回収する。
        },
    };
}
~~~

- **例の省略**: shaderのSceneArguments型と初期artifact、viewport/scissorの値は描画内容固有なので省略する。scenePassはtarget.Viewをclear/storeするattachmentであり、overlay側は直前versionをReadWriteとしてloadする。独立な準備中失敗にもアプリ台帳でsceneCommandsと部分ownerを保持し、AddDirect成功でのみbatchへ移管する。native作者はtable物理closureを省略してよいわけではない。

### ゲームループへの組込み

アプリは表示用slotを有限数だけ所有し、非同期prepareとイベント・simulation・owner pumpを並行させ、明示Submit直後にPresentを要求してから完了を回収する。

- **起動時**: device/surface/swapchain、ExecutionRuntimeへのGraphicsExecutionModule登録と開始を完了する。session、artifact、sampler、前節の不変templateとfreeze済みdrawingRoutes、rendererはloop外で保持する。描画もsurfaceも持たないCPU-only Pipelineにはsession・GPU・ExecutionRuntimeを要求しない。
- **上限**: native表示の既定は一slotである。複数slotを選ぶなら有限上限、予算、frame間競合を明示する。slotにはacquired frame、acquire/render-done semaphore、batch、snapshot、context binding、開始済Taskを保持し、最後の回収まで空けない。未完了snapshotや仕事を無制限にqueueしない。
- **thread**: 宣言と送信の開始はExecution handlerの外にあるアプリcoordinatorが行う。native操作はgraphicsOwner.InvokeAsyncで固定ownerへ渡す。handler内で再入pumpしない。ConfigureAwait(false)をwindow thread復帰保証にしない。
- **表示同期**: backendごとのbinary同期能力を構築時に検査し、logical同queue順序しかない経路をnative semaphore保証へ昇格させない。QueueSubmitDescのWaitSemaphoresへacquire信号、SignalSemaphoresへrender-done信号を明示する。surface.Present([renderDone])はSubmit受理直後、CPUのGPU完了待機より前に行う。Present状態へのExportはPresent呼出しではない。receipt、submission完了、surface release、実表示時刻は別の事実である。
- **対応範囲**: 下の取得→非同期prepare例は、取得imageをawait越しに保持できるnative backendだけを対象とする。turn寿命のBrowser表示はこの共同表示APIでは未対応として入場前に拒否する。基準コードの src/UI/Lumyte.UI.Application/UiHostPresenter.cs が行う取得前offscreen準備と同turn最終記録・Submit・Presentを、同turn中のawait再開で代用しない。Browserに必要なresolved targetへの遅延束縛・同期最終record境界は本ADRで提供しない。offscreen処理の可否とBrowser表示の可否は別に判定する。

~~~text
起動: runtime/module接続・開始 → session/template/routes/renderer → 有限slot
各turn: window入力 → simulation → owner phase pump → 完了済Taskの観測
  slotが空: 不変snapshot → native acquire → 全segment宣言・Seal
    → 非同期prepare/record（この間もturnとpumpを継続）
    → 一回のbatch Submit(acquire wait / render-done signal)
    → 明示Present(render-done) → GPU完了 と surface release
    → command-first退役 → slotを空ける
停止: 新規入場停止 → 開始済Taskを全観測/drain → 世代別回収 → owner停止
~~~

次のC#は変更後の公開境界を使うnative host例である。AppSlotはアプリの所有権台帳entryで、下記のフィールドを持つ。PrepareMixedAsyncは直前のmixed例を関数化したもので、同じslot.Batchへ登録し、Sealと三つのrecordが終わるまでを行う。送信やPresentは行わない。

~~~csharp
using System;
using System.Threading.Tasks;
using Lumyte.Execution;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Planning;
using Lumyte.Graphics.Drawing2D;

// アプリのdata。binary semaphoreはdeviceで作成済み、同deviceで未使用。
sealed class AppSlot
{
    public required IGraphicsSemaphore Acquire;
    public required IGraphicsSemaphore RenderDone;
    public IGraphicsSurfaceFrame? Surface;
    public GraphicsSubmissionBatch? Batch;
    public Task? Work;
    public SurfaceStatus AcquireStatus;
    public SurfaceStatus? PresentStatus;
    public bool Quarantined;
    public bool SurfaceRegistered; // batchの存在とは別に登録成功を記録する。
}

async Task DrawNativeAsync(AppSlot slot, DrawingSceneSnapshot2D snapshot)
{
    slot.SurfaceRegistered = false;
    ValueTask<SurfaceAcquireResult> acquiring = default;
    await graphicsOwner.InvokeAsync(() =>
        acquiring = swapchain.AcquireNextFrameAsync(slot.Acquire, admissionToken));
    SurfaceAcquireResult result = await acquiring;
    slot.Surface = result.Frame; // 成功leaseは次の失敗点より前に台帳へ登録。
    slot.AcquireStatus = result.Status;
    if (result.Frame is null) return;

    slot.PresentStatus = null;
    slot.Batch = session.CreateSubmissionBatch(maximumSegments: 3);
    await PrepareMixedAsync(slot.Batch, slot.Surface, snapshot,
        () => slot.SurfaceRegistered = true, admissionToken);
    await graphicsOwner.InvokeAsync(() =>
    {
        slot.Batch.Submit(new QueueSubmitDesc
        {
            CommandBuffers = slot.Batch.CommandBuffers, // bridgeも含む正確な順序列。
            WaitSemaphores = [new() { Semaphore = slot.Acquire,
                Stages = slot.Batch.AcquireWaitStages }],
            SignalSemaphores = [slot.RenderDone],
        }); // 全参加者を試行前に隔離、成功receiptを一回attach。
        slot.PresentStatus = slot.Surface.Present([slot.RenderDone]);
    }); // SubmitとPresentの間にawaitや取消し点を入れない。
    await slot.Batch.WaitForCompletionAsync();
    await slot.Batch.WaitForSurfaceReleaseAsync(slot.Surface);
    // 固定ownerへsurface.WaitForReleaseAsyncを配送し、別のrelease証明でpinを返す。
    await slot.Batch.RetireAsync(); // command→共有receipt一回→依存owner。
    await graphicsOwner.InvokeAsync(() => slot.Surface.Dispose(), cleanup: true);
    slot.Surface = null;
    slot.Batch = null;
}

// runtime登録・開始とtemplate/routes構築はloop外。sessionはtemplate構築より前に作る。
var session = new GraphicsDeviceSession(graphicsOwner, device, graphicsBudget);
// 前節のtemplate/routesをこのsessionで構築してfreezeする。
var renderer = new Renderer2D<IGraphicsStageContext>(session, drawingRoutes);
AppSlot slot = nativeSlot; // default上限は一つ。snapshotはWorkとbatchが保持する。
bool admit = true;
while (!ExitRequested())
{
    PollWindowEvents();
    AdvanceSimulation();
    runtime.PumpHostLifecycle(64, TimeSpan.FromMilliseconds(2));
    runtime.PumpJobs(64, TimeSpan.FromMilliseconds(2));
    runtime.RunHostPhase(graphicsOwner.HostPhase, 64, TimeSpan.FromMilliseconds(2));

    if (slot.Work is { IsCompleted: true })
    {
        try { slot.Work.GetAwaiter().GetResult(); } // 完了済みだけを観測。
        catch (Exception error)
        {
            slot.Quarantined = true;
            admit = false; // このsurface/device世代だけの入場を止める。
            ReportGraphicsFailure(error);
        }
        slot.Work = null; // ownerとslot予約は回収成功まで残す。
        if (slot.AcquireStatus is SurfaceStatus.Outdated or SurfaceStatus.Lost or SurfaceStatus.DeviceLost
            || slot.PresentStatus is SurfaceStatus.Outdated or SurfaceStatus.Lost or SurfaceStatus.DeviceLost)
            admit = false; // 回収・再構成が済むまで、この世代に再入場させない。
        if (slot.AcquireStatus == SurfaceStatus.Suboptimal || slot.PresentStatus == SurfaceStatus.Suboptimal)
            admit = false; // この例は安全側で停止し、全image返却後の再構成へ進む。
    }
    if (admit && slot.Work is null && !slot.Quarantined && IsDrawable())
        slot.Work = DrawNativeAsync(slot, CaptureImmutableScene());
    WaitForEventOrWorkWithFiniteTimeout(); // GPU fenceの同期waitではない。
}
// 終了時もturn/pumpを続け、開始済Workを観測し、下記の順で回収する。
// 回収失敗時はslot/session/runtimeを保持。using/finallyで強制解放しない。
~~~

- **アプリの接続点**: ExitRequested/PollWindowEvents/AdvanceSimulation/CaptureImmutableScene/IsDrawableは入力・時間・不変snapshot処理、ReportGraphicsFailureは障害通知、WaitForEventOrWorkWithFiniteTimeoutはwindow/owner仕事で起床する有限待機である。PrepareMixedAsyncは直前に展開した処理だけを行う。GPUの送信・表示操作を未定義の描画一括helperへ隠さない。contextsとsnapshotはbatchの使用ownerとして登録し、開始済prepare taskを全てdrainする。
- **待機中**: 入力、simulation、Execution phaseのpumpを続け、不完了Taskをowner上でGetResultしない。唯一のpump全体を未完了awaitの後ろへ置かない。独立owner pumpを持つ非owner coordinatorならawaitできる。HostPhaseはnative配送用でありゲームUpdateを自動実行しない。
- **snapshot**: simulationが進んでも入場済snapshot、upload、cacheを変更しない。入場前に最新snapshotを選べるが、受理済GPU仕事は捨てない。同frameのreadback依存CPU処理だけは実完了を待ち、前回結果を次tickへ渡すなら明示versionと遅延policyを使う。

障害と終了ではslotを単なるTaskではなく回収可能なowner集合として扱う。

| 境界・結果 | hostの処理と保持 |
| --- | --- |
| zero-size・Timeout | 新規描画を見送り、イベント・simulation・退役を続ける。取得imageなし。busy waitしない。 |
| acquire Suboptimal | 返されたimageを使用できる。世代の全image解放後に明示再構成する。 |
| acquire Outdated / Lost | imageなしでそのsurface世代の入場停止。全旧image回収後に再構成 / surface再作成する。 |
| acquire DeviceLost | 当該device世代の入場停止。信頼された世代退役と回収を行ってから再作成。 |
| Submit前の失敗・取消し | 開始済全taskをdrainし、取得済imageはWaitForSurfaceReleaseAsyncでnative acquisition releaseを確認し、未送信batchをAbortAsyncで返す。消費済frameを再実行しない。 |
| Submit受理不明 | 全ticket・command・lease・semaphore・surfaceを隔離。再Submitしない。信頼されたdevice世代退役証明が必要。 |
| Submit成功後、Present前の例外 | 既知receiptを保持。GPU完了と取得imageのnative releaseを別々に確認し、不要なPresentを再試行しない。 |
| Present Outdated / Lost / DeviceLost | Present結果だけで解放しない。既知submissionをdrainし、surface releaseまたは信頼された世代退役を確認して対象世代だけを再作成する。 |
| Wait取消し・resize | GPU仕事は残る。slotを保持し待機・回収を再試行。旧image解放前にReconfigureせず、size/format依存計画だけを無効化する。 |
| shutdown | 入場停止→開始済Task観測/drain→既知GPU完了または世代退役→surface release→command-first回収→frame/import→renderer/routes/template→session→swapchain/surface/device→runtimeの順。 |

表の未送信・既知submissionの回収は次のアプリ関数に対応する。呼出し前に開始済Workをpump継続下で完了まで観測し、その後の確定状態でsubmittedを選ぶ。submittedは実Submitからreceiptが戻った事実であり、Taskの成功・失敗から推測しない。受理不明ではこの関数へ入らず、後述の証明付き経路を使う。

~~~csharp
async Task RetireKnownSlotAsync(AppSlot slot, bool submitted)
{
    if (slot.Work is { } started)
    {
        try { await started; } // 呼出し前に完了済み。結果/例外を観測して全開始仕事をdrain。
        catch (Exception error) { ReportGraphicsFailure(error); } // 主例外は台帳にも保存。
        slot.Work = null;
    }
    if (submitted && slot.Batch is { } known) await known.WaitForCompletionAsync();
    if (slot.Surface is { } image)
    {
        if (slot.SurfaceRegistered)
            await slot.Batch!.WaitForSurfaceReleaseAsync(image);
        else // batch作成済みでもAcquireSurfaceImageの登録失敗なら直接releaseを確認。
        {
            ValueTask release = default;
            await graphicsOwner.InvokeAsync(() => release = image.WaitForReleaseAsync(), cleanup: true);
            await release;
        }
    }
    if (slot.Batch is { } batch)
    {
        if (submitted) await batch.RetireAsync();
        else await batch.AbortAsync(); // 未登録surfaceの回収後も、未送信batchを返す。
        slot.SurfaceRegistered = false; // pin返却済み。続くsurface.Dispose失敗の再試行は直接release側。
        slot.Batch = null; // ここまでの成功後だけ参照を外す。
    }
    if (slot.Surface is { } released)
    {
        await graphicsOwner.InvokeAsync(released.Dispose, cleanup: true);
        slot.Surface = null;
        slot.SurfaceRegistered = false;
    }
}

// 入場停止と全開始Workの完了観測後、既知の状態だけを渡す。
// 待機・回収Taskを同期blockせず、以下のpump継続で完了させる。
Task cleanup = RetireKnownSlotAsync(slot, submitted: knownSubmitted);
while (!cleanup.IsCompleted)
{
    PollWindowEvents();
    runtime.PumpHostLifecycle(64, TimeSpan.FromMilliseconds(2));
    runtime.PumpJobs(64, TimeSpan.FromMilliseconds(2));
    runtime.RunHostPhase(graphicsOwner.HostPhase, 64, TimeSpan.FromMilliseconds(2));
    WaitForEventOrWorkWithFiniteTimeout();
}
cleanup.GetAwaiter().GetResult(); // 失敗時はownerを保持し、再構成/停止を進めない。
~~~

- **再構成との対応**: 上の回収成功後にだけgraphicsOwner.InvokeAsyncからswapchain.Reconfigure(nextConfiguration)を実行し、その配送Taskも同じpumpで完了させる。全slot上限を増やした構成では対象世代の全slot回収を先に行う。Lostなら既存swapchain/surfaceを解放して作り直し、zero-sizeでは新しい正のsizeが決まるまで再構成を始めない。
- **受理不明との対応**: 全開始済Taskの観測後に、信頼された同device世代のGraphicsDeviceRetirementProofを得られた場合だけbatch.RecoverAsync(proof)を呼ぶ。RecoverはGPU側の隔離を回収するが、surface releaseの証明を捏造せずsurface pinを残す。その後WaitForSurfaceReleaseAsync→RetireAsync→surface.Disposeへ進む。native surfaceも失われreleaseを証明できない場合は当該native lifetimeを保証する退役確認が得られるまで保持し、通常のAbort・再Submit・Reconfigureで回避しない。
- **終了との対応**: 全slotの上記回収後にsession.DrainAsyncで残る部分frame/epochを回収し、独立export/useが返ったことを確認してtemplate、session.RetireAsync、swapchain/surface/device、runtime停止の順へ進む。停止中の配送も同じpumpで待ち、失敗したownerを外側finallyでDisposeしない。

- **再試行可能性**: sessionはbatchと部分prepareを、hostはsurfaceとsemaphoreを常に到達可能な台帳へ保持する。表示中の使用leaseはhostがWaitForSurfaceReleaseAsyncでsurface releaseを確認するまでpinし、session.DrainAsyncがGPU完了側を回収してもこのpinを失わない。回収再試行で成功済解放を繰り返さず、主例外とcleanup失敗を別保存する。Runtime停止Task中も必要なHost lifecycle/cleanup pumpを継続する。simulationやイベント処理の例外も同じ停止・回収へ入り、外側finallyで強制Disposeしない。証明のない有限時間解放は保証しない。

### 参照根拠の区分

本ADRの根拠は冒頭の固定基準コミットに存在する過去ADRとソースに限定し、追加提案の契約と実在APIを区別する。

- **過去の判断**: 次節の過去ADRと対象節を使い、変更範囲と継続範囲を分ける。
- **Graphics抽象**: src/Graphics/Lumyte.Graphics.Abstractions/IGraphicsCommandBuffer.cs、IRenderEncoder.cs、IGraphicsQueue.cs、QueueSubmitDesc.cs、IGraphicsSubmission.cs、IGraphicsSwapchain.cs、IGraphicsSurfaceFrame.cs、IGraphicsSemaphore.cs の記録・明示同期・所有契約を使う。
- **実行と表示**: src/Core/Lumyte.Execution/ExecutionRuntime.cs のpump、src/UI/Lumyte.UI.Application/UiHostPresenter.cs の取得・準備・表示順序を使う。Planningの新APIは以下の追加設計であり、基準実装の機能とは扱わない。

### 過去の判断との関係

主要判断はGPU実行基盤の統合である。次の一部を変更し、既存ADRの判断本文は保持する。

- **未実装設計との区分**: [RESOURCES-0001](../resources/RESOURCES-0001-typed-pipelines.md#型の解決と責務)の型付きStageをResourcesへ置く案とは異なり、一般DAGとcontext型の要件をCoreへ分離する。同ADRの構築から公開まで・所有権と解放で示す借用と公開後の保持、失敗時cleanup再試行は継続する。基準コミットで同ADRは提案であり、これは旧設計との差分であって実在するCore Pipeline APIの削除ではない。

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
- **依存方向**: 一般的な型付きDAG・lease・評価と回収はCoreへ置く。Coreは画像・device型とExecutionRuntimeへ依存せず、Executionを使うStageだけがその型付きサービスへ依存する。
- **接続**: Graphics部品は共通builderへ初期CPU計算、GPU処理、明示Upload/Downloadを宣言する。独立runのleaseを再帰的なPrepareで継ぎ足さない。
- **部品の拡張**: 公開IStageは不変factoryを持つ。factoryの要求申告を検証してからprocessorを作る。CPU processorは入力leaseを読み不変出力を公開し、Graphics processorは宣言scopeへkernel・resource use・argumentsを追加する。外部作者にも同じ契約を適用し、既知型switchで組込み専用経路を作らない。
- **構築と再利用**: Stageと単項Thenは同じnode追加で、呼出しごとに別nodeを作る。同じPortはproducerを共有し、多入力Stageで分岐・合流する。foreign graph、cycle、freeze後の変更を拒否する。Buildは算法・schema・artifactを固定し、frameのInvokeは同一scopeへ有限個のinstanceを作る。

### 型付きcontextとCoreの公開境界

Stageが必要とするサービスをcontext型へ固定し、Coreの評価・所有権とサービス供給を分離する。

- **要求の型**: IStage<in TContext, A, O>は必要サービスだけをinterfaceで要求する。TContextは参照型、payload A/Oはinvariantとする。IStageFactoryとIStageProcessorもcontextだけを反変にする。
- **接続の方向**: IGraphicsStageContextだけを要求するStageは、それを継承するIGraphicsExecutionContextのgraphへ接続できる。逆方向と必要能力のないgraphへの接続はコンパイルエラーにする。複数能力は作者がcomposite interfaceを明示し、Stage列からinterfaceのunionを自動推論しない。
- **要件の保持**: PipelineGraph<TContext, TInput>、Port<TContext, T>、読み取り専用IPipelineDefinition<in TContext, A, O>、IPipelineとtemplate/Invokeまでcontext要件を保持する。class自体はvariantにしない。definitionは反変、factoryを引数に取るvisitorは共変とし、型付きfactoryとedgeの受渡しでもvarianceを成立させる。1〜6入力は同じ一つのTContextを使う。
- **構築と呼出し**: factoryのconstructorがshader/artifactなど不変のbuild依存をcaptureし、CreateはStageBuildContextだけを受ける。毎回のframe/ExecutionサービスはPrepareAsyncの独立したTContext services引数で受け、processor fieldへ保存しない。StagePreparationContextは非genericのlifecycle境界とし、invariantな`StagePreparationContext<TContext>`で反変性を壊さない。
- **型保証の範囲**: context型が保証するのは宣言されたサービス能力のavailabilityであり、security sandboxではない。factory constructorへの明示依存注入は自由であり、captureされたruntime依存までgraphの型制約が検出・禁止するとはしない。graph側でRuntime利用を型付き契約として制御するStageは、constructor captureだけで済ませずIExecutionStageContextのinvocation capabilityを必須にする。
- **実行の選択**: 純粋CPU Stageは空のIPipelineContextだけを要求できる。Execution利用StageはIExecutionStageContextを要求し、そのサービスでdispatchとawait後の再開を行う。Core hostに必須runtime引数・型判定・サービス探索・隠れたschedulerを置かず、起動・停止を引き受けない。
- **供給と権限**: `IPipelineContextFactory<out TContext>`が呼出し単位のbindingを貸す。bindingのlifecycle authorityは要求検証・opaque値検証・owner台帳への接続だけを担い、サービス取得口ではない。純粋CPUではauthorityを省略でき、その場合opaque値を拒否する。Graphicsでは同frameのauthorityを必須にし、公開前にownerを必ず台帳へ登録する。contextの型検査はframe所属・scope期限の動的検証を代替しない。CoreのOwnとauthorityのOwnは同じ台帳への一回の移管であり、二重所有・二重Disposeを作らない。binding.Disposeは使用leaseの返却だけで、GPU使用中のownerを解放しない。
- **native ownerとの区別**: GraphicsDeviceSessionのGraphicsExecutionModuleと固定owner pumpは、Stageが任意にExecutionを使う契約とは別接続である。本判断だけでframeのnative hostからExecutionRuntimeが不要になるとはしない。
- **宣言の受渡し**: IPipelineDefinitionのvisitorはcaptured factoryそのものと型付き入出力edgeを宣言順に渡す。型名や診断descriptionからfactoryを再構築せず、不変パラメーターを保持する。
- **template展開**: 別構築へ挿入するtemplateは同一graphへ展開してproducerを付け直す。独立PrepareAsyncの結果leaseを再帰的に接続しない。
- **backendとCoreの責務**: context factoryはinvocationサービスを明示供給する。Coreは要求検証、processor生成、入力admission、出力検証、scope閉鎖と失敗時回収を所有する。processorはrun間共有なのでawaitを跨いでも再入可能とし、invocation状態を可変fieldへ保持しない。
- **所属検査の期限**: 入力に含まれる全opaque子のframe・世代・producer所属は、最初のCPU processorを呼ぶ前のadmissionで検査する。出力は公開前、GPU宣言viewは各操作前に再検証し、不正入力でCPU副作用を先に起こさない。
- **CPUとopaque値**: CPUのRead/Publishとbackendの不透明な宣言参照を分ける。lifecycle scopeはhostが貸した正確なbindingだけへ結び付け、失効scope・foreign input・二重出力を拒否する。
- **検証の迂回禁止**: generic Read/Publishもbinding authorityの値検証を通す。Graphics便宜APIを使わなくても全opaque子のframe所属とread producer依存を照合する。独立ownerは公開前に失敗しても回収台帳へ残す。
- **再帰bundle**: DeviceとDeviceを含むbundleはCPU可読にしない。任意objectをreflection探索せず、対応するbundleが全ての子を列挙する。IImmutablePipelineInputは不変metadataの宣言であり、Device不在の証明ではない。
- **CPU作者契約**: 独自payloadはICpuPipelineValueで、Host・Device・ValueLease・ownerなど所属や寿命を持つ値を再帰的に含まないことを申告する。Deviceを含む構造はbundle契約で渡す。
- **宣言viewの寿命と予約**: GPU宣言viewとpassもinvocationへ束縛する。終了後の追加を拒否し、各操作の前に宣言数と保持量を予約する。宣言失敗時はframeを無効化し、部分計画を実行しない。

### lease・予算・非同期失敗の共通保証

型付きサービスへ変更しても、全ての評価を同じprovenanceと回収台帳で管理する。

- **provenance**: ValueLeaseは生成run・node・scope・世代とproducerを保持する。既定値、期限切れ、foreign run/node、無関係な出力の再公開を拒否し、generic経路にも同じ検証を適用する。borrowした入力を新しい所有出力として偽装しない。
- **scope**: StagePreparationContextのRead/Publish/Ownは一invocationだけ有効である。入力leaseは評価完了まで借用し、出力は公開前から独立ownerを登録する。終了後の読書き・二重Publishを拒否し、結果lease保持中は必要な依存とbindingを生存させる。
- **上限**: build時のDescribeからcompiled上限を固定する。実入力によるactual requirementsは各項目でactual <= compiledを要求し、超過を確保・宣言前に拒否する。node数、値の保持bytes、schema extent、pass/resource数、in-flightとcacheのpeakを別計上し、追加前に有限予算を予約する。
- **失敗と取消し**: 最初の失敗で後続の開始を止めても、既に開始した全ての非同期処理をdrainし、その出力とownerを回収する。一つのawait失敗や待機取消しで兄弟taskを置き去りにしない。cleanup失敗でも主例外と未返却owner・予約を保持し、成功済み返却を繰り返さず同じ台帳から再試行する。bindingとサービスはdrain・回収まで生存させる。

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

- **Canvasとcoverage**: CanvasのCPU記録は不変内容と描画順を保持する。shape/clipは同sample位置で評価し、未resolveのmembership/paint、ResolvedImage、raw MaskImageを別schemaにする。半pixelを覆う不透明描画二回はdrawごとresolveしてsource-overするprofileではalpha 0.75であり、scene末尾一回resolveへ移して0.5に変えない。
- **合成と背景効果**: paint内の隔離Composite、背景とのlayer blend、完成groupへのopacity、背景snapshotのeffect置換を別演算にする。背景効果の境界はeffect*c + original*(1-c)で、単純source-overへ置換しない。
- **SVG・文字**: SVG用paint/filterとCOLR paintの補間空間・alpha・座標・ClipBox・有限領域を保持し、無条件に同じ画像演算へ潰さない。usvgのgroup/paint/clip/mask/filter/patternをadapterで維持し、文字のpaint DAG・foreground座標・同内容の別配置を区別する。
- **地図**: 全tileのcasing→全tileの内線というglobal順、camera-relative精度、ownership clipとAA clipの区別、dash距離、pattern位相を渡す。style評価・label衝突回避・font解析は共通実行基盤に持ち込まない。
- **明示的な融合**: 統合するのは所有と計画であり、自動shader融合ではない。明示fused実装にも分離実装と同じtyped入出力、sample/resolve、color、precision、資源契約を要求する。

### 契約と API 差分

判断対象の公開契約を、基準コミットとの差分として示す。

- **比較の区分**: 基準コミットに下記のLumyte.Pipelines APIは存在しないため、全て追加として示す。旧Resources設計の変更範囲は前節に分離する。
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
+        public static IStage<IGraphicsStageContext, Device<ImageSchema>, Device<ImageSchema>> Stage(ImageEffectDefinition effect);
+    }
 }
 namespace Lumyte.Pipelines
 {
+    public interface IPipelineContext;
+    public interface IStage<in TContext, A, O> where TContext : class
+    { IStageFactory<TContext, A, O> Factory { get; } }
+    public interface IStageFactory<in TContext, A, O> where TContext : class
+    {
+        StageRequirements Describe(StageInputDescriptions inputs);
+        IStageProcessor<TContext, A, O> Create(StageBuildContext build);
+    }
+    public interface IStageProcessor<in TContext, A, O> : IDisposable where TContext : class
+    {
+        ValueTask<ValueLease<O>> PrepareAsync(StagePreparationContext lifecycle,
+            TContext services, ValueLease<A> input, CancellationToken cancellationToken);
+    }
+    // 2〜6入力もcontextだけ反変。同じTContextを全入力に適用する。
+    public interface IStage<in TContext, A, B, O> where TContext : class
+    { IStageFactory<TContext, A, B, O> Factory { get; } }
+    public interface IStageFactory<in TContext, A, B, O> where TContext : class
+    {
+        StageRequirements Describe(StageInputDescriptions inputs);
+        IStageProcessor<TContext, A, B, O> Create(StageBuildContext build);
+    }
+    public interface IStageProcessor<in TContext, A, B, O> : IDisposable where TContext : class
+    {
+        ValueTask<ValueLease<O>> PrepareAsync(StagePreparationContext lifecycle, TContext services,
+            ValueLease<A> a, ValueLease<B> b, CancellationToken cancellationToken);
+    }
+    // 例の三入力Contributeもpayload型を消さずに接続する。
+    public interface IStage<in TContext, A, B, C, O> where TContext : class
+    { IStageFactory<TContext, A, B, C, O> Factory { get; } }
+    public interface IStageFactory<in TContext, A, B, C, O> where TContext : class
+    {
+        StageRequirements Describe(StageInputDescriptions inputs);
+        IStageProcessor<TContext, A, B, C, O> Create(StageBuildContext build);
+    }
+    public interface IStageProcessor<in TContext, A, B, C, O> : IDisposable where TContext : class
+    {
+        ValueTask<ValueLease<O>> PrepareAsync(StagePreparationContext lifecycle, TContext services,
+            ValueLease<A> a, ValueLease<B> b, ValueLease<C> c, CancellationToken cancellationToken);
+    }
+    public sealed class StageBuildContext; // build寿命だけ。不変captureと予算を検証する。
+    public sealed class StagePreparationContext
+    {
+        // 全経路でprovenance、opaque子、producerを照合する。scope終了後は拒否。
+        // Deviceを含むbundleはCPU Read不可。authorityが存在してもこの禁止は変わらない。
+        public T Read<T>(ValueLease<T> value);
+        // backend用宣言view。Bindで結んだ同一authority以外は拒否。CPU bytesは公開しない。
+        public T ReadDeclaration<T>(ValueLease<T> value, IStageLifecycleAuthority authority);
+        public ValueLease<T> Publish<T>(T value);
+        public void Own(IDisposable owner);
+    }
+    public interface IPipelineContextFactory<out TContext> where TContext : class
+    { IPipelineContextBinding<TContext> Bind(StagePreparationContext lifecycle); }
+    public interface IPipelineContextBinding<out TContext> : IDisposable where TContext : class
+    {
+        TContext Services { get; }
+        IStageLifecycleAuthority? Authority { get; }
+    }
+    public interface IStageLifecycleAuthority
+    {
+        // サービス取得機能は持たない。確保前にactual <= compiledを検証する。
+        void Validate(StageRequirements compiled, StageRequirements actual);
+        void ValidateRead<T>(T value);
+        // 全opaque子のprovenance・frame/世代・producer readを検証/登録する。
+        void ValidateDeclarationRead<T>(T value);
+        void ValidatePublication<T>(T value);
+        void Own(IDisposable owner);
+    }
+    public interface ICpuPipelineValue;
+    public interface IPipelineBundle
+    {
+        // default値を含む全ての子を一回ずつ列挙。深さ16・総数64を上限とする。
+        void VisitChildren(IPipelineBundleVisitor visitor);
+    }
+    public interface IPipelineBundleVisitor { void Child<T>(T value); }
+    public readonly record struct PipelineBundle<A, B>(A First, B Second) : IPipelineBundle;
+    public sealed class PipelineBuildHost : IDisposable
+    {
+        // GPU/Execution具象依存なし。processor生成とCore台帳を所有する。
+        public PipelineBuildHost(PipelineBudget budget);
+        public IPipeline<TContext, A, O> Build<TContext, A, O>(
+            IPipelineDefinition<TContext, A, O> definition) where TContext : class;
+        public void Dispose();
+    }
+    public interface IPipeline<TContext, A, O> : IDisposable where TContext : class
+    {
+        // CPU評価または同frameへの宣言。bindingをdrain・回収まで保持する。
+        ValueTask<PipelineResult<O>> PrepareAsync(IPipelineContextFactory<TContext> contexts,
+            A input, CancellationToken cancellationToken = default);
+    }
+    public readonly record struct PipelineEdge<T>(int Index);
+    public interface IPipelineDefinitionVisitor<out TContext> where TContext : class
+    {
+        // captured factoryと正確なtyped edgeを渡す。多入力も同じcontext。
+        void Stage<A, O>(IStageFactory<TContext, A, O> factory,
+            PipelineEdge<A> input, PipelineEdge<O> output);
+    }
+    public interface IPipelineDefinition<in TContext, A, O> where TContext : class
+    {
+        PipelineEdge<O> OutputEdge { get; }
+        void Visit(IPipelineDefinitionVisitor<TContext> visitor);
+    }
+    public sealed class PipelineGraph<TContext, TInput> where TContext : class
+    {
+        public Port<TContext, TInput> Input { get; }
+        // 三入力もedgeとfactoryを型付きで保持し、framework tupleへの型消去はしない。
+        public Port<TContext, O> Stage<A, O>(IStage<TContext, A, O> stage, Port<TContext, A> input);
+        public Port<TContext, O> Stage<A, B, C, O>(IStage<TContext, A, B, C, O> stage,
+            Port<TContext, A> a, Port<TContext, B> b, Port<TContext, C> c);
+        public Port<TContext, O> Stage<A, B, O>(IStage<TContext, A, B, O> stage,
+            Port<TContext, A> a, Port<TContext, B> b);
+        public Port<TContext, O> Invoke<A, O>(IPipelineDefinition<TContext, A, O> definition,
+            Port<TContext, A> input);
+        public IPipelineDefinition<TContext, TInput, O> Complete<O>(Port<TContext, O> output);
+    }
+    public sealed class Port<TContext, T> where TContext : class
+    { public Port<TContext, O> Then<O>(IStage<TContext, T, O> stage); }
 }
 namespace Lumyte.Pipelines.Execution
 {
+    // 任意のStageサービス。CoreではなくExecution接続assemblyが所有する。
+    public interface IExecutionStageContext : IPipelineContext
+    { ExecutionRuntime Execution { get; } }
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
+    public interface IGraphicsStageContext : IPipelineContext
+    { GraphicsStageCapabilities Graphics { get; } }
+    // 複合能力は作者が明示する。自動union推論は行わない。
+    public interface IGraphicsExecutionContext : IGraphicsStageContext, IExecutionStageContext;
+    public sealed class GraphicsContextFactory : IPipelineContextFactory<IGraphicsStageContext>, IDisposable
+    {
+        // 同frameのauthority、scope期限、予約とowner台帳を必須で接続する。
+        public GraphicsContextFactory(GraphicsFrameScope frame);
+        public IPipelineContextBinding<IGraphicsStageContext> Bind(StagePreparationContext lifecycle);
+        public void Dispose();
+    }
+    public sealed class GraphicsStageCapabilities
+    {
+        // invocation限定。CPU bytesではなく同frameの宣言を読みproducerを登録する。
+        public GraphicsDeclarationScope Plan { get; }
+        // assemblyを跨ぐ公開境界。lifecycle.ReadDeclarationと正確なbinding authorityを使う。
+        // opaque wrapper/metadataを検査するだけでDeviceの画素取得を許さない。
+        public T ReadBundle<T>(StagePreparationContext lifecycle, ValueLease<T> input)
+            where T : IPipelineBundle;
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
+    public interface IGraphicsPlanSource<TContext, A, O> where TContext : class
+    {
+        // 意味論node/templateを同scopeへ宣言。effectのGPU Lowerはここで先行実行しない。
+        PlanValue<O> Declare(GraphicsPlanBuilder builder, IPipelineContextFactory<TContext> contexts, PlanValue<A> input);
+    }
+    // アプリがExecutionRuntimeへ登録し、Host配置なら同じthreadからphaseをpumpする。
+    public sealed class GraphicsExecutionModule : IExecutionModule
+    {
+        public PhaseKey HostPhase { get; }
+        // 固定ownerへの公開配送。async delegateを受けずaction内await不可。
+        // cleanupは停止中も配送可能。handler内再入pumpは禁止。
+        public ValueTask InvokeAsync(Action action, bool cleanup = false);
+    }
+    public sealed class GraphicsDeviceSession : IDisposable
+    {
+        // graphicsOwnerは既存ExecutionRuntimeへ登録済みの固定owner。runtime自体は借用する。
+        public GraphicsDeviceSession(GraphicsExecutionModule execution, IGraphicDevice device,
+            GraphicsPlanningBudget? budget = null);
+        public GraphicsFrameScope CreateFrame();
+        // 1以上かつ予算内の有限上限。同sessionのidentity/回収ledgerを共有。
+        public GraphicsSubmissionBatch CreateSubmissionBatch(int maximumSegments);
+        public GraphicsPipelineTemplate<TContext, A, O> Build<TContext, A, O>(
+            IPipelineDefinition<TContext, A, O> definition) where TContext : class;
+        public GraphicsExecution CreateExecution(PreparedGraphicsExecution prepared);
+        // 登録済batch/部分prepareもGPU側をdrain。surface未releaseのpinは残し、
+        // hostの明示release確認後にRetireを再試行する。surfaceを暗黙Presentしない。
+        public ValueTask DrainAsync();
+        public ValueTask RetireAsync();
+        // 使用中/隔離中は拒否。暗黙wait/device破壊はしない。
+        public void Dispose();
+    }
+    public sealed class GraphicsPipelineTemplate<TContext, A, O> :
+        IGraphicsPlanSource<TContext, A, O>, IDisposable where TContext : class
+    {
+        // definitionのcontext要件を保持。別pipelineを実行するopaque nodeではない。
+        public PlanValue<O> Declare(GraphicsPlanBuilder builder,
+            IPipelineContextFactory<TContext> contexts, PlanValue<A> input);
+        public void Dispose();
+    }
+    public sealed class GraphicsFrameScope : IDisposable
+    {
+        // 上位のRenderGraph/効果型へ逆依存しない共通builder。
+        public GraphicsPlanBuilder Plan { get; }
+        // 通常経路。呼出し時に一回だけframeを消費し、compile/prepare/execute/正常退役を統合。
+        // 失敗/取消し後の再実行も拒否。受理不明のownerはsessionの回収台帳に残す。
+        public ValueTask ExecuteAsync(CancellationToken cancellationToken = default);
+        // 同じatomic one-shotを消費。Seal済予約では唯一のrecord継続。
+        // 単一epochだけ。finished buffersとticketを返しSubmit/Present/Waitなし。
+        public ValueTask<PreparedSubmissionSegment> RecordForSubmissionAsync(
+            GraphicsSubmissionBatch batch, CancellationToken cancellationToken = default);
+        // 高度な明示経路。同じ実行権を消費し、統合ExecuteAsyncとの併用を拒否する。
+        public ValueTask<CompiledGraphicsPlan> CompileAsync(CancellationToken cancellationToken);
+        public ValueTask RetireAsync();
+    }
+    public sealed class CompiledGraphicsPlan : IDisposable
+    {
+        // Export済の最終PlanImageだけ。Prepare前に論理producer/epoch/最大保持量を予約。
+        public GraphicsImageExportReservation ReserveImageExport(PlanImage output);
+        // 一回だけ。最初の区間を準備し、全epochの実行/ownerを代表するpreparedを返す。
+        // 後段の区間準備は唯一のGraphicsExecutionが内部で進める。Submitなし。
+        public ValueTask<PreparedGraphicsExecution> PrepareAsync(CancellationToken cancellationToken);
+        // export/useを含むprepared ownerが残れば拒否し、回収台帳で再試行可能。
+        public ValueTask RetireAsync();
+    }
+    // compiledが発行。default/foreign/stale・Prepare後の予約追加を拒否。
+    public sealed class GraphicsImageExportReservation;
+    public sealed class PreparedGraphicsExecution : IDisposable
+    {
+        // 初期Prepare後、CreateExecution前に一予約一回だけ独立ownerを取得。
+        // 最終epoch未materializeでも予約とownerをpin。SubmitもValue公開もしない。
+        public GraphicsImageExportLease AcquireImageExport(GraphicsImageExportReservation reservation);
+        public void Dispose(); // 実行中は拒否。退役時もexport/use保持資源を解放しない。
+    }
+    public sealed record GraphicsExportedImage
+    {
+        // 借用view。DescriptionとOutgoingState/versionは同じ最終内容に束縛。
+        public IGraphicsTextureView View { get; }
+        public ImageDescription Description { get; }
+        public GraphicsAllocationIdentity Identity { get; }
+        public InteropSubresources Range { get; }
+        public InteropState OutgoingState { get; }
+        public ulong ContentVersion { get; }
+    }
+    public sealed class GraphicsImageExportLease
+    {
+        // 全最終producer成功・GPU完了・必要command退役後だけ。未完了/失敗時は拒否。
+        public GraphicsExportedImage Value { get; }
+        // 未返却leaseの公開済内容だけ。有限予算を予約して独立pinする。
+        // 元lease返却後もuseが生存し、実allocationは共通ledgerで一回だけ課金。
+        public GraphicsImageExportUseLease AcquireUse();
+        public ValueTask RetireAsync(); // 固定ownerへ配送。成功済返却は反復しない。
+    }
+    public sealed class GraphicsImageExportUseLease : IDisposable
+    {
+        public GraphicsExportedImage Value { get; }
+        public ValueTask RetireAsync();
+        public void Dispose(); // 固定owner上だけ。batch移管後は呼出し側から返さない。
+    }
+    public sealed class GraphicsExecution : IDisposable
+    {
+        // 一回claimしたpreparedで全epochを順次Prepare/Record/Submit/Waitする。
+        // 成功時は必要commandを退役し、export/useが保持するownerは残す。
+        // 取消しでも受理済仕事とownerはsessionに残る。
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
+        // 既知のincoming access/layout/initialized/versionを必須とする。未知状態は拒否。
+        public PlanImage ImportImage(IGraphicsTextureView view, IDisposable useLease,
+            ImageDescription description, InteropState incoming, ulong contentVersion);
+        // producer依存、incoming access/layout/initialized/versionと使用権を継承。
+        public PlanImage ImportImage(InteropImageVersion input);
+        // copy可能format/extentを検証。同targetへのpreserve依存付き新version。
+        public PlanImage CopyTo(PlanImage source, InteropImageVersion target);
+        public PlanImage AppendEffect(ImageEffectDefinition effect, PlanImage input);
+        public PlanValue<O> Invoke<A, O>(IGraphicsPlanSource<IGraphicsStageContext, A, O> source, PlanValue<A> input);
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
-    public sealed partial class Renderer2D
-    {
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
-    }
+    // 不変sceneは全体ordinalとsnapshot identityを持つ。既存Canvas記録から切り出す追加型。
+    public sealed class DrawingSceneSnapshot2D;
+    // 実際の型定義は意味論metadataのみ。native ownerを持たない。
+    public sealed class DrawRequest2D
+    {
+        public DrawingState2D DrawingState { get; }
+        public IReadOnlyList<DrawingCommand2D> ClipAncestry { get; }
+        // target/sample/color/ROIの正確な記述を保持する不変metadata。
+        public DrawingSampleTarget2D SampleTarget { get; }
+        public DrawingColorDescription2D Color { get; }
+        public ImageRegion RegionOfInterest { get; }
+        public int PainterOrder { get; }
+        public BackdropVersion? RequiredBackdropVersion { get; }
+    }
+    public sealed class DrawingSampleTarget2D; // sample配置・座標写像・target identity。
+    public sealed class DrawingColorDescription2D; // format・色空間・alpha・精度。
+    public readonly record struct BackdropVersion; // frame/target/内容versionに束縛。defaultは無効。
+    // CPU metadataにはDevice/lease/ownerを再帰的に含めない。
+    public sealed record DrawingCommandMetadata2D<TCommand>(
+        TCommand Command, DrawRequest2D Drawing) : ICpuPipelineValue where TCommand : DrawingCommand2D;
+    // Image/Versionは双方あり、または双方なし。存在する全Device子を列挙する。
+    public readonly record struct DrawingBackdrop2D(Device<ImageSchema>? Image,
+        BackdropVersion? Version) : IPipelineBundle
+    { public void VisitChildren(IPipelineBundleVisitor visitor); }
+    // frame-local。MetadataとBackdropを列挙。全体はCPU Read不可。
+    public sealed record DrawingCommandPipelineRequest2D<TCommand>(
+        DrawingCommandMetadata2D<TCommand> Metadata, DrawingBackdrop2D Backdrop)
+        : IPipelineBundle where TCommand : DrawingCommand2D
+    { public void VisitChildren(IPipelineBundleVisitor visitor); }
+    public static class DrawingCommandInputs2D
+    {
+        // 公開adapter。Graphics能力で宣言viewを借用し、検証後metadataだけを公開。
+        public static IStage<IGraphicsStageContext, DrawingCommandPipelineRequest2D<TCommand>,
+            DrawingCommandMetadata2D<TCommand>> Metadata<TCommand>() where TCommand : DrawingCommand2D;
+        // 背景をopaqueのまま公開し、snapshot copy producerへのread依存を継承する。
+        public static IStage<IGraphicsStageContext, DrawingCommandPipelineRequest2D<TCommand>,
+            DrawingBackdrop2D> Backdrop<TCommand>() where TCommand : DrawingCommand2D;
+    }
+    public enum Composition2D { SourceOver, ReplaceWithBackdrop }
+    // Deviceを含むopaque bundle。Imageを必ずVisitChildrenへ渡す。CPU可読にしない。
+    public readonly record struct DrawingCommandResult2D(Device<ImageSchema> Image,
+        Composition2D Composition, BackdropVersion? BackdropVersion) : IPipelineBundle
+    { public void VisitChildren(IPipelineBundleVisitor visitor); }
+    // 値はregistryだけが発行する。default/foreign/stale keyを拒否する。
+    public readonly struct DrawingPipelineRoute2D<TContext, TCommand>
+        where TContext : class where TCommand : DrawingCommand2D;
+    public sealed class DrawingPipelineRoutesBuilder2D<TContext> where TContext : class
+    {
+        public DrawingPipelineRoutesBuilder2D(GraphicsDeviceSession session);
+        public DrawingPipelineRoute2D<TContext, TCommand> Register<TCommand>(
+            GraphicsPipelineTemplate<TContext, DrawingCommandPipelineRequest2D<TCommand>,
+                DrawingCommandResult2D> template) where TCommand : DrawingCommand2D;
+        // 同一command型の重複default、foreign keyを拒否する。
+        public void SetDefault<TCommand>(DrawingPipelineRoute2D<TContext, TCommand> route)
+            where TCommand : DrawingCommand2D;
+        // 以後builderを変更できない。不変registryはtemplateを借用し送信しない。
+        public DrawingPipelineRoutes2D<TContext> Freeze();
+    }
+    public sealed class DrawingPipelineRoutes2D<TContext> where TContext : class;
+    public sealed class DrawingPipelineOverrides2D<TContext> where TContext : class;
+    public sealed class DrawingPipelineOverrideBuilder2D<TContext> where TContext : class
+    {
+        public DrawingPipelineOverrideBuilder2D(DrawingSceneSnapshot2D scene,
+            DrawingPipelineRoutes2D<TContext> routes);
+        // snapshot全体ordinalのactual型を照合。重複/範囲外/異registryは拒否する。
+        public void Set<TCommand>(int ordinal, DrawingPipelineRoute2D<TContext, TCommand> route)
+            where TCommand : DrawingCommand2D;
+        public DrawingPipelineOverrides2D<TContext> Freeze();
+    }
+    public sealed class Renderer2D<TContext> where TContext : class
+    {
+        // registryを借用する。単一算法のfallback overloadは設けない。
+        public Renderer2D(GraphicsDeviceSession session, DrawingPipelineRoutes2D<TContext> routes);
+        // 全選択を部分宣言前に検証。contextsは同frameのauthorityを必須とする。
+        // 以後の宣言失敗はframeを無効化。bindingとownerはdrain/回収まで保持する。
+        public PlanImage Declare(GraphicsFrameScope frame, DrawingSceneSnapshot2D scene,
+            IPipelineContextFactory<TContext> contexts, DrawingPipelineOverrides2D<TContext>? overrides = null);
+        // 同frame targetをpreserve。通常Declareと同じpainter/backdrop規則。
+        public PlanImage DeclareOnto(GraphicsFrameScope frame, DrawingSceneSnapshot2D scene,
+            IPipelineContextFactory<TContext> contexts, PlanImage target,
+            DrawingPipelineOverrides2D<TContext>? overrides = null);
+    }
 }
~~~

共有identityと取得初期状態は、基準コミットのGraphics抽象へ次の公開metadataを追加して取得する。backendが実allocationから値を発行し、利用者がobject参照やview名で別名を推測しない。

~~~diff
 namespace Lumyte.Graphics.Abstractions
 {
+    // 同device世代の同allocationは型付き別名を含め同じ値。defaultは無効。
+    public readonly struct GraphicsAllocationIdentity;
+    public interface IGraphicsResourceIdentitySource
+    {
+        public GraphicsAllocationIdentity AllocationIdentity { get; }
+    }
-    public interface IGraphicsTexture : IDisposable
+    public interface IGraphicsTexture : IDisposable, IGraphicsResourceIdentitySource
     { /* 既存メンバーは維持。viewはTextureのidentityとInfoのrangeを使う。 */ }
-    public interface IGraphicsBuffer<T> : IDisposable where T : unmanaged
+    public interface IGraphicsBuffer<T> : IDisposable, IGraphicsResourceIdentitySource where T : unmanaged
     { /* 既存メンバーは維持。 */ }
-    public interface IGraphicsShaderDataBuffer<T> : IDisposable where T : struct, IShaderArguments
+    public interface IGraphicsShaderDataBuffer<T> : IDisposable, IGraphicsResourceIdentitySource
+        where T : struct, IShaderArguments
     { /* 既存メンバーは維持。同allocation wrapperは同identityを公開。 */ }
+    public readonly record struct SurfaceImageEntryState(TextureState Layout,
+        BarrierScope Access, bool Initialized, ulong ContentVersion);
     public interface IGraphicsSurfaceFrame : IDisposable
     {
+        // 取得成功時の実状態。acquire同期を不要にする値ではない。
+        // 初回Undefined/再利用Presentをbackendが区別。未知状態ではinteropを拒否。
+        public SurfaceImageEntryState EntryState { get; }
     }
 }
~~~

共同送信の追加境界は次を正規宣言とする。全て基準コミットに対する追加提案であり、既存QueueSubmitDesc・surface操作の現実装とは区別する。

~~~diff
 namespace Lumyte.Graphics.Planning
 {
+    public readonly record struct InteropState(BarrierScope Access,
+        TextureState? Layout, bool Initialized);
+    // buffer byte range、texture aspect/mip/layerを区別する有限範囲。
+    public readonly struct InteropSubresources;
+    public abstract class InteropResourceLease
+    {
+        public GraphicsAllocationIdentity Identity { get; }
+        public InteropSubresources Range { get; }
+        public InteropState InitialState { get; }
+        public InteropResourceVersion InitialVersion { get; }
+    }
+    public sealed class InteropImageLease : InteropResourceLease
+    {
+        // 借用view。raw作者はこれを使い実command-local準備を行える。
+        public IGraphicsTextureView View { get; }
+        public new InteropImageVersion InitialVersion { get; }
+    }
+    // 未完了producer依存を含む宣言handle。CPU内容やallocation ownerではない。
+    public abstract class InteropResourceVersion
+    {
+        public InteropResourceLease Resource { get; }
+    }
+    public sealed class InteropImageVersion : InteropResourceVersion
+    { public new InteropImageLease Resource { get; } }
+    public enum InteropAccess { Read, WriteDiscard, ReadWrite }
+    // writeはAddDirect時に後継versionを生成。ReadWriteはinitialized入力必須。
+    public sealed record DirectResourceUse(InteropResourceVersion Input,
+        InteropSubresources Range, InteropAccess Access,
+        InteropState Entry, InteropState Exit);
+    public sealed record DirectSegmentSource
+    {
+        // 未記録・同device。AddDirect成功時に実command所有権を一回移管。
+        public required IGraphicsCommandBuffer Commands { get; init; }
+        public required IReadOnlyList<DirectResourceUse> Uses { get; init; }
+        // 不変・有限。推移参照/whole-table等の物理closureも含む。
+        public required IReadOnlyList<InteropResourceLease> ResourceClosure { get; init; }
+        // 独立使用lease/準備ownerだけを移管。allocation自体は借用。
+        public required IReadOnlyList<IDisposable> UseOwners { get; init; }
+        // 上記実commandへ同期記録。Finishはbatchが行う。完全申告は作者契約。
+        // 未申告参照、allocation/table追加、Submitは禁止。
+        public required Action<IGraphicsCommandBuffer> Record { get; init; }
+    }
+    // batchが発行。default/foreign/staleを拒否。
+    public readonly struct DirectSegmentHandle;
+    public sealed class PreparedSubmissionSegment
+    {
+        public IReadOnlyList<IGraphicsCommandBuffer> CommandBuffers { get; }
+        public SubmissionOwnershipTicket Ownership { get; }
+    }
+    // 実command集合/世代/epoch/producerへ束縛された回収entry。独立Dispose不可。
+    public sealed class SubmissionOwnershipTicket;
+    // 構築不可。実IGraphicsSubmissionはbatchだけが所有する借用view。
+    public sealed class GraphicsSubmissionReceipt
+    {
+        public IReadOnlyList<SubmissionOwnershipTicket> ExpectedCoverage { get; }
+        public IReadOnlyList<IGraphicsCommandBuffer> ActualCommands { get; }
+    }
+    public sealed class GraphicsSubmissionBatch
+    {
+        // 固定owner上で実初期state/access/identityをbackendから取得。
+        // turn寿命表示は入場前に拒否。
+        // 元surfaceはhost所有。使用lease返却はsurface releaseではない。
+        // hostのsurface pinをrelease確認まで残し、session drainだけでは返さない。
+        // 登録/pinの公開は成功時だけ。失敗時はsurface未登録のままで、
+        // 内部の部分ownerだけをsessionが回収する。hostは元surfaceを保持する。
+        public InteropImageLease AcquireSurfaceImage(IGraphicsSurfaceFrame surface);
+        // 使用権を一回移管しbackend identityへ正規化。既知incoming状態必須。
+        // export useではValueのview/description/state/versionとの一致も検証。
+        public InteropImageLease AcquireImage(IGraphicsTextureView view,
+            IDisposable independentUseLease, ImageDescription description,
+            InteropState incoming, ulong contentVersion);
+        public InteropResourceLease AcquireBuffer<T>(IGraphicsBuffer<T> buffer,
+            IDisposable independentUseLease, InteropSubresources range,
+            InteropState incoming, ulong contentVersion) where T : unmanaged;
+        public DirectSegmentHandle AddDirect(DirectSegmentSource source);
+        public InteropImageVersion Output(DirectSegmentHandle segment, InteropImageLease image);
+        // 宣言済frameのoutputを同batchの未完了producerとして公開。
+        public InteropImageVersion AddFrame(GraphicsFrameScope frame, PlanImage output);
+        // 同一取得imageの最終versionだけ。Present状態へのbarrier/rootを追加。
+        public void ExportForPresent(InteropImageVersion output);
+        public void Own(IDisposable useOwner);
+        // 最初のawait前に全frameをatomic claim/freeze。単一epochだけ。
+        // 全manifest→共通compile→境界barrier。Submit/Presentなし。
+        public ValueTask SealAsync(CancellationToken cancellationToken = default);
+        public ValueTask<PreparedSubmissionSegment> RecordDirectAsync(
+            DirectSegmentHandle segment, CancellationToken cancellationToken = default);
+        // 全segment ready後だけ公開。bridgeを含み順序と実command identityを固定。
+        public IReadOnlyList<IGraphicsCommandBuffer> CommandBuffers { get; }
+        public PipelineStage AcquireWaitStages { get; }
+        // 固定owner。全ticketを先に隔離→実Queue.Submit一回→receipt一回attach。
+        // exact commands/coverage/waits/signalsを検証。再Submit禁止。
+        public GraphicsSubmissionReceipt Submit(QueueSubmitDesc description);
+        // 取消しは待機だけ。surface releaseを含まない。
+        public ValueTask WaitForCompletionAsync(CancellationToken cancellationToken = default);
+        // hostが明示呼出し。登録済surfaceだけに既存WaitForReleaseAsyncを配送し、
+        // 完了を記録してsurface pinを返す。Submit/Present/GPU waitはしない。
+        // 取消し時はpinを維持。未送信acquireのreleaseにも同じ境界を使う。
+        public ValueTask WaitForSurfaceReleaseAsync(IGraphicsSurfaceFrame surface,
+            CancellationToken cancellationToken = default);
+        // 未送信仕事だけ。開始済prepareを全drainしてcommand-first回収。
+        public ValueTask AbortAsync();
+        // GPU完了と必要surface pin返却後、またはAbortの回収再試行。
+        // 使用中/受理不明は拒否。独立exportが残るownerも保持。
+        public ValueTask RetireAsync();
+        // 同device世代のGPU側回収だけ。surface pinは別のrelease確認まで残す。
+        public ValueTask RecoverAsync(GraphicsDeviceRetirementProof proof);
+    }
 }
~~~

- **所有の一意性**: AcquireImage/AcquireBufferの使用権は共通ledgerへ一回移す。frameのimportは制限viewを借り、同じIDisposableをframeとbatchへ別々にOwnしない。DirectSegmentSource採用失敗時は呼出し側がownerを保ち、成功後はbatchだけが回収する。frame作成直後の失敗もsessionが保持する。
- **manifestと実記録**: DirectResourceUseは入力versionと境界状態を記述し、AddDirectがwriteの後継versionを一度だけ発行する。Outputはその登録結果だけを返す。entryへの遷移はbridgeが行い、raw recordはentryから開始してexitを満たす。準備済tableの全参照と実commandへの束縛はSeal前に検証し、宣言不能なら記録を始めない。

共通契約は、template・上位frontend・低水準backendの全てへ適用する。

- **template**: GraphicsPipelineTemplate<TContext, A, O>は同じcontextのIGraphicsPlanSourceを実装する。RenderGraphの簡易Invokeは同frameのIGraphicsStageContextだけを供給し、それ以上の能力を要求するtemplateを受け取らない。複合contextでは型付きfactoryを渡すDeclareを使う。factory/processorは公開契約であり、外部作者も同じ宣言・検証を通す。DeviceをCPU可読にせず、Deviceを含むbundleも再帰的に制限し、Graphics能力のviewから宣言を追加する。
- **効果と2Dの移行**: ImageEffectGraphは再利用可能な不変DAGとして保持するが実行器やownerにしない。ImageEffectSubgraph、EffectImage、AppendEffectsWithContext/AppendEffectWithContext、旧RenderGraphContextのGPU参照取得、PreparedDraw2D/PreparedDrawingPasses2Dの分散所有をPlanImageと共通executionへ移す。全Renderer2D constructorと旧Prepare/Recordを利用側も含めて変更し、旧overloadを残さない。
- **低水準**: IArgumentTable、IGpuRef、IShaderArguments、Graphics command APIは共通基盤と宣言済direct segmentが使用する。既存wire ABIを変えず、管理componentへ独立所有を戻さない。直接systemの既存資源もinterop使用leaseで同じledgerへ接続し、台帳外で勝手にSubmitしない。

## 検討した代替案

- 独立した実行器をadapterで接続する: 既存の実装境界を保てるが、効果実装、資源配置、table確定、完了判定の分散が残るため選ばない。
- allocatorだけ共通化する: 確保の重複を減らせるが、生存判定と登録表の世代、payload、送信証跡が別計画のままで不十分である。
- 全処理を一つのtableへ登録する: 管理は単純だが、whole-tableの物理露出、attachment競合、binding上限、不要な保持を解決できない。
- 効果へGraph用Appendとpipeline用Encodeを実装する: shaderを共有しても領域・引数・useと所有が二重になる。単一Lowerを選ぶ。
- Core hostへExecutionRuntimeとruntime capability castを必須化する: 単一hostへ配送を集められるが、CPUだけの構成にも具象依存を強制し、能力不足を実行時まで隠す。Stageの型付き要件と明示bindingを選ぶ。
- typed処理をなくしRenderGraphだけにする: resource依存は表せるが、CPU値を含む型付き部品交換と非描画処理の再利用を失う。入口を分け、実行基盤だけを統合する。
- 直接systemも全てStageへ書き換える: structured解析を統一できるが、実commandの独自記録能力と正常な外部interopを失う。有限manifestを持つ別segmentと共同hostを選ぶ。
- 全効果を自動でshader融合する: pass削減の余地はあるが、色・resolve・clip・filter順序と精度の別判断になる。本判断では明示構成を使う。

## 結果と影響

効果の実装を一つにし、各入口が同じ資源・引数・完了契約で構成できるようにする。

- **得られる効果**: 重複管理と不整合な所有境界をなくす。table数やpass数を一律に減らす保証ではない。
- **移行の負担**: 実装と利用側を一緒に変更し、custom effect、独自batch、DI登録、shader payload接続も書き換える。互換adapterや二重実行経路を維持しない。
- **依存方向**: Coreの型付き構築からGPU/Executionへの具象依存を除き、各Stageと接続assemblyが必要サービスへ依存する。共通Graphics計画の上にRenderGraph・画像効果・Drawing2Dを置く。RenderGraphとtyped Graphicsの循環を作らず、Resources連携は共通sessionへartifact leaseとpublication snapshotを供給する。
- **型の受入条件**: 小さい要求のStage・factoryを豊かなcontextへ接続でき、逆方向・能力不足・異なるpayload・要件を失ったInvokeはコンパイルエラーになることを確認する。1〜6入力とtemplateでも同じ契約を確認する。CPUのみの構成はGPU/Executionを供給せず評価できる。factory/processorの反変性とdefinition visitorの二重反変位置もC# compilerで確認し、native統合の成立とは分ける。
- **背景入力の受入条件**: parent背景copy→route read→ReplaceWithBackdropの依存とversion一致、背景欠落・foreign frame・stale versionの拒否、metadata projection後だけCPU geometryが読めること、request/背景bundleへのgeneric CPU Read拒否と全Device子の列挙を確認する。
- **2D経路の受入条件**: 三種類以上の異種commandと同型二経路を混在させ、既定と個別overrideの双方で元のpainter順を保つ。command入力型/context能力不一致のコンパイル失敗、ordinal/actual型不一致、default・foreign・stale key、別snapshot/registry/世代、重複とmissing routeの部分宣言前の拒否を確認する。templateを複数command・複数frameで再利用し、新registry/route変更でretained cacheがmissすること、backdrop mode/version不整合とunresolved出力を拒否することも検証する。
- **構造の受入条件**: Graph・typed構築・2D・単独実行が同じ効果のLower/kernel recipeを使い、効果ごとの別recordを持たない。2D→effect→composite→3D/postprocessが共通計画に見え、dead taskはallocation/table/uploadを作らない。
- **資源の受入条件**: sharingと異context/ROI、旧version保持、partial RMW、frame間import競合、range/view差を検証する。全table登録のPhysicalBindingUse、RO/RW、attachment/combined制限、binding域を跨ぐshader-data、relocation、immutable table全owner保持を含める。
- **画素の受入条件**: 二つの半coverage描画のalpha 0.75、shape/clip相関、group opacity、mask、色補間差、blur halo、backdrop置換、地図の順序・継ぎ目を独立期待値で検証する。
- **失敗の受入条件**: 部分prepare/record失敗、後続区間失敗、待機取消し、送信受理不明、device世代退役、export/cache eviction、cleanup再試行を検証する。frame.ExecuteAsyncの二度目・同時呼出し・手動Compileとの競合、失敗/取消し後の再実行拒否と正常完了後の新frame取得も検証する。
- **共同送信の受入条件**: raw scene→Graph postprocess→2D UI→raw overlayで同じ取得targetの内容を継承し、正確な全command列を一回のQueueSubmitDescで送る。別名/subresourceのRAW/WAR/WAW、incoming access、initialized/version、境界barrierと旧version保持を検証し、shared owner・予約・allocatorを二重化しない。
- **ticketの受入条件**: 実Submit前の全参加者隔離、受理不明、attach途中失敗、wrong/subset command、coverage不足、二重attach/Submit、同frameのCompile/Execute/record競合を検証する。複数bufferのreceiptを一つだけ所有し、command-first退役とcleanup再試行でもsubmissionを二重Disposeしない。
- **loopの受入条件**: 一slot満杯時もイベント/simulation/pumpが進み、仕事数が有限である。zero-size、取得全結果、resize、PresentLost、送信前後の失敗、取消し、shutdownで開始済Taskをdrainし、GPU完了とsurface releaseを別に確認する。PresentをCPU GPU待機より先に行い、影響しない世代を止めない。
- **独立exportの受入条件**: 後段epochの最終出力をPrepare前に予約し、初期preparedから独立ownerを取得できる。初期Prepareと実行claimは各一回、後段準備はその実行内で進み、GPU完了/command退役前のValueを拒否する。AcquireUse後に元exportを返しても内容を保持し、表示batchへの一回移管・incoming access/state/version一致・consumer退役後の返却とsource回収を確認する。通常ImportImageでも状態情報欠落・未知状態を拒否する。
- **境界の受入条件**: 不完全なraw closure、未知state、未保持参照、別command向けnative準備をrecord前に拒否する。directの任意GPU挙動を静的に証明したとは扱わない。複数epochの共同batchとturn寿命のBrowser表示を入場前に拒否し、offscreen複数epoch→明示export/importの別frame表示を検証する。cross-queueを同queue順序で代用しない。
- **検証の分離**: CPU構造・所有権、native統合、GPU画像、Browser/AOTを別に確認する。文書検査だけで実装成立・画素一致・速度改善を認定しない。
- **不足時の扱い**: 数値予算、対応backend、filter集合は構築時に明示する。黙った省略や別方式への切替を認めない。
