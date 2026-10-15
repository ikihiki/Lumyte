# Lumyte.Resources.Graphics

Resourcesのruntime依存を、Graphics.Hostingの名前付きdevice serviceへ接続します。deviceはアプリが明示的に初期化・復旧します。

```csharp
services.AddGraphicsDevice<AppDeviceFactory>("main");
services.AddResources("scene", builder => builder
    .UseSource<AppResourceSource>()
    .AddSourcePipeline<CpuMesh, CpuMeshStage>()
    .AddTransformPipeline<CpuMesh, GpuMesh, GpuMeshStage>());
services.AddResourceGraphics("scene", "main", "graphics.main");
```

GPU stageはconstructor injectionで `[FromKeyedServices("main")] IGraphicsDeviceService` を受け取り、構築中に次を呼びます。

```csharp
GraphicsDeviceLease epoch = await context.UseGraphicsAsync(
    devices, "graphics.main", cancellationToken: cancellationToken);
IGraphicDevice device = epoch.Device;
// deviceで生成・uploadし、GPU完了後に完成した資源を返す。
```

返されるleaseはcontextが所有します。stageからDisposeしません。既定のResident依存では、stageのReleaseAsyncがNative子資源を解放した後にleaseが返されます。生成途中に例外が発生した場合、stageは部分生成物を片付けてから例外を返す必要があります。生成時に捕捉したdeviceとdispatcherを解放にも使います。

bridgeは同じDI scopeのservice instanceを識別します。同名でも別scopeのserviceをUseGraphicsAsyncへ渡すことはできません。deviceのloss通知により旧Ready Versionは失効し、公開直前にlossした構築結果も公開されません。CPU readbackなど、構築完了後にdeviceを必要としない出力はBuild寿命を指定できます。

ロスト後はアプリがGPU使用を停止し、旧epochを保持して `devices.WaitForRetirementAsync(epoch.State.Epoch)` の完了を確認します。stageがNative子資源を解放できる状態になってからmanagerのTrimを実行し、続いて `devices.RetireAsync()` で旧ownerを解放します。stageのReleaseAsyncが失敗した場合、managerはleaseを保持して解放を再試行します。新deviceがReadyになった後、managerの `RebuildRuntimeAsync(["graphics.main"])` で新epochの生成物を構築します。

bridgeは実行スレッドの配送、GPU submission、完了待ち、Native退役を暗黙に行いません。これらはbackendの制約を知るstageとアプリが担当します。
