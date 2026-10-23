# Lumyte.Resources.DependencyInjection

`AddResources(name, configure)`で名前付きscoped managerを登録します。
`UseSource<T>()`、`AddSourcePipeline<T, TStage>()`、
`AddTransformPipeline<TInput, TOutput, TStage>()`でconstructor injectionを設定します。
manager内では各出力型につき一つのPipelineを登録します。
SourceとStageはscopedで、DIが破棄し、managerからは借用します。

登録したStageは`IResourceStage`としてDIが直接生成し、Managerへ渡します。
SourcePipeline/TransformPipelineによるラップは行いません。Stageのサービス依存は
constructor injectionで解決し、Stageの破棄はDI scopeが一度だけ行います。

Sourceが`IResourceSource.Changes`から通知元を提供すると、Managerが自動的に監視を開始します。`UseSource<TSource>()`だけで監視も接続し、通知元の別登録は不要です。以下のSceneSourceはアプリ側の実装です。

```csharp
services.AddResources("scene", pipelines => pipelines
    .UseSource<SceneSource>()
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
