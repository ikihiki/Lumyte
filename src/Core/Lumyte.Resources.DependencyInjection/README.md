# Lumyte.Resources.DependencyInjection

`AddResources`で名前付きのscopedな`ResourceManager`と、constructor injectionで構成する取得元・ステージを登録します。`IMemoryManager`は事前に`AddMemoryManagement`などで登録してください。

```csharp
services.AddResources("scene", pipelines => pipelines
    .UseSource<SceneSource>()
    .AddSourcePipeline<SceneDocument, DocumentStage>()
    .AddTransformPipeline<SceneDocument, CpuMesh, MeshStage>()
    .AddTransformPipeline<CpuMesh, GpuMesh, UploadStage>());

await using var scope = provider.CreateAsyncScope();
var resources = scope.ServiceProvider.GetRequiredKeyedService<ResourceManager>("scene");
```

例の型はアプリ側の実装です。Pipelineは出力型の`RuntimeTypeHandle`で登録し、transformの入力型も型本体から解決します。同じStage型が複数の出力型を実装する場合も、各manager名・出力型のkeyで別instanceになります。SourceとStageはDIが所有し、managerから解放しません。実行時の値・lease・sessionを先に終了し、managerの終了に成功してからDI scopeを閉じてください。

構成はcallback終了時に固定します。同名manager・runtime依存、同一manager内の出力型の重複、重複Sourceを拒否します。同じ型の異なる要素・変換条件はResourceAddressのSubresource・Variantで指定します。SourceとStageはscopedに限定し、singletonによるscoped依存の捕捉と、transientの所有責任の曖昧さを避けます。`ValidateScopes`と`ValidateOnBuild`を有効にしたproviderを推奨します。DIの生成処理で非同期初期化やI/Oを同期待機しません。

`UseRuntimeDependency<T>`でconstructor injectionによるruntime providerを登録できます。Graphicsなどの連携パッケージは`AddResourceRuntimeDependency`でscopedなadapterを追加できます。登録した名前と実際のproviderの`Id`は一致する必要があります。

自動変更監視は`WatchChanges<TChangeSource>()`で追加します。ファイル取得元と監視元には共通の`FileResourceSourceOptions`をDIで登録します。

```csharp
services.AddResources("scene", pipelines => pipelines
    .UseSource<FileResourceSource>()
    .WatchChanges<FileResourceChangeSource>(new ResourceReloadOptions
    {
        DebounceDelay = TimeSpan.FromMilliseconds(100),
        MaximumBatchDelay = TimeSpan.FromSeconds(1),
    })
    .AddSourcePipeline<SceneDocument, DocumentStage>());

await using var scope = provider.CreateAsyncScope();
var changes = scope.ServiceProvider.GetRequiredKeyedService<ResourceReloadService>("scene");
changes.Start();
// ファイル更新は自動的に再読み込みされる。
// scope終了前に、利用中のresource reference・lease・read scope・sessionを返す。
```

監視元はmanagerごとにscoped登録され、DIが一度だけ所有・解放します。登録や解決だけでは監視を開始しません。`Start()`後は監視サービスが通知集約と再試行を行い、scopeの終了時には監視・進行中の再構築を終了してからmanagerを破棄します。managerを先に明示的に閉じる場合は`changes.StopAsync()`をawaitしてください。`Completion`で監視元の異常終了を確認できます。
