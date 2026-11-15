using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation.Tests;

internal sealed class PresentationTexture : IGraphicsTexture
{
    public uint Width => 32;

    public uint Height => 16;

    public uint MipLevels => 1;

    public uint ArrayLayers => 1;

    public TextureFormat Format => TextureFormat.Bgra8Unorm;

    public TextureUsage Usage => TextureUsage.RenderAttachment;

    public int DisposeCount { get; private set; }

    public PresentationView? LastView { get; private set; }

    public (uint Width, uint Height) GetMipSize(uint mipLevel) => (Width, Height);

    public IGraphicsTextureView CreateView(TextureViewDesc? desc = null)
    {
        LastView = new(this);
        return LastView;
    }

    public void Dispose() => DisposeCount++;
}
