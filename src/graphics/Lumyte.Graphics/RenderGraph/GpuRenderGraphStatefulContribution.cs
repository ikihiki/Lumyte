namespace Lumyte.Graphics.RenderGraph;

internal sealed class GpuRenderGraphStatefulContribution<TState>(
    string name,
    int order,
    bool enabled,
    TState state,
    Action<GpuRenderGraphContributionContext, TState> contribute) : GpuRenderGraphContribution(name, order, enabled)
{
    public override void Invoke(GpuRenderGraphContributionContext context) => contribute(context, state);
}
