namespace Lumyte.Graphics.RenderGraph;

internal readonly record struct GpuRenderGraphAccessStructure(
    int ResourceIndex,
    GpuRenderGraphAccess Access,
    GpuStage Stage,
    GpuBarrierHazards Hazards);
