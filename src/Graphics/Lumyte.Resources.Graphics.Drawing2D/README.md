# Lumyte.Resources.Graphics.Drawing2D

ResourceManagerから2Dレンダラーを生成するための資源を取得します。Drawing2D本体はResourcesやRenderGraphに依存しません。

```csharp
services.UseGraphics2D(); // 命令ごとのrendererとDrawingCommandRenderers2D。
services.AddResources(resources => resources.UseSource<AppSource>().AddGraphics2D());
services.AddResourceGraphics();
// memory、device service、Executionの登録と起動はアプリが行う。

using var rendererResource = await manager.AcquireRenderer2DAsync();
using var renderer = rendererResource.CreateRenderer(commandRenderers);
using var prepared = await renderer.PrepareAsync(drawList, target);
prepared.Record(commands);
// submit、GPU完了、command解放の後にpreparedを解放する。
```

`commandRenderers`はDIから解決した表です。省略時は`DrawingCommandRenderers2D.CreateDefault()`を使います。
生成用Referenceは初期化時に取得し、フレームをまたいで保持できます。各CreateRendererはその時の公開世代を固定します。
生成時はI/O・GPU生成を行いません。Renderer2Dを複数作っても同じ公開世代のpipelineとmoduleを共有します。
レンダラーと準備済み描画は独立して資源を保持するため、レンダラーやReferenceを先に返しても、準備済み描画はGPU完了まで保持できます。

## 共通シェーダー設定

設定しなければDrawing2DのDLL埋め込みartifactを使い、アプリのSourceを読みません。
変更する場合は起動時のDI設定だけを追加します。Effectの指定も同じAPIです。

```csharp
services.UseGraphicsShaders<Renderer2DResource>(new("shaders/2d-v"), new("shaders/2d-f"));
services.UseGraphicsShaders<ImageEffectShaderResource>(new("shaders/effect-v"), new("shaders/effect-f"));
services.AddResources(resources => resources.UseSource<AppSource>()
    .AddShaderPipeline().AddGraphics2D().AddImageEffects());
```

設定は型ごとに独立し、同じ型の登録はDIのあと勝ちです。互換性のあるvertex/fragment ABIでコンパイル済みの`.lshader`を指定します。
キー・program ID・providerを描画時に渡す必要はありません。Effectの適用は親graphの`AddImageEffects(manager)`と既存のAppend APIを使います。

HotReload後は同じReferenceの次のCreateRendererが新しい世代を使います。生成済みレンダラーと準備済み描画は元の世代を保ちます。
他の入力資源とpublicationを揃える場合だけ`reference.CreateRenderer(commandRenderers, frameSnapshot)`を使います。
Snapshotは生成完了後に返せます。

device lossでは古いrendererを使い続けず、GPU退役を確認してpreparedとrendererを解放します。
device復旧と`manager.RebuildRuntimeAsync([graphicsRuntime.Id])`の後に、同じReferenceでCreateRendererを再実行します。
CPU artifactを再利用し、新しいdevice上でGPU資源を作り直します。ロストした世代は描画時にも拒否します。
Manager・Graphics Runtimeはrendererとpreparedの返却およびnative cleanupが終わるまで生存させます。

## キー付き定義

DrawingGraphicsResources2DのVertexShader、FragmentShader、Pipeline、Samplerが公開キーです。
`AddGraphicsResource(key, definition)`でアプリの定義を登録すると、既定値より優先します。
例えばPipelineへGraphicsPipelineDefinitionを指定すれば、shader入力とOptimization/Label等を変更できます。
同じ型の別キーも独立して登録でき、shader moduleを他のpipelineと共有できます。
triangle・fragment・single-sampleの前提と矛盾する定義は生成用資源の取得時に拒否します。

共有module/pipeline/samplerはResourceManagerが保持し、2Dの準備batchは借用します。
フレームごとのargument table、データ、初期化を行うfallback画像は準備batchが所有します。

DIなしではshader・pipeline・samplerのGraphicsResourceRegistration<T>とGraphicsResourceStage<T>、
Renderer2DResourceStageをManagerへ登録し、各出力型へGraphicsResourceRuntimeを割り当てます。
Resourcesなしの通常描画は従来どおりRenderer2DとCreateDefaultだけで使えます。
