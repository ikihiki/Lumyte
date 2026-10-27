# Lumyte.Resources.Graphics

`GraphicsResourceRuntime`は共有Executor、device service、世代取得、旧epochの退役確認をまとめた実行環境です。
Managerが選択した環境のExecutorとDependenciesを一度収集し、Stageの構築・解放を管理します。
CPU Stageは共有の既定Runtimeを使い、GPU StageごとにGraphics Runtimeを選択します。
物理deviceの初期化・復旧はGraphics.Hostingが所有し、ホストが明示的に進めます。

```csharp
services.AddGraphicsDevice<AppDeviceFactory>(ServiceLifetime.Singleton);
services.AddResources(builder => builder
    .UseSource<AppResourceSource>()
    .AddSourcePipeline<CpuMesh, CpuMeshStage>()
    .AddTransformPipeline<CpuMesh, GpuMesh, GpuMeshStage>()
    .UseStageRuntime<GpuMesh, GraphicsResourceRuntime>());
services.AddResourceGraphics();
```

`AddResources`はDI rootに一つのunkeyed singleton Managerを登録します。
`AddResourceGraphics()`は名前なしのsingleton device serviceを借用し、同じDI rootに名前なしのsingleton Runtimeを登録します。
scoped deviceは登録時に拒否します。`AddGraphicsDevice`自体の既定はScopedなので、Resourcesとの接続ではSingletonを指定します。
deviceは`GetRequiredService<IGraphicsDeviceService>()`、Runtimeは`GetRequiredService<GraphicsResourceRuntime>()`で取得できます。
子scopeからも同じRuntimeを返し、複数Stageで共有できます。
環境のDependenciesから世代providerも収集されるため、providerの追加登録は不要です。
別deviceを使う場合は`AddGraphicsDevice<OtherFactory>("readback", ServiceLifetime.Singleton)`と`AddResourceGraphics("readback")`を追加し、対象Stageの
`UseStageRuntime<T>(provider => provider.GetResourceGraphicsRuntime("readback"))`で既存のdevice名を選びます。
名前なしと名前付きの環境は登録順によらず独立し、名前付き登録を既定環境として扱いません。
世代追跡用のIDはRuntimeが生成します。アプリが別の名前を定義する必要はありません。
既定のジョブ用ResourceRuntimeとExecutionRuntimeの登録は`AddResources`が行い、
ホストがExecutionRuntimeのPrepare・Start・Pump・Stopを管理します。

GPU Stageはconstructorにdeviceを保持せず、構築contextの選択済みRuntimeを使います。

```csharp
// BuildAsyncのGraphicsResourceRuntime runtime引数を使用する。
GraphicsDeviceLease epoch = await runtime.AcquireAsync(
    context, cancellationToken: cancellationToken);
IGraphicDevice device = epoch.Device;
// このdeviceで生成・uploadし、GPU完了後に完成した資源を返す。
// 元epochと実際のdeviceを保持する。解放時にもManagerが同じruntimeを渡す。
```

同じGraphicsResourceRuntime instanceに対応する依存Stageは、一つのジョブで構築します。CPU側の別Runtimeへ切り替わる箇所では別ジョブにし、await後も各区間のジョブへ戻ります。出力の解放は保存済みExecutorから配送し、型付きReleaseAsyncへ元Runtimeを渡します。
Stageは個別のGPU生成とsubmissionを記述します。`AcquireAsync`は毎回現在の物理epochを取得し、
Versionとservice identityを記録してleaseの所有をcontextへ移します。戻り値をStageからDisposeしません。
GPU完了前に構築を完了させず、部分生成物もcleanupへ登録します。

既定のResident寿命ではNative子資源の解放後にdevice leaseが返ります。
CPU readbackなど完成後にdeviceを必要としない出力はBuild寿命を指定できます。
Runtimeを選択しただけでResident依存になることはありません。
低水準の`UseGraphicsAsync`も選択済みGraphics Runtimeから依存IDを取得し、登録と異なるDI rootのserviceを拒否します。

ロスト後もManagerが渡す元Runtimeと、保持していたepochを使い、
`runtime.WaitForRetirementAsync(epoch.State.Epoch)`でNative解放の安全性を確認します。
解放時に新deviceや最新Runtimeを取得し直しません。部分生成物のcleanupは、出力が返る前の失敗に備えて元RuntimeとLeaseを保持します。
解放失敗時は所有を保持し、TrimまたはDisposeで再試行します。
新deviceがReadyになった後、`manager.RebuildRuntimeAsync([runtime.Id])`で新epochの生成物を構築できます。
`runtime`には対象Stageと同じ登録済み環境を使い、IDの文字列を組み立てたり保存したりしません。

