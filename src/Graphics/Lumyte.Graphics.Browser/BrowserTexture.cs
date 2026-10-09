using System.Runtime.InteropServices.JavaScript;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal sealed class BrowserTexture : IGraphicsTexture
{
    private readonly BrowserDevice _owner;
    private readonly JSObject _native;
    private int _viewCount;
    private bool _disposed;

    internal BrowserTexture(BrowserDevice owner, TextureDesc desc)
    {
        (_owner, Width, Height, MipLevels, ArrayLayers, Format, Usage) = (owner, desc.Width, desc.Height, desc.MipLevels, desc.ArrayLayers, desc.Format, desc.Usage);
        _native = BrowserInterop.CreateTexture(owner.Handle, checked((int)Width), checked((int)Height), checked((int)ArrayLayers), checked((int)MipLevels), (int)Format, (int)Usage);
    }

    public uint Width { get; }

    public uint Height { get; }

    public uint MipLevels { get; }

    public uint ArrayLayers { get; }

    public TextureFormat Format { get; }

    public TextureUsage Usage { get; }

    internal JSObject Native
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _native;
        }
    }

    internal BrowserDevice Owner => _owner;

    public (uint Width, uint Height) GetMipSize(uint mipLevel)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (mipLevel >= MipLevels)
        {
            throw new ArgumentOutOfRangeException(nameof(mipLevel));
        }

        return (Math.Max(1U, Width >> (int)mipLevel), Math.Max(1U, Height >> (int)mipLevel));
    }

    public IGraphicsTextureView CreateView(TextureViewDesc? desc = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        TextureViewInfo info = TextureValidation.Resolve(this, desc);
        JSObject native = BrowserInterop.CreateTextureView(_native, (int)info.Dimension, checked((int)info.BaseMipLevel), checked((int)info.MipLevelCount), checked((int)info.BaseArrayLayer), checked((int)info.ArrayLayerCount));
        var view = new BrowserTextureView(this, info, native);
        _viewCount++;
        return view;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_viewCount != 0)
        {
            throw new InvalidOperationException("Dispose all views before disposing their texture.");
        }

        BrowserInterop.DestroyTexture(_native);
        _native.Dispose();
        _disposed = true;
        _owner.ReleaseTexture();
    }

    internal void ReleaseView() => _viewCount--;
}
