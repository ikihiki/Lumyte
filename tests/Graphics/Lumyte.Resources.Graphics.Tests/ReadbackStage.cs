namespace Lumyte.Resources.Graphics.Tests;

internal sealed class ReadbackStage : IResourceSourceStage<ReadbackValue, GraphicsResourceRuntime>
{
    public async ValueTask<ReadbackValue> BuildAsync(GraphicsResourceRuntime runtime, ResourceBuildContext context, CancellationToken cancellationToken)
    {
        await runtime.AcquireAsync(context, ResourceDependencyLifetime.Build, cancellationToken);
        return new(runtime);
    }

    public ValueTask ReleaseAsync(ReadbackValue resource) => ValueTask.CompletedTask;
}
