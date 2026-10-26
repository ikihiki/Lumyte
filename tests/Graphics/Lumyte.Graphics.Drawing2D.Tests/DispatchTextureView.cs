using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D.Tests;

internal sealed class DispatchTextureView(IGraphicsTexture texture, List<string>? lifetime) : IGraphicsTextureView
{
    public IGraphicsTexture Texture => texture;

    public TextureViewInfo Info => new()
    {
        Format = texture.Format,
        Dimension = TextureViewDimension.D2,
        BaseMipLevel = 0,
        MipLevelCount = 1,
        BaseArrayLayer = 0,
        ArrayLayerCount = 1,
    };

    public void Dispose() => lifetime?.Add("view");
}
