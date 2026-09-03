namespace Lumyte.Graphics.RenderGraph;

/// <summary>
/// Lifetime and logical reuse-slot assignment for one live graph-created resource.
/// A reuse slot is a compile-time plan and does not by itself imply native memory aliasing.
/// </summary>
public sealed record GpuRenderGraphTransientResourcePlan(
    GpuRenderGraphResource Resource,
    GpuTransientLifetime Lifetime,
    int ReuseSlot);
