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

Sourceが`IResourceSource.Changes`から通知元を提供すると、Managerが自動的に監視を開始します。`UseSource<FileResourceSource>()`で読み込みと監視が接続され、監視元の別登録は不要です。通知を提供しないSourceではworkerを作りません。

```csharp
services.AddResources("scene", pipelines => pipelines
    .UseSource<FileResourceSource>()
    .ConfigureHotReload(new ResourceReloadOptions
    {
        DebounceDelay = TimeSpan.FromMilliseconds(100),
        MaximumBatchDelay = TimeSpan.FromSeconds(1),
    })
    .AddSourcePipeline<SceneDocument, DocumentStage>());

await using var scope = provider.CreateAsyncScope();
var manager = scope.ServiceProvider.GetRequiredKeyedService<ResourceManager>("scene");
manager.HotReload!.ReloadFailed += (_, failure) => Console.Error.WriteLine(failure.Error);
// ファイル更新は自動的に再読み込みされる。
// scope終了前に、利用中のresource reference・lease・resource snapshot・sessionを返す。
```

通知元はStreamを提供するSourceが所有し、Managerはその購読を所有します。終了時は監視・進行中の自動再構築をキャンセルしてjoinしてからリソースを解放します。DI scopeはManager終了後にSourceを破棄します。明示的な`Start`／`StopAsync`は不要です。`manager.HotReload.Completion`で監視元の異常終了を確認できます。`ConfigureHotReload`は集約・再試行の設定のみを受け取り、`Enabled = false`で自動監視を無効にできます。

登録したStageは`IResourceStage`としてDIが直接生成し、Managerへ渡します。
SourcePipeline/TransformPipelineによるラップは行いません。Stageのサービス依存は
constructor injectionで解決し、Stageの破棄はDI scopeが一度だけ行います。
