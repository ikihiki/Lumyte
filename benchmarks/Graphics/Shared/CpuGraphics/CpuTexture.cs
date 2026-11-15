using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Benchmarks.Testing;

internal sealed class CpuTexture(CpuGraphicsDevice owner, TextureDesc desc) : CpuResource(owner), IGraphicsTexture
{
    private int _views;

    public uint Width => desc.Width;

    public uint Height => desc.Height;

    public uint MipLevels => desc.MipLevels;

    public uint ArrayLayers => desc.ArrayLayers;

    public TextureFormat Format => desc.Format;

    public TextureUsage Usage => desc.Usage;

    public (uint Width, uint Height) GetMipSize(uint mipLevel)
    {
        Check();
        if (mipLevel >= MipLevels)
        {
            throw new ArgumentOutOfRangeException(nameof(mipLevel));
        }

        return (Math.Max(1, Width >> (int)mipLevel), Math.Max(1, Height >> (int)mipLevel));
    }

    public IGraphicsTextureView CreateView(TextureViewDesc? desc = null)
    {
        Check();
        desc ??= new()
        {
            Dimension = ArrayLayers == 1 ? TextureViewDimension.D2 : TextureViewDimension.D2Array,
        }
       ;
        uint m = desc.MipLevelCount ?? (MipLevels - desc.BaseMipLevel);
        uint l = desc.ArrayLayerCount ?? (desc.Dimension == TextureViewDimension.D2 ? 1 : ArrayLayers - desc.BaseArrayLayer);
        if (desc.BaseMipLevel >= MipLevels || m == 0 || m > MipLevels - desc.BaseMipLevel || desc.BaseArrayLayer >= ArrayLayers || l == 0 || l > ArrayLayers - desc.BaseArrayLayer)
        {
            throw new ArgumentOutOfRangeException(nameof(desc));
        }

        var info = new TextureViewInfo
        {
            Format = Format,
            Dimension = desc.Dimension,
            BaseMipLevel = desc.BaseMipLevel,
            MipLevelCount = m,
            BaseArrayLayer = desc.BaseArrayLayer,
            ArrayLayerCount = l,
        }
       ;
        _views++;
        return new CpuView(Owner, this, info);
    }

    public override void Dispose()
    {
        if (Disposed)
        {
            return;
        }

        if (_views != 0)
        {
            throw new InvalidOperationException("Dispose _views before texture.");
        }

        base.Dispose();
    }

    internal void ReleaseView() => _views--;
}
