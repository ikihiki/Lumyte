using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.TwoD;

public readonly record struct RenderPassResources(
    GpuRenderGraphTexture Target,
    IReadOnlyList<GpuRenderGraphBuffer> Buffers,
    IReadOnlyList<GpuRenderGraphTexture> Images);
