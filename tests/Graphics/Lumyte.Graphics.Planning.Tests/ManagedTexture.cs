using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning.Tests;

/// <summary>Provides deterministic managed test instrumentation.</summary>
internal sealed class ManagedTexture(TextureDesc desc, List<string> events) : IGraphicsTexture
{
    public uint Width => desc.Width;

    public uint Height => desc.Height;

    public uint MipLevels => desc.MipLevels;

    public uint ArrayLayers => desc.ArrayLayers;

    public TextureFormat Format => desc.Format;

    public TextureUsage Usage => desc.Usage;

    public (uint Width, uint Height) GetMipSize(uint mipLevel) => mipLevel < MipLevels ? (Math.Max(1u, Width >> (int)mipLevel), Math.Max(1u, Height >> (int)mipLevel)) : throw new ArgumentOutOfRangeException(nameof(mipLevel));

    public IGraphicsTextureView CreateView(TextureViewDesc? desc = null)
    {
        events.Add("allocate-view");
        return new ManagedView(this, events, desc);
    }

    public void Dispose() => events.Add("dispose-texture");
}
