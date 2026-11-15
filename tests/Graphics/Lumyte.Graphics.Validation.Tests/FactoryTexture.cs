using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation.Tests;

internal sealed class FactoryTexture(TextureDesc desc) : IGraphicsTexture
{
    public uint Width => desc.Width;

    public uint Height => desc.Height;

    public uint MipLevels => desc.MipLevels;

    public uint ArrayLayers => desc.ArrayLayers;

    public TextureFormat Format => desc.Format;

    public TextureUsage Usage => desc.Usage;

    internal TextureViewDesc? LastViewDesc { get; private set; }

    internal FactoryView? View { get; private set; }

    internal int DisposeCount { get; private set; }

    public (uint Width, uint Height) GetMipSize(uint mipLevel) => (Width >> (int)mipLevel, Height >> (int)mipLevel);

    public IGraphicsTextureView CreateView(TextureViewDesc? desc = null)
    {
        LastViewDesc = desc;
        return View ??= new FactoryView(this);
    }

    public void Dispose() => DisposeCount++;
}
