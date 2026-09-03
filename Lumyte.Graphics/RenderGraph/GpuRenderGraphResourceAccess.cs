namespace Lumyte.Graphics.RenderGraph;

internal sealed record GpuRenderGraphResourceAccess(
    GpuRenderGraphResource Resource,
    GpuRenderGraphAccess Access,
    GpuStage Stage,
    GpuBarrierHazards Hazards);
