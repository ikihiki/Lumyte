namespace Lumyte.Resources.Graphics.Tests;

internal sealed class ReadbackStage(GraphicsTestState state) : IResourceSourceStage<ReadbackValue, GraphicsResourceRuntime>
{
    public async ValueTask<ReadbackValue> BuildAsync(GraphicsResourceRuntime runtime, ResourceBuildContext context, CancellationToken cancellationToken)
    {
        await runtime.AcquireAsync(context, ResourceDependencyLifetime.Build, cancellationToken);
        state.ReadbackRuntime = runtime;
        return new();
    }

    public ValueTask ReleaseAsync(ReadbackValue resource, GraphicsResourceRuntime runtime) => ValueTask.CompletedTask;
}
