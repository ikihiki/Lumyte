# Lumyte.Resources.DependencyInjection

`AddResources(name, configure)`で名前付きscoped managerを登録します。
`UseSource<T>()`、`AddSourcePipeline<T, TStage>()`、
`AddTransformPipeline<TInput, TOutput, TStage>()`でconstructor injectionを設定します。
manager内では各出力型につき一つのStageを登録します。
SourceとStageはscopedでDIが破棄し、managerは借用します。Pipelineによるラップは行いません。

## 登録と起動

`AddResources`は既定の`ResourceRuntime`、`IResourceRuntime`と`ExecutionRuntime`をsingleton登録し、
同じResourceRuntimeを`IExecutionModule`として接続します。既存の登録は上書きしません。
複数の名前付きmanagerは同じ既定ジョブ環境を借り、独立したDI root間ではportを共有しません。
既定の環境を使うStageへのジョブ注入やRuntimeの手動登録は不要です。
ResourceRuntimeのmodule登録はAddResourcesに任せ、同じinstanceを別途IExecutionModuleへ重複登録しないでください。

次はFileResourceSourceで読み込みと変更監視を接続する例です。Stageと出力型はアプリ側の実装です。

```csharp
services.AddSingleton<IMemoryManager>(memory);
services.AddSingleton(new FileResourceSourceOptions
{
    RootPath = Path.GetFullPath("assets"),
});
services.AddResources("assets", resources => resources
    .UseSource<FileResourceSource>()
    .ConfigureHotReload(new ResourceReloadOptions
    {
        DebounceDelay = TimeSpan.FromMilliseconds(100),
        MaximumBatchDelay = TimeSpan.FromSeconds(1),
    })
    .AddSourcePipeline<Document, DocumentStage>()
    .AddTransformPipeline<Document, Mesh, MeshStage>());

await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions
{
    ValidateScopes = true,
    ValidateOnBuild = true,
});
ExecutionRuntime execution = provider.GetRequiredService<ExecutionRuntime>();
execution.PrepareOnCurrentHost(
    OwnerPlacement.Host, CpuExecutionMode.ThreadPool, 2, new ExecutionOptions());
await execution.StartAsync();

AsyncServiceScope scope = provider.CreateAsyncScope();
ResourceManager manager = scope.ServiceProvider.GetRequiredKeyedService<ResourceManager>("assets");
manager.HotReload!.ReloadFailed += (_, failure) => Console.Error.WriteLine(failure.Error);
{
    using ResourceReference<Mesh> mesh = await manager.AcquireAsync(meshKey);
    using ResourceLease<Mesh> lease = mesh.Acquire();
    Consume(lease.Value);
}

await manager.DisposeAsync(); // 失敗時は同じmanagerで再試行し、成功してからscopeを閉じる。
await scope.DisposeAsync();
await execution.StopAsync();
```

DI解決だけではスレッドを起動しません。Prepare/Start/Pump/Stopはホストが管理します。
BrowserではHost/Cooperativeに配置し、ロード・解放を待つ間もホストからPumpを続けます。
例は解放が成功する経路です。容量不足やbackend解放失敗時は、manager・scope・各Runtime・ExecutionRuntimeを保持してください。
原因が解消したら同じmanagerのDisposeAsyncを再試行し、成功後にscopeを破棄してExecutionRuntimeを停止します。
DI scope自体のDisposeAsyncを再試行しても、途中の解放が再開される保証はありません。

## 登録の制約

Pipelineは出力型のRuntimeTypeHandleで登録し、transformの入力も型本体から解決します。
構成はcallback終了時に固定し、同名manager・runtime依存、出力型の重複、重複Sourceを拒否します。
同じ型の異なる要素・変換条件はResourceAddressのSubresource・Variantで指定します。
SourceとStageはscopedに限定し、DIが一度だけ破棄します。DIの生成処理で非同期初期化やI/Oを同期待機しません。
IMemoryManagerは事前にAddMemoryManagementなどで登録するか、例のように既存instanceを渡してください。

## 自動変更監視

Sourceが`IResourceSource.Changes`から通知元を提供すると、Managerが自動的に監視を開始します。
`UseSource<FileResourceSource>()`だけで読み込みと同じrootの監視を接続し、通知元の別登録は不要です。
通知を提供しないSourceでは監視を開始しません。
通知元はSourceが所有し、Managerは購読を所有します。
`ConfigureHotReload`は集約・再試行を設定し、`Enabled = false`で自動監視を無効にできます。
`manager.HotReload.Completion`で監視元の異常終了を確認できます。

Managerの終了は監視と進行中の自動再構築をキャンセルしてjoinし、リソースを解放します。
利用中のReference・Lease・Snapshot・stream sessionを返してから終了してください。
HotReload専用のStart／StopAsyncは不要ですが、ExecutionRuntimeの起動・停止は上記の順序で行います。

## StageごとのRuntimeと世代

Audio・GraphicsなどのRuntimeはIResourceRuntimeを実装し、共通ジョブ環境とdevice・owner・世代の借用をまとめます。
UseStageRuntimeは登録済みサービスをManager構築時に一度解決し、出力型へ対応付けます。
サービス自体の登録・寿命は呼出側が決め、未指定の出力は既定Runtimeを使います。
次のMeshRuntimeはResourceRuntimeとdevice serviceをconstructorで受け取るアプリ定義の例です。

```csharp
services.AddSingleton<MeshRuntime>();
services.AddResources("meshes", resources => resources
    .UseSource<MySource>()
    .AddSourcePipeline<Document, DocumentStage>()
    .AddTransformPipeline<Document, Mesh, MeshStage>()
    .UseStageRuntime<Mesh, MeshRuntime>());
```

既存サービスから選ぶ場合は`.UseStageRuntime<Mesh>(provider => provider.GetRequiredService<MeshRuntime>())`も使えます。
device serviceと必要なIExecutionModuleも起動前に登録してください。
Stageはcontext.Runtimeから選択された安定した環境を取得し、構築ごとに現在のdevice世代を借ります。
解放は内容世代に保存したRuntimeと生成時のLease・ownerを使います。

選択RuntimeがIResourceRuntimeDependencyを実装していれば、同じinstanceを世代providerとして自動収集します。
同じprovider instanceは重複登録せず、同じIdを持つ別instanceは拒否します。
実際に借りたVersionのTrackRuntimeDependencyによる記録はStage側で行います。
追加のproviderはUseRuntimeDependencyまたはAddResourceRuntimeDependencyでも登録できます。登録名とproviderのIdは一致する必要があります。

[既定Runtimeの実行契約](../Lumyte.Resources.Execution/README.md)と
[Runtime設計](../../../docs/adr/resources/RESOURCES-0003-runtime-execution.md)を参照してください。