DIなしでも同じRuntimeをStageの出力型へ割り当てます。

```csharp
var jobs = new ResourceRuntime();
var graphics = new GraphicsResourceRuntime(devices, jobs);
var execution = new ExecutionRuntime([jobs]);
execution.PrepareOnCurrentHost(
    OwnerPlacement.Host, CpuExecutionMode.ThreadPool, 2, new ExecutionOptions());
await execution.StartAsync();
await devices.InitializeAsync();

await using (var manager = new ResourceManager(
    "scene", source, [new CpuMeshStage(), new GpuMeshStage()], memory, jobs,
    stageRuntimes: [new(typeof(GpuMesh), graphics)]))
{
    using ResourceReference<GpuMesh> owner = await manager.RetainAsync(meshKey);
}

await execution.StopAsync();
```

Source・Stage・device service・memoryの寿命はホストが保証します。
Managerの解放が成功してからExecutionを停止し、最後にdevice serviceを終了してDI rootを閉じます。
Graphics Runtimeはbackend固有の同期制約を取り除くものではなく、StageはそのdeviceのGPU完了・Native操作の契約を守ります。

## シェーダのロード

共通形式はコンパイル済みの`.lshader`です。ResourceManagerを使わない場合も、同じartifactを直接ロードして既存のdevice APIへ渡せます。Slangのコンパイルは既存のoffline buildまたは`IShaderCompiler`が担当します。

```csharp
await using Stream input = File.OpenRead("fullscreen.lshader");
ShaderArtifact artifact = await ShaderArtifact.LoadAsync(input);
using IGraphicsShader shader = device.CreateShader(artifact);
```

Managerを使う場合は、CPU artifactとGPU shaderの依存を登録します。

```csharp
services.AddGraphicsDevice<AppDeviceFactory>(ServiceLifetime.Singleton);
services.AddResources(resources => resources.UseSource<AppSource>().AddShaderPipeline());
services.AddResourceGraphics();
```

`ShaderArtifactStage`は入力Revisionを記録し、binaryをCPU予算へ計上します。長さが分かる入力は確保前に予約し、作業bufferもBuild scopeへ計上します。長さが分からない入力は読み込み後に実サイズを予約します。`GraphicsShaderStage`は型付きRuntimeからその構築のdeviceを取得し、GPU shaderと元のepochを保持します。deviceロスト後の解放は元のepochのretirementを待ちます。CPU artifactはdevice世代に依存せず、復旧時に再利用します。

CPU artifactだけ必要なら`AddSourcePipeline<ShaderArtifact, ShaderArtifactStage>()`を登録できます。ImageEffectsやshader-data-buffer生成にも同じartifactを渡せます。ResourceManagerが所有するGPU shaderを利用側でDisposeしないでください。

## RenderGraphへのimport

RenderGraph本体はResourcesへ参照を追加しません。`RenderGraphExtensionsBuilder.AddResources(manager)`で登録してBuildAsyncで構成を確定し、Graph内の共通の`ImportResources()`からtexture、typed buffer、shader-data-bufferを取得できます。アプリ固有の出力wrapperやシェーダは`AcquireAsync<T>`で保持し、既存のgraph/device APIへ渡します。

```csharp
using var extensions = await new RenderGraphExtensionsBuilder()
    .AddResources(manager)
    .BuildAsync();
using var graph = new RenderGraph(device, extensions: extensions);
var imports = graph.ImportResources();
RenderGraphTexture texture = await imports.ImportTextureAsync(
    new ResourceKey<IGraphicsTexture>("textures/albedo"),
    TextureState.Sampled, precedingScope);
// passを追加し、読み書きとoutputを宣言する。
using CompiledRenderGraph plan = graph.Compile();
plan.Record(commands);
// commandsをsubmitし、GPU完了を待つ。その後planをDisposeする。
```

import時のstate/scopeは実際の直前状態を指定し、別deviceのallocationを混在させないでください。キーの出力型はPipeline登録と一致させます。wrapper出力なら`AcquireAsync`で取得してから、そのtexture/bufferをgraphへimportします。

importsは借用ビューで、DisposeやCompileを持ちません。compile前の放棄はGraphのDisposeでLeaseを返し、`graph.Compile()`の成功時に全拡張の所有をplanへ一括で移します。GraphをDisposeしても移譲済みの世代は解放されません。planは記録前にLeaseの失効を検証し、GPU完了またはcommand buffer破棄後に、view・一時allocation・逆登録順の外部所有権を解放します。旧世代はHotReload中も保持します。

