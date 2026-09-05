namespace Lumyte.Graphics.RenderGraph;

/// <summary>A typed render-graph texture handle paired with its immutable description.</summary>
public readonly record struct GpuRenderGraphTexture
{
    internal GpuRenderGraphTexture(
        GpuRenderGraphResource resource,
        GpuTextureDescription description)
    {
        Resource = resource;
        Description = description;
    }

    internal GpuRenderGraphResource Resource { get; }
    public GpuTextureDescription Description { get; }
    public bool IsNull => Resource.IsNull;
}
