using Lumyte.Graphics.RenderGraph.Legacy;

namespace Lumyte.Graphics.TwoD;

public readonly record struct RenderPassResources(
    GpuRenderGraphTexture Target,
    IReadOnlyList<GpuRenderGraphBuffer> Buffers,
    IReadOnlyList<GpuRenderGraphTexture> Images);
