namespace Lumyte.Graphics.RenderGraph;

public sealed record GpuRenderGraphResourceAccess(
    GpuRenderGraphResource Resource,
    GpuRenderGraphAccess Access,
    GpuStage Stage,
    GpuBarrierHazards Hazards);