同じGraphで別の拡張からImportResourcesを取得しても同じinstanceを返します。設定なしの`graph.ImportResources(manager)`でもGraphに一度だけ関連付け、異なるManagerの混在を拒否します。ResourceManager自体は借用し、Graphや設定からDisposeしません。拡張を利用したGraphはCompile成功後の変更・再Compileを拒否します。

複数入力の公開世代を揃える場合は、先にReferenceを取得し、最初の取得前に借用viewを選びます。

```csharp
using ResourceSnapshot snapshot = manager.CaptureSnapshot();
graph.ImportResources().UseSnapshot(snapshot);
// imports.Pin(reference)やEffectsを使ってGraphを構築する。
// Compile後はSnapshotを返してよく、発行済みLeaseはPlanの解放まで保持される。
```

`imports.Pin(reference)`やEffectsなどの拡張も同じviewを使います。明示的な`imports.Pin(snapshot, reference.Handle)`も利用でき、発行済みLeaseはsnapshotを返した後も有効です。個々のAcquireAsyncは暗黙にSnapshotを取得しません。

## ImageEffectsとの接続

ImageEffectsとの通常の接続には、任意のパッケージ[Lumyte.Resources.Graphics.ImageEffects](../Lumyte.Resources.Graphics.ImageEffects/README.md)を使用します。`AddImageEffects()`で既定の埋め込みシェーダ一式を登録し、パッケージ名やキーの指定なしで取得できます。

```csharp
// 初期化時に構成を登録し、BuildAsyncで一度ロードする。
using var extensions = await new RenderGraphExtensionsBuilder()
    .AddResources(manager)
    .AddImageEffects(manager)
    .BuildAsync(cancellationToken);

// フレームごと。
using var graph = new RenderGraph(device, extensions: extensions);
EffectImage output = graph.AppendEffects(effectGraph, source);
graph.MarkOutput(output.Texture);
using CompiledRenderGraph plan = graph.Compile();
plan.Record(commands);
// GPU完了後にplanをDispose。effectのGPU資源、shaderのLeaseの順に返す。
```

カスタムの場合だけ`UseGraphicsShaders<ImageEffectShaderResource>`でDIへ登録し、二つのartifactキーを指定します。組を一つの世代として取得するため、通常のappendでSnapshotは不要です。HotReload後に既存のpipelineをその場で差し替えず、次のplanで新しいシェーダ一式を利用します。

低水準のartifact／GPU module取得と`ImageEffectShaders`への受け渡しも利用できます。その場合は取得元の寿命をGraphへ保持させてから固定GPU資源を準備します。直接Graphに追加するEffectsと2Dの部分ビューは同じImageEffectsGraphContextを使用します。

## キー付きGraphicsリソース定義

ResourceManagerは、定義から生成・共有・再構築するGPUリソースを管理します。
型ごとのshader設定だけでなく、`ResourceKey<GraphicsResource<T>>`ごとに異なる定義を登録できます。
同じ型のpipelineを用途別に複数持てます。同じ型・キーへの登録はDIのあと勝ちです。

```csharp
var vertex = new ResourceKey<GraphicsResource<IGraphicsShader>>("app/vertex");
var fragment = new ResourceKey<GraphicsResource<IGraphicsShader>>("app/fragment");
var pipeline = new ResourceKey<GraphicsResource<IGraphicsPipeline>>("app/pipeline");
services.AddGraphicsResource(vertex, new GraphicsShaderDefinition(new ResourceKey<ShaderArtifact>("shaders/vertex.lshader")));
services.AddGraphicsResource(fragment, new GraphicsShaderDefinition(new ResourceKey<ShaderArtifact>("shaders/fragment.lshader")));
services.AddGraphicsResource(pipeline, new GraphicsPipelineDefinition
{
    VertexShader = vertex,
    FragmentShader = fragment,
    TopologyClass = PrimitiveTopologyClass.Triangle,
    AlphaToCoverageEnable = false,
    Optimization = PipelineOptimizationMode.FullSpecialization,
    Label = "app-pipeline",
});
services.AddResources(resources => resources.UseSource<AppSource>()
    .AddShaderPipeline().AddGraphicsResources());
services.AddResourceGraphics();

// device・Execution起動後。生成やロードは取得時に行う。
using var reference = await manager.RetainAsync(pipeline);
using var lease = reference.Pin();
encoder.SetPipeline(lease.Value.Value);
// GPU使用・command解放の後にleaseを返す。
```

定義のキーとshader入力のキーは別です。二つのpipelineが同じshaderキーを参照すると、GPU moduleも共有します。
shaderはpipelineのResident依存です。pipelineの解放が成功するまでmoduleと元device epochを保持します。
Depth-only用途ではFragmentShaderを省略できます。Blend・Cull・Depth/Stencil・SampleMaskは描画時、
attachment format/sample countはpassから指定し、定義へ重複させません。

