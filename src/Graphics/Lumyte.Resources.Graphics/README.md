# Lumyte.Resources.Graphics

`GraphicsResourceRuntime`はStageのジョブ実行、名前付きdevice service、世代取得、旧epochの退役確認をまとめた実行環境です。
CPU Stageは共有の既定Runtimeを使い、GPU StageごとにGraphics Runtimeを選択します。
物理deviceの初期化・復旧はGraphics.Hostingが所有し、ホストが明示的に進めます。

```csharp
services.AddGraphicsDevice<AppDeviceFactory>("main");
services.AddResources("scene", builder => builder
    .UseSource<AppResourceSource>()
    .AddSourcePipeline<CpuMesh, CpuMeshStage>()
    .AddTransformPipeline<CpuMesh, GpuMesh, GpuMeshStage>()
    .UseStageRuntime<GpuMesh>(provider =>
        provider.GetResourceGraphicsRuntime("scene", "graphics.main")));
services.AddResourceGraphics("scene", "main", "graphics.main");
```

`AddResourceGraphics`はManager内のIDで一つのscoped Runtimeを登録します。
`GetResourceGraphicsRuntime`は同じscopeの同じRuntimeを返し、複数Stageで共有できます。
別deviceを使うStageには別のIDのRuntimeを割り当てます。既定RuntimeとExecutionRuntimeの登録は`AddResources`が行い、
ホストがExecutionRuntimeのPrepare・Start・Pump・Stopを管理します。

GPU Stageはconstructorにdeviceを保持せず、構築contextの選択済みRuntimeを使います。

```csharp
// BuildAsyncのGraphicsResourceRuntime runtime引数を使用する。
GraphicsDeviceLease epoch = await runtime.AcquireAsync(
    context, cancellationToken: cancellationToken);
IGraphicDevice device = epoch.Device;
// このdeviceで生成・uploadし、GPU完了後に完成した資源を返す。
// 解放で必要なruntime、epoch、実際のdeviceを出力やcleanupに保持する。
```

同じGraphicsResourceRuntime instanceに対応する依存Stageは、一つのジョブで構築します。CPU側の別Runtimeへ切り替わる箇所では別ジョブにし、await後も各区間のジョブへ戻ります。出力の解放は元Runtimeから配送します。
Stageは個別のGPU生成とsubmissionを記述します。`AcquireAsync`は毎回現在の物理epochを取得し、
Versionとservice identityを記録してleaseの所有をcontextへ移します。戻り値をStageからDisposeしません。
GPU完了前に構築を完了させず、部分生成物もcleanupへ登録します。

既定のResident寿命ではNative子資源の解放後にdevice leaseが返ります。
CPU readbackなど完成後にdeviceを必要としない出力はBuild寿命を指定できます。
Runtimeを選択しただけでResident依存になることはありません。
低水準の`UseGraphicsAsync`も同じ世代検証を行い、登録と異なるscopeの同名serviceを拒否します。

ロスト後も出力やcleanupが保持した元のRuntimeとepochを使い、
`runtime.WaitForRetirementAsync(epoch.State.Epoch)`でNative解放の安全性を確認します。
解放時に新deviceや最新Runtimeを取得し直しません。Managerも生成に使ったRuntimeを保持し、同じRuntimeへ解放を依頼します。
解放失敗時は所有を保持し、TrimまたはDisposeで再試行します。
新deviceがReadyになった後、`RebuildRuntimeAsync(["graphics.main"])`で新epochの生成物を構築できます。

DIなしでも同じRuntimeをStageの出力型へ割り当てます。

```csharp
var jobs = new ResourceRuntime();
var graphics = new GraphicsResourceRuntime("graphics.main", devices, jobs);
var execution = new ExecutionRuntime([jobs]);
execution.PrepareOnCurrentHost(
    OwnerPlacement.Host, CpuExecutionMode.ThreadPool, 2, new ExecutionOptions());
await execution.StartAsync();
await devices.InitializeAsync();

await using (var manager = new ResourceManager(
    "scene", source, [new CpuMeshStage(), new GpuMeshStage()], memory, jobs,
    stageRuntimes: [new(typeof(GpuMesh), graphics)]))
{
    using ResourceReference<GpuMesh> owner = await manager.AcquireAsync(meshKey);
}

await execution.StopAsync();
```

Source・Stage・device service・memoryの寿命はホストが保証します。
Managerの解放が成功してからExecutionを停止し、最後にdevice serviceのscopeを閉じます。
Graphics Runtimeはbackend固有の同期制約を取り除くものではなく、StageはそのdeviceのGPU完了・Native操作の契約を守ります。
