namespace Lumyte.Graphics.RenderGraph;

internal sealed record GpuRenderGraphTransientCandidate(
    int DeclarationIndex,
    GpuRenderGraphResourceInfo Info,
    GpuTransientLifetime Lifetime);
