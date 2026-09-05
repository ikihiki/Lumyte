namespace Lumyte.Graphics.RenderGraph;

internal sealed record GpuRenderGraphPlanCacheEntry(
    GpuRenderGraphStructure Structure,
    GpuRenderGraphPlan Template,
    IReadOnlyDictionary<GpuRenderGraphResource, int> TemplateResourceIndices);
