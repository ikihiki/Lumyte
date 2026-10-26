using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Resources.Graphics.Tests;

internal sealed class TestTexture(TextureDesc desc) : IGraphicsTexture
{
    public uint Width => desc.Width;

    public uint Height => desc.Height;

    public uint MipLevels => desc.MipLevels;

    public uint ArrayLayers => desc.ArrayLayers;

    public TextureFormat Format => desc.Format;

    public TextureUsage Usage => desc.Usage;

    internal bool Disposed { get; private set; }

    public (uint Width, uint Height) GetMipSize(uint mipLevel) => (Width >> (int)mipLevel, Height >> (int)mipLevel);

    public IGraphicsTextureView CreateView(TextureViewDesc? description = null) => new View(this);

    public void Dispose() => Disposed = true;

    private sealed class View(TestTexture texture) : IGraphicsTextureView
    {
        public IGraphicsTexture Texture => texture;

        public TextureViewInfo Info => throw new NotSupportedException();

        public void Dispose() => Assert.False(texture.Disposed);
    }
}
