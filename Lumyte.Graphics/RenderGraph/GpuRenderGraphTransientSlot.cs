namespace Lumyte.Graphics.RenderGraph;

internal sealed class GpuRenderGraphTransientSlot(int index, GpuRenderGraphResourceInfo resource)
{
    public int Index { get; } = index;
    public GpuRenderGraphResourceKind Kind { get; } = resource.Kind;
    public GpuMemoryKind MemoryKind { get; } = resource.MemoryKind;
    public GpuTextureDescription? TextureDescription { get; } = resource.TextureDescription;
    public GpuBufferDescription? BufferDescription { get; } = resource.BufferDescription;
    public List<GpuRenderGraphResource> Resources { get; } = [];
    public List<GpuTransientLifetime> Lifetimes { get; } = [];

    public bool IsCompatible(GpuRenderGraphResourceInfo candidate)
        => Kind == candidate.Kind
            && MemoryKind == candidate.MemoryKind
            && TextureDescription == candidate.TextureDescription
            && BufferDescription == candidate.BufferDescription;
}
