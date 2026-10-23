# Lumyte.Resources.DependencyInjection

`AddResources(name, configure)`で名前付きscoped managerを登録します。
`UseSource<T>()`、`AddSourcePipeline<T, TStage>()`、
`AddTransformPipeline<TInput, TOutput, TStage>()`でconstructor injectionを設定します。
manager内では各出力型につき一つのPipelineを登録します。
SourceとStageはscopedで、DIが破棄し、managerからは借用します。

登録したStageは`IResourceStage`としてDIが直接生成し、Managerへ渡します。
SourcePipeline/TransformPipelineによるラップは行いません。Stageのサービス依存は
constructor injectionで解決し、Stageの破棄はDI scopeが一度だけ行います。
