using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.RenderGraph.Common;

public readonly record struct DrawRenderPassResources(
    GpuRenderGraphTexture Target,
    IReadOnlyList<GpuRenderGraphTexture> SampledTextures);
