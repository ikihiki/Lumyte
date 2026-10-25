# Lumyte.Resources.DependencyInjection

`AddResources(configure)`でDI rootにつき一つのResourceManagerを登録します。
Audio・GraphicsなどのPipelineを同じbuilderへ追加し、`GetRequiredService<ResourceManager>()`から取得します。
Manager、Source、StageとDI生成の世代providerはsingletonです。子scopeからも同じManagerを使い、scopeの終了では資源を破棄しません。
ManagerはSource・Stage・Runtimeを借用し、各サービスの所有者はDI rootまたは既存instanceを渡した呼出側です。

## 登録と利用

`UseSource<T>()`、`AddSourcePipeline<T, TStage>()`、`AddTransformPipeline<TInput, TOutput, TStage>()`で
constructor injectionを設定します。出力型ごとに一つのStageを登録します。
次のStage・Source・出力型はアプリ側の実装です。

次のSceneSourceは、Streamと`IResourceSource.Changes`を提供するアプリ側のSourceです。

```csharp
services.AddMemoryManagement();
services.AddResources(resources => resources
    .UseSource<SceneSource>()
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

// ExecutionRuntimeと必要なdevice systemは利用前に起動しておく。
ResourceManager manager = provider.GetRequiredService<ResourceManager>();
manager.HotReload!.ReloadFailed += (_, failure) => Console.Error.WriteLine(failure.Error);
{
    using ResourceReference<Mesh> mesh = await manager.AcquireAsync(meshKey);
    using ResourceLease<Mesh> lease = mesh.Acquire();
    Consume(lease.Value);
}

await manager.DisposeAsync(); // 失敗時は環境を維持して同じManagerで再試行する。
// 解放成功後に各systemを停止し、最後にDI rootを破棄する。
```

`AddResources`は既定の`ResourceRuntime`、`IResourceRuntime`と`ExecutionRuntime`をsingleton登録し、
同じResourceRuntimeを`IExecutionModule`として接続します。既存の登録は上書きしません。
既定環境を使うStageへのジョブ注入やRuntimeの手動登録は不要です。
ResourceRuntimeのmodule登録を別途重複させないでください。独立したDI root間ではManagerと既定Runtimeを共有しません。

登録やDI解決はスレッド・deviceを起動しません。各systemの起動・停止と必要なPumpはアプリ側で行います。
BrowserではHost/Cooperativeを選び、ロード・解放を待つ間もPumpを続けます。
利用権を返し、Managerの解放が成功してから実行器・device system・DI rootを閉じます。
cleanup失敗時はManagerと環境を保持してDisposeAsyncを再試行します。DI root自体の破棄を先に始めないでください。

## 登録の制約

`AddResources`の重複呼び出し、Sourceや出力型の重複を拒否します。構成はcallback終了時に固定します。
各moduleのPipelineは一つのcallbackへまとめ、入力元が複数なら一つの合成Sourceで振り分けます。
Source・Stage・Runtimeが要求するサービスもManagerの利用期間全体を満たす寿命で登録します。
`UseSource<T>`は事前登録済みのTを借用でき、未登録ならsingletonとして追加します。
scopedなSourceやRuntimeをsingleton Managerへ渡す構成は`ValidateScopes`で拒否されます。

## 自動変更監視

Sourceが`IResourceSource.Changes`から通知元を提供すると、Managerが自動的に監視を開始します。
`UseSource<TSource>()`だけで監視も接続し、通知元の別登録は不要です。
通知元はSourceが所有し、Managerは購読を所有します。
`ConfigureHotReload`は集約・再試行を設定し、`Enabled = false`で自動監視を無効にできます。
`manager.HotReload.Completion`で監視元の異常終了を確認できます。

Managerの終了は監視と進行中の自動再構築をキャンセルしてjoinし、リソースを解放します。
利用中のReference・Lease・Snapshotを返してから終了してください。
HotReload専用のStart／StopAsyncは不要ですが、ExecutionRuntimeの起動・停止は上記の順序で行います。

## StageごとのRuntimeと世代

Audio・GraphicsなどのRuntimeは共通ジョブ環境とdevice・owner・世代の借用をまとめます。
`UseStageRuntime`は登録済みサービスをManager構築時に一度解決し、出力型へ対応付けます。
未指定の出力は既定Runtimeを使います。次のMeshRuntimeはアプリ定義の環境です。

```csharp
services.AddSingleton<MeshRuntime>();
services.AddResources(resources => resources
    .UseSource<MySource>()
    .AddSourcePipeline<Document, DocumentStage>()
    .AddTransformPipeline<Document, Mesh, MeshStage>()
    .UseStageRuntime<Mesh, MeshRuntime>());
```

既存環境を選ぶ`.UseStageRuntime<Mesh>(provider => provider.GetRequiredService<MeshRuntime>())`も使用できます。
Stageは`IResourceTransform<TInput, TOutput, TRuntime>`などを実装し、BuildとReleaseの型付き引数で環境を受け取ります。
宣言型に適合しない登録はManager構築時に拒否します。各構築で現在のdevice世代を取得し、解放時は生成元のLease・ownerを使います。
同じRuntime instanceを使う依存Stageは同じ構築ジョブを共有します。

選択Runtimeの`Dependencies`から世代providerを収集します。同じinstanceは重複除去し、同じIdの別instanceは拒否します。
実際に借りたVersionは`TrackRuntimeDependency`で記録します。
追加providerはbuilderの`UseRuntimeDependency<T>(id)`または`services.AddResourceRuntimeDependency<T>(id, factory)`でも登録できます。
登録したIdとproviderのIdは一致させてください。

## Native AOT

constructor injectionで生成するSource・Stage・providerの型引数には、公開constructorを保持する注釈を付けています。
factory登録や既存サービスを選択する`UseStageRuntime`には、不要な保持指定を追加しません。
CIでは`Lumyte.Resources.AotSmoke`をNative AOT公開し、DIだけで構築する実装型と型付きRuntimeを
ThreadPool／Hostの両方で実行して、生成・未完了await・解放まで検証します。

[既定Runtime](../Lumyte.Resources.Execution/README.md)と[Runtime設計](../../../docs/adr/resources/RESOURCES-0003-runtime-execution.md)も参照してください。
