namespace Lumyte.Graphics.RenderGraph;

/// <summary>
/// A virtual render-graph resource used only to express ordering between passes.
/// It has no GPU allocation and produces no resource barrier.
/// </summary>
public readonly record struct GpuRenderGraphDependency
{
    internal GpuRenderGraphDependency(GpuRenderGraphResource resource) => Resource = resource;

    internal GpuRenderGraphResource Resource { get; }
    public bool IsNull => Resource.IsNull;
}