`AddGraphicsResources()`はshader、graphics/compute pipeline、sampler、texture、texture viewのStageを追加します。
繰り返しや2D/Effectsからの共用登録でも既存Stageを置き換えません。
`GraphicsBufferDefinition<T>`には`AddGraphicsResourcePipeline<IGraphicsBuffer<T>>()`を追加します。
独自meshや複合GPUアセットも`GraphicsResourceDefinition<T>`と同じgeneric Stageで定義できます。
`GraphicsComputePipelineDefinition`はcompute shaderへの、`GraphicsTextureViewDefinition`はtextureへのResident依存を登録します。

`GraphicsTextureDefinition`と`GraphicsBufferDefinition<T>`は割り当てだけを復元します。
画像・mesh等の内容を復元する場合は、独自定義でCPU資源をRequireAsyncし、初期化・upload完了を待ってから返します。
更新可能な内容を割り当て定義だけで保存・復元できるとはみなしません。
途中生成物は次の失敗し得る操作より前にcontextのcleanupへ登録します。

| 管理対象 | 所有者 |
| --- | --- |
| 共有shader、pipeline、sampler、texture、buffer、view、mesh | ResourceManager。依存・再構築手順が定義されたもの |
| Renderer生成用の共有資源 | ResourceManager。汎用キーの依存を束ねる |
| Renderer実体、フレームごとの更新buffer・binding | 描画側 |
| 一時画像、Effects中間出力 | RenderGraph |
| Swapchain画像、command、描画submission・完了判断 | Graphicsシステム・利用側 |
| state/attachmentに応じたnative pipeline cache | backend |

DI定義はManagerのStage構築時に型別・キー別の辞書へ固定します。描画時にDIを参照せず、Acquire時にも定義を変更しません。
`TryAddGraphicsResource`でライブラリ既定値を登録し、アプリ定義は登録順序にかかわらず優先できます。
Runtimeは出力型ごとに選び、GPU依存が別physical deviceなら失敗します。出力型ごとの名前付きRuntime選択も従来どおり使えます。
DIなしでは`new GraphicsResourceStage<T>(registrations)`と`ResourceStageRuntime(typeof(GraphicsResource<T>), runtime)`をManagerに登録します。
各registrationは公開の`GraphicsResourceRegistration<T>(key, definition)`です。

## 2D・Effectsの既定定義

2Dは`AddGraphics2D()`、Effectsは`AddImageEffects()`が汎用の定義とStageを登録します。
`DrawingGraphicsResources2D`と`ImageEffectGraphicsResources`がshader/pipeline等の公開キーを提供します。
2Dの取得・生成は`AcquireRenderer2DAsync()`→`CreateRenderer()`、Effectsは親graphの`AddImageEffects(manager)`で接続します。
描画時にshaderやproviderを組み立てません。

```csharp
services.AddGraphicsResource(ImageEffectGraphicsResources.Pipeline, new GraphicsPipelineDefinition
{
    VertexShader = ImageEffectGraphicsResources.VertexShader,
    FragmentShader = ImageEffectGraphicsResources.FragmentShader,
    Optimization = PipelineOptimizationMode.FullSpecialization,
});
```

`UseGraphicsShaders<Renderer2DResource>(vertex, fragment)`と
`UseGraphicsShaders<ImageEffectShaderResource>(vertex, fragment)`は入力だけを変える簡略APIとして残します。
明示的に登録した同じ公開キーの定義を優先します。未設定時はDLL埋め込みartifactを使い、アプリのSourceを読みません。
2D/Effectsが要求するtriangle・fragment・single-sample互換性に反する定義は、生成用資源の取得時に拒否します。

## 公開世代・復旧と寿命

生成したrendererやgraph/planは正確な一世代の依存を保持します。HotReload後は同じReferenceから次のrendererを生成し、
Effectsは次のGraphで新しい公開世代を取得します。既存のrenderer・prepared draw・planはその場で差し替えません。
複数入力を同じpublicationへ揃える場合だけResourceSnapshotを共有します。

GPU使用終了の判断は利用側の責務です。ResourceManagerはlease返却後の解放を行い、描画のsubmitや完了判断を代行しません。
Build中の初期化uploadについては定義が完了まで所有し、未完了のGPU資源を公開しません。
loss時は旧rendererの使用を停止し、GPU退役を確認して旧prepared ownerを返します。
device復旧後に`manager.RebuildRuntimeAsync([graphicsRuntime.Id])`を呼び、同じReferenceから生成し直します。
CPU入力を再利用し、GPU出力だけを新しいepochで構築します。解放失敗時は依存先を保持し、Trim/Disposeで再試行します。
