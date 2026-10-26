namespace Lumyte.Resources.Graphics.Tests;

internal sealed class CpuStage(GraphicsTestState state) : IResourceSourceStage<CpuValue>
{
    public ValueTask<CpuValue> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
    {
        state.CpuBuilds++;
        state.CpuRuntime = context.Runtime;
        return ValueTask.FromResult(new CpuValue());
    }

    public ValueTask ReleaseAsync(CpuValue resource) => ValueTask.CompletedTask;
}
