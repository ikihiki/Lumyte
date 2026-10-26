# Lumyte.Resources.Graphics.Drawing

変換済みの `ModelMeshData` を、汎用 Graphics リソース定義から GPU mesh へ構築します。
インポーターや独自の HotReload manager は持ちません。Drawing 本体に Resources／DI の依存を加えません。

## 登録と取得

```csharp
using Lumyte.Graphics.Drawing;
using Lumyte.Resources;
using Lumyte.Resources.Graphics;
using Lumyte.Resources.Graphics.Drawing;

var cpuKey = new ResourceKey<ModelMeshData>("scene.mesh");
var gpuKey = new ResourceKey<GraphicsResource<ModelMesh>>("scene.mesh");
services.AddMemoryManagement();
services.AddGraphicsDevice<AppDeviceFactory>(ServiceLifetime.Singleton);
services.AddGraphicsResource(gpuKey, new ModelMeshDefinition(cpuKey));
services.AddResources(builder => builder.UseSource<AppSource>()
    .AddSourcePipeline<ModelMeshData, AppMeshStage>()
    .AddGraphicsResourcePipeline<ModelMesh>());
services.AddResourceGraphics();

// device と Execution の起動後。取得時に upload が完了する。
using ResourceReference<GraphicsResource<ModelMesh>> reference = await manager.AcquireAsync(gpuKey);
using var graph = new RenderGraph(device);
ModelMesh mesh = graph.ImportResources(manager).Acquire(reference).Value;
ModelMeshGraphResources buffers = mesh.Import(graph,
    new(PipelineStage.VertexShader, ResourceAccess.ShaderRead),
    new(PipelineStage.IndexInput, ResourceAccess.IndexRead));
// buffers.Use(modelPass) と texture／attachment の use を宣言し、graph を Compile／Record／Submit。
// GPU 完了後に plan を解放する。借用した mesh を直接 Dispose しない。
```

`AppSource`／`AppMeshStage` はアプリが用意します。CPU stage は Source を context 経由で観測し、
64-byte の interleaved vertices と ushort または uint の triangle-list indices を返します。
`ModelMeshData` は backing memory をコピーせず借用するため、CPU 出力の寿命まで所有してください。
GPU 定義はこの CPU 値を Build 依存で保持し、staging に一括コピーします。

専用の事前登録メソッドは不要です。`AddGraphicsResource`／`AddGraphicsResourcePipeline<ModelMesh>` は
最新の汎用 API をそのまま使います。GPU 生成・map・upload は選択済み GraphicsResourceRuntime の仕事として実行します。
公開前に submission の実完了を待ち、初期 staging を解放します。部分生成物は次の失敗し得る操作より前に context の cleanup へ移します。
submit 後のキャンセルでも完了または元 epoch の退役まで所有を維持し、cleanup 失敗は同じ所有者から再試行できます。

## シェーダー、HotReload、復旧

既定の ABI は `DrawingShaders.LoadModelVertex()` の共有埋め込み artifact です。
カスタム shader の変更も mesh の再構築依存にする場合は、`new ModelMeshDefinition(cpuKey, vertexArtifactKey)` として
`ResourceKey<ShaderArtifact>` を渡します。変更後も固定頂点 ABI の一致が必要です。
shader module／pipeline／sampler は既存の `GraphicsShaderDefinition`／`GraphicsPipelineDefinition`／
`GraphicsSamplerDefinition` で個別に登録・共有できます。

Source の HotReload が CPU stream を更新すると、GPU mesh も新世代へ構築されます。
reference は論理キーを維持し、次の graph が新世代を取得します。既存 graph／plan は旧 mesh を保持します。
複数材質・texture・mesh を同じ publication に揃える場合は、取得前に `UseSnapshot(frameSnapshot)` を指定します。

device loss 時は描画を停止して旧 epoch の退役を確認し、旧 plan を返します。
Hosting で device を復旧した後、`manager.RebuildRuntimeAsync([graphicsRuntime.Id])` で GPU 出力を再構築します。
CPU stream を再利用でき、旧 native mesh は元の epoch で解放されます。
GPU 使用終了の判断、Execution／device／Manager の起動停止は利用側が管理します。
