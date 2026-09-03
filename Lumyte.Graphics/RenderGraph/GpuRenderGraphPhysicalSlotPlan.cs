namespace Lumyte.Graphics.RenderGraph;

public sealed class GpuRenderGraphPhysicalSlotPlan
{
    internal GpuRenderGraphPhysicalSlotPlan(
        int slot,
        GpuRenderGraphResourceKind kind,
        GpuMemoryKind memoryKind,
        ulong size,
        ulong alignment,
        ulong compatibility,
        GpuRenderGraphResource[] resources)
    {
        Slot = slot;
        Kind = kind;
        MemoryKind = memoryKind;
        Size = size;
        Alignment = alignment;
        Compatibility = compatibility;
        Resources = Array.AsReadOnly(resources);
    }

    public int Slot { get; }
    public GpuRenderGraphResourceKind Kind { get; }
    public GpuMemoryKind MemoryKind { get; }
    public ulong Size { get; }
    public ulong Alignment { get; }
    public ulong Compatibility { get; }
    public IReadOnlyList<GpuRenderGraphResource> Resources { get; }
}
