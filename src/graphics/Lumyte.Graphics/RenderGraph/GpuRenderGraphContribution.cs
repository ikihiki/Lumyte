namespace Lumyte.Graphics.RenderGraph;

internal abstract class GpuRenderGraphContribution(string name, int order, bool enabled)
{
    public string Name { get; } = name;
    public int Order { get; } = order;
    public bool Enabled { get; } = enabled;
    public abstract void Invoke(GpuRenderGraphContributionContext context);
}
