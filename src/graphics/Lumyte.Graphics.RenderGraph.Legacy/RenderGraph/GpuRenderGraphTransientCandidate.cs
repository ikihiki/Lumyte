namespace Lumyte.Graphics.RenderGraph.Legacy;

internal sealed record GpuRenderGraphTransientCandidate(
    int DeclarationIndex,
    GpuRenderGraphResourceInfo Info,
    GpuTransientLifetime Lifetime);
