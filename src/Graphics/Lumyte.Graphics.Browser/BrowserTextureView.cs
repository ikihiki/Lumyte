using System.Runtime.InteropServices.JavaScript;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal sealed class BrowserTextureView(BrowserTexture texture, TextureViewInfo info, JSObject native) : IGraphicsTextureView, IGraphicsNativeOrigin
{
    private bool _disposed;

    IGraphicDevice IGraphicsNativeOrigin.Device => Owner;

    object IGraphicsNativeOrigin.AllocationIdentity => ((IGraphicsNativeOrigin)texture).AllocationIdentity;

    public IGraphicsTexture Texture => texture;

    public TextureViewInfo Info { get; } = info;

    internal JSObject Native
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return native;
        }
    }

    internal BrowserDevice Owner => texture.Owner;

    void IGraphicsNativeOrigin.ValidateOrigin()
    {
        ObjectDisposedException.ThrowIf(Owner.Status == GraphicsDeviceStatus.Disposed, Owner);
        if (Owner.Status == GraphicsDeviceStatus.Lost)
        {
            throw new GraphicsDeviceLostException(Owner.Loss!);
        }

        _ = Native;
        ((IGraphicsNativeOrigin)texture).ValidateOrigin();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        native.Dispose();
        _disposed = true;
    }
}
