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

// deviceとExecutionを起動した後、一度取得して保持する。
using var shaders = await manager.AcquireImageEffectShadersAsync();
```

既定のシェーダはImageEffects DLLの埋め込みartifactです。アプリのSourceに専用のファイルや名前を登録する必要はありません。CPU artifactはライブラリ共有cacheを再利用し、GPU moduleの組はManagerとdevice epochごとに共有します。

```csharp
var graph = new RenderGraph(device);
using var imports = graph.ImportResources(manager);
// sourceはこのgraphへimportしたEffectImage。
EffectImage output = imports.AppendEffects(shaders, effectGraph, source);
graph.MarkOutput(output.Texture);
using var plan = imports.Compile();
plan.Record(commands);
// submitしてGPU完了を確認する。その後planをDisposeする。
```

単一effectは`imports.AppendEffect(shaders, new OpacityEffect(0.5f), source)`でも追加できます。返るのは画像だけで、PreparedImageEffectのDisposeやRetainは利用側に不要です。compile前の放棄はimportsが、compile後はplanが、Effectの固定資源を解放してからシェーダのLeaseを返します。解放失敗時は失敗した資源と依存先を保持し、同じ所有者のDisposeで再試行します。append失敗後はそのgraphを破棄してください。

GPUのsubmit・完了確認と入力画像のstate/scopeは利用側が指定します。待機のキャンセルはGPU完了ではありません。未完了のplanは所有を保持し、実際の完了後に解放を再試行します。device loss時は旧epochのretirementを確認してからplanの子資源を解放し、その後Managerを再構築します。

## カスタムシェーダの差し込み

アプリのSourceで読む二つのartifactキーを、登録時に一度指定します。ResourceManagerへのシェーダ一式の取得や描画コードは既定の場合と同じです。

```csharp
services.AddSingleton(new ImageEffectShaderPackage(
    new ResourceKey<ShaderArtifact>("shaders/fullscreen.lshader"),
    new ResourceKey<ShaderArtifact>("shaders/custom-effects.lshader")));
services.AddResources(resources => resources
    .UseSource<AppSource>()
    .AddSourcePipeline<ShaderArtifact, ShaderArtifactStage>()
    .AddImageEffects());
```

カスタム入力は既存ImageEffectsのvertex/fragment ABIに従うコンパイル済み`.lshader`です。既に`AddShaderPipeline()`などでartifactのPipelineを登録していれば、その登録を利用します。設定はDI rootごとに固定し、既定とカスタムを判別する文字列や特殊なパッケージ名を描画側へ渡しません。

二つのartifactをResident依存に持つCPUの組と、そこから作るGPUの組を一つずつ公開します。SourceのHotReloadでは組を再構築し、次のappendから新しい世代を使います。実行中のplanは元の組を保持します。device復旧ではCPUの組を再利用し、新deviceへGPUの組を作ります。

取得済みReferenceからappendする経路はI/Oや全リソースSnapshotの取得を行いません。他のtextureやmeshと同じpublicationへ揃える場合だけ、`imports.AppendEffects(shaders, effectGraph, source, frameSnapshot)`へフレーム共通のSnapshotを渡せます。固定pipeline・sampler・tableの生成は既存ImageEffectsの各passで行います。

## DIを使わない場合

Managerのstagesに`new ImageEffectArtifactStage()`と`new ImageEffectShaderStage()`を加え、`stageRuntimes`に`new ResourceStageRuntime(typeof(ImageEffectShaderResource), graphicsRuntime)`を指定します。カスタム時は`new ImageEffectArtifactStage(package)`へ同じ設定を渡し、artifactを作るStageも登録します。Source、memory、deviceとExecutionの起動・終了はアプリが管理します。
