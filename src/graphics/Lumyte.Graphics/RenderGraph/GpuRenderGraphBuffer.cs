namespace Lumyte.Graphics.RenderGraph;

/// <summary>A typed render-graph buffer handle paired with its immutable description.</summary>
public readonly record struct GpuRenderGraphBuffer
{
    internal GpuRenderGraphBuffer(
        GpuRenderGraphResource resource,
        GpuBufferDescription description)
    {
        Resource = resource;
        Description = description;
    }

    internal GpuRenderGraphResource Resource { get; }
    public GpuBufferDescription Description { get; }
    public bool IsNull => Resource.IsNull;
}
