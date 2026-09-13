namespace Lumyte.Graphics.RenderGraph.Legacy;

internal readonly record struct GpuRenderGraphAccessStructure(
    int ResourceIndex,
    GpuRenderGraphAccess Access,
    GpuStage Stage,
    GpuBarrierHazards Hazards);
