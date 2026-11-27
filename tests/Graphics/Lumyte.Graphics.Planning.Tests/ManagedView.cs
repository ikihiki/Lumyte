using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning.Tests;

/// <summary>Provides deterministic managed test instrumentation.</summary>
internal sealed class ManagedView(IGraphicsTexture texture, List<string> events, TextureViewDesc? description = null) : IGraphicsTextureView
{
    public IGraphicsTexture Texture => texture;

    public TextureViewInfo Info => new() { Format = texture.Format, Dimension = description?.Dimension ?? TextureViewDimension.D2, BaseMipLevel = description?.BaseMipLevel ?? 0, MipLevelCount = description?.MipLevelCount ?? texture.MipLevels, BaseArrayLayer = description?.BaseArrayLayer ?? 0, ArrayLayerCount = description?.ArrayLayerCount ?? texture.ArrayLayers };

    public void Dispose() => events.Add("dispose-view");
}
