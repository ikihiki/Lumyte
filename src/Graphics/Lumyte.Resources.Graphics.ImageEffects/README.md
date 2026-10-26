# Lumyte.Resources.Graphics.ImageEffects

ResourceManagerが既定のImageEffectsシェーダ一式を保持し、RenderGraphへの追加時に固定GPU資源と世代の寿命をまとめて管理します。通常はパッケージ名、シェーダのキー、個別のLeaseを指定しません。RenderGraphとImageEffects本体はResourcesに依存しません。

## 既定の登録と利用

```csharp
services.AddMemoryManagement();
services.AddGraphicsDevice<AppDeviceFactory>(ServiceLifetime.Singleton);
services.AddResources(resources => resources
    .UseSource<AppSource>()
    .AddImageEffects());
services.AddResourceGraphics();

// deviceとExecutionを起動した後、構成を登録し、一度だけ準備する。
using var extensions = await new RenderGraphExtensionsBuilder()
    .AddResources(manager)
    .AddImageEffects(manager)
    .BuildAsync(cancellationToken);
```

既定のシェーダはImageEffects DLLの埋め込みartifactです。アプリのSourceに専用のファイルや名前を登録する必要はありません。CPU artifactはライブラリ共有cacheを再利用し、GPU moduleの組はManagerとdevice epochごとに共有します。

```csharp
using var graph = new RenderGraph(device, extensions: extensions);
// sourceはこのgraphへimportしたEffectImage。
EffectImage output = graph.AppendEffects(effectGraph, source);
graph.MarkOutput(output.Texture);
using var plan = graph.Compile();
plan.Record(commands);
// submitしてGPU完了を確認する。その後planをDisposeする。
```

単一effectは`graph.AppendOpacity(source, 0.5f)`や`graph.AppendEffect(new OpacityEffect(0.5f), source)`でも追加できます。返るのは画像だけで、シェーダや個別の所有者を渡す必要はありません。compile前の放棄はGraphが、compile後はplanが、Effectの固定資源を解放してからシェーダのLeaseを返します。解放失敗時は失敗した資源と依存先を保持し、同じ所有者のDisposeで再試行します。append失敗後はそのgraphを破棄してください。

BuildAsyncが返す拡張構成は変更できず、複数フレームで再利用できます。ImageEffectsGraphContextはGraphごとに一つ作り、そのGraphのシェーダ世代を最初の利用時に選びます。2DのImageEffectSubgraphからの呼び出しも同じ状態を共有します。他の拡張は同じbuilderの`Add<OtherContext>(factory)`で登録でき、各importsはGraphを借用するビューです。Compileと所有権の移譲はGraph全体で一度だけ行います。

`AddImageEffects(manager)`は準備の定義だけを登録し、ロードを開始しません。`BuildAsync`が登録順に非同期準備を実行し、完成した構成がReferenceを所有します。Graphが取得したLeaseは設定のDispose後もplanの解放まで保持されます。拡張設定にManagerやdeviceそのものの起動停止は含めません。Resourcesを使わないGraphでも、同じ`graph.AppendEffects`が既定の埋め込み版で動きます。

GPUのsubmit・完了確認と入力画像のstate/scopeは利用側が指定します。待機のキャンセルはGPU完了ではありません。未完了のplanは所有を保持し、実際の完了後に解放を再試行します。device loss時は旧epochのretirementを確認してからplanの子資源を解放し、その後Managerを再構築します。

## カスタムシェーダの差し込み

アプリのSourceで読む二つのartifactキーを、登録時に一度指定します。ResourceManagerへのシェーダ一式の取得や描画コードは既定の場合と同じです。

```csharp
services.UseGraphicsShaders<ImageEffectShaderResource>(
    new ResourceKey<ShaderArtifact>("shaders/fullscreen.lshader"),
    new ResourceKey<ShaderArtifact>("shaders/custom-effects.lshader"));
services.AddResources(resources => resources
    .UseSource<AppSource>()
    .AddSourcePipeline<ShaderArtifact, ShaderArtifactStage>()
    .AddImageEffects());
```

カスタム入力は既存ImageEffectsのvertex/fragment ABIに従うコンパイル済み`.lshader`です。既に`AddShaderPipeline()`などでartifactのPipelineを登録していれば、その登録を利用します。設定はDI rootごとに固定し、既定とカスタムを判別する文字列や特殊なパッケージ名を描画側へ渡しません。

各shaderがartifactをResident依存に持ち、共有pipelineが両shaderを、効果用の資源一式がpipelineとsamplerをResident依存に持ちます。SourceのHotReloadでは組を再構築し、次のGraphが初めてEffectsを使うときに新しい世代を取得します。同じGraph内の後続appendと実行中のplanは元の組を保持します。device復旧ではCPUの組を再利用し、新deviceへGPUの組を作ります。

appendはI/Oや全リソースSnapshotの取得を行いません。他のtextureやmeshと同じpublicationへ揃える場合だけ、最初のimportやEffects利用より前に`graph.ImportResources().UseSnapshot(frameSnapshot)`を呼びます。Snapshotは呼び出し側がGraph構築終了まで保持し、各拡張が発行したLeaseはSnapshotを返した後も有効です。共有pipeline・samplerはManagerで事前生成し、argument tableは各passで生成します。

## 汎用定義の上書きと管理境界

`ImageEffectGraphicsResources`のVertexShader、FragmentShader、Pipeline、Samplerが公開キーです。
`AddGraphicsResource(key, definition)`で同じキーへ独自定義を登録できます。既定登録はアプリの定義を置き換えません。
shader指定だけでなく、pipelineのOptimization等も`GraphicsPipelineDefinition`で設定できます。
同じnative型の別キーも独立して登録・取得できます。シェーダ指定の簡略APIはこれらの既定定義へ入力を渡します。

ResourceManagerがmodule・pipeline・samplerとそのdevice世代を共有します。EffectGpuPassはこれらを借用し、
一時画像はRenderGraph、フレームごとのargument tableとデータはgraph/planが所有します。
Resourcesを使わない場合の既定経路は従来どおりgraph/planが生成した資源を所有します。

DIなしではshader・pipeline・samplerの`GraphicsResourceRegistration<T>`を公開キーで用意し、
`GraphicsResourceStage<T>`と`ImageEffectShaderStage`をManagerへ登録します。
各GPU出力にGraphicsResourceRuntimeを割り当てます。CPU artifactは`ImageEffectShaders.Embedded`から参照できます。
Source、memory、deviceとExecutionの起動・終了はアプリが管理します。
