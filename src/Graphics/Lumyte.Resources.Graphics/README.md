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
    using ResourceReference<GpuMesh> owner = await manager.AcquireAsync(meshKey);
}

await execution.StopAsync();
```

Source・Stage・device service・memoryの寿命はホストが保証します。
Managerの解放が成功してからExecutionを停止し、最後にdevice serviceを終了してDI rootを閉じます。
Graphics Runtimeはbackend固有の同期制約を取り除くものではなく、StageはそのdeviceのGPU完了・Native操作の契約を守ります。
