using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation.Tests;

internal sealed class FactoryView(IGraphicsTexture texture) : IGraphicsTextureView
{
    public IGraphicsTexture Texture => texture;

    public TextureViewInfo Info { get; } = new()
    {
        Format = texture.Format,
        Dimension = TextureViewDimension.D2,
        BaseMipLevel = 1,
        MipLevelCount = 1,
        BaseArrayLayer = 0,
        ArrayLayerCount = 1,
    };

    internal int DisposeCount { get; private set; }

    public void Dispose() => DisposeCount++;
}
