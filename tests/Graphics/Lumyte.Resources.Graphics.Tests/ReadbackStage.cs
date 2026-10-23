namespace Lumyte.Resources.Graphics.Tests;

internal sealed class ReadbackStage : IResourceSourceStage<ReadbackValue>
{
    public async ValueTask<ReadbackValue> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
    {
        var runtime = (GraphicsResourceRuntime)context.Runtime;
        await runtime.AcquireAsync(context, ResourceDependencyLifetime.Build, cancellationToken);
        return new(runtime);
    }

    public ValueTask ReleaseAsync(ReadbackValue resource) => ValueTask.CompletedTask;
}
