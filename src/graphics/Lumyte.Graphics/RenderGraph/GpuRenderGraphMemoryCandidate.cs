namespace Lumyte.Graphics.RenderGraph;

internal sealed record GpuRenderGraphMemoryCandidate(
    int DeclarationIndex,
    GpuRenderGraphResourceInfo Info,
    GpuTransientLifetime Lifetime,
    ulong Size,
    ulong Alignment,
    ulong Compatibility);
