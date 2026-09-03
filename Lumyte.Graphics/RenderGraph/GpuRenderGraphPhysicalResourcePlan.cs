namespace Lumyte.Graphics.RenderGraph;

internal sealed record GpuRenderGraphPhysicalResourcePlan(
    GpuRenderGraphResource Resource,
    GpuTransientLifetime Lifetime,
    int ReuseSlot,
    ulong Size,
    ulong Alignment,
    ulong Compatibility);
