using System.Runtime.InteropServices.JavaScript;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal sealed class BrowserTexture : IGraphicsTexture
{
    private readonly BrowserDevice _owner;
    private readonly JSObject _native;
    private readonly SurfaceFrameLifetime? _surfaceFrame;
    private int _viewCount;
    private bool _disposed;

    internal BrowserTexture(BrowserDevice owner, TextureDesc desc)
    {
        (_owner, Width, Height, MipLevels, ArrayLayers, Format, Usage) = (owner, desc.Width, desc.Height, desc.MipLevels, desc.ArrayLayers, desc.Format, desc.Usage);
        _native = BrowserInterop.CreateTexture(owner.Handle, checked((int)Width), checked((int)Height), checked((int)ArrayLayers), checked((int)MipLevels), (int)Format, (int)Usage);
    }

    internal BrowserTexture(BrowserDevice owner, TextureDesc desc, JSObject native, SurfaceFrameLifetime lifetime)
    {
        (_owner, Width, Height, MipLevels, ArrayLayers, Format, Usage, _native, _surfaceFrame) = (owner, desc.Width, desc.Height, 1, 1, desc.Format, desc.Usage, native, lifetime);
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
            _surfaceFrame?.ValidateRecording();
            return _native;
        }
    }

    internal BrowserDevice Owner => _owner;

    internal SurfaceFrameLifetime? SurfaceFrame => _surfaceFrame;

    public (uint Width, uint Height) GetMipSize(uint mipLevel)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _surfaceFrame?.ValidateRecording();
        if (mipLevel >= MipLevels)
        {
            throw new ArgumentOutOfRangeException(nameof(mipLevel));
        }

        return (Math.Max(1U, Width >> (int)mipLevel), Math.Max(1U, Height >> (int)mipLevel));
    }

    public IGraphicsTextureView CreateView(TextureViewDesc? desc = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _surfaceFrame?.ValidateRecording();
        TextureViewInfo info = TextureValidation.Resolve(this, desc);
        JSObject native = BrowserInterop.CreateTextureView(_native, (int)info.Dimension, checked((int)info.BaseMipLevel), checked((int)info.MipLevelCount), checked((int)info.BaseArrayLayer), checked((int)info.ArrayLayerCount));
        var view = new BrowserTextureView(this, info, native);
        _viewCount++;
        return view;
    }

    public void Dispose()
    {
        if (_surfaceFrame != null)
        {
            throw new InvalidOperationException("The frame owns this borrowed presentation image.");
        }

        DisposeLease();
    }

    internal void DisposeLease()
    {
        if (_disposed)
        {
            return;
        }

        if (_viewCount != 0)
        {
            throw new InvalidOperationException("Dispose all views before disposing their texture.");
        }

        if (_surfaceFrame == null)
        {
            BrowserInterop.DestroyTexture(_native);
        }

        // A canvas owns borrowed images; releasing the managed reference must not destroy them.
        _native.Dispose();
        _disposed = true;
        if (_surfaceFrame == null)
        {
            _owner.ReleaseTexture();
        }
    }

    internal void ReleaseView() => _viewCount--;
}
