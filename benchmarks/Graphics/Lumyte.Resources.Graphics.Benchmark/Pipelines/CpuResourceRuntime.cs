namespace Lumyte.Resources.Graphics.Benchmark.Pipelines;

/// <summary>Synchronous managed benchmark executor: runs complete real product stages, not Execution endpoints.</summary>
public sealed class CpuResourceRuntime : IResourceRuntime, IResourceExecutor
{
    /// <inheritdoc/>
    public IResourceExecutor Executor => this;

    /// <inheritdoc/>
    public ValueTask BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken) => context.ExecuteAsync(cancellationToken);

    /// <inheritdoc/>
    public ValueTask ReleaseAsync(IResourceStage stage, IResourceRuntime runtime, object resource) => stage.ReleaseAsync(resource, runtime);
}
