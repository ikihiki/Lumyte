namespace Lumyte.Graphics.RenderGraph.Legacy;

internal sealed record GpuRenderGraphPlanCacheEntry(
    GpuRenderGraphStructure Structure,
    GpuRenderGraphPlan Template,
    IReadOnlyDictionary<GpuRenderGraphResource, int> TemplateResourceIndices);
