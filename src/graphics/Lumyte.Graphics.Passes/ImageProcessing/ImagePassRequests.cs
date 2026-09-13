using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Passes;

public sealed record ClearPassRequest(GpuRenderGraphTexture Target, GpuGraphValue<TextureClearValue> Value);
public sealed record TextureCopyPassRequest(GpuRenderGraphTexture Source, GpuRenderGraphTexture Target);
public sealed record OutputPassRequest(
    GpuRenderGraphTexture Source,
    GpuRenderGraphTexture Target,
    OutputEncoding Encoding = OutputEncoding.Srgb,
    OutputAlphaMode AlphaMode = OutputAlphaMode.Opaque);

public readonly record struct TexturePassResult(GpuRenderGraphTexture Target);

public enum OutputEncoding
{
    Srgb,
    Linear,
}

public enum OutputAlphaMode
{
    Opaque,
    Premultiplied,
}
