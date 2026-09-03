namespace Lumyte.Graphics.RenderGraph;

/// <summary>
/// A conservative group of compatible transient resources whose lifetimes do not overlap.
/// Resources are compatible only when their kind, memory kind, and complete description match.
/// </summary>
public sealed class GpuRenderGraphTransientSlotPlan
{
    internal GpuRenderGraphTransientSlotPlan(
        int slot,
        GpuRenderGraphResourceKind kind,
        GpuMemoryKind memoryKind,
        GpuTextureDescription? textureDescription,
        GpuBufferDescription? bufferDescription,
        GpuRenderGraphResource[] resources)
    {
        Slot = slot;
        Kind = kind;
        MemoryKind = memoryKind;
        TextureDescription = textureDescription;
        BufferDescription = bufferDescription;
        Resources = Array.AsReadOnly(resources);
    }

    public int Slot { get; }
    public GpuRenderGraphResourceKind Kind { get; }
    public GpuMemoryKind MemoryKind { get; }
    public GpuTextureDescription? TextureDescription { get; }
    public GpuBufferDescription? BufferDescription { get; }
    public IReadOnlyList<GpuRenderGraphResource> Resources { get; }
}
