using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation.Tests;

internal sealed class PresentationView(IGraphicsTexture texture) : IGraphicsTextureView
{
    public IGraphicsTexture Texture { get; } = texture;

    public TextureViewInfo Info { get; } = new()
    {
        Format = TextureFormat.Bgra8Unorm,
        Dimension = TextureViewDimension.D2,
        BaseMipLevel = 0,
        MipLevelCount = 1,
        BaseArrayLayer = 0,
        ArrayLayerCount = 1,
    };

    public int DisposeCount { get; private set; }

    public void Dispose() => DisposeCount++;
}
