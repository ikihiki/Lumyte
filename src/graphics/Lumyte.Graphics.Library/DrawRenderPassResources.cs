using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Library;

public readonly record struct DrawRenderPassResources(
    GpuRenderGraphTexture Target,
    IReadOnlyList<GpuRenderGraphTexture> SampledTextures,
    IReadOnlyList<GpuRenderGraphBuffer> ShaderBuffers);
