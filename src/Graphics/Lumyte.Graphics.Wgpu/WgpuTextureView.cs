using Lumyte.Graphics.Abstractions;
using A = Ahjo.Wgpu;

namespace Lumyte.Graphics.Wgpu;

internal sealed class WgpuTextureView(WgpuTexture texture, TextureViewInfo info, A.TextureView native) : IGraphicsTextureView, IGraphicsNativeOrigin
{
    private bool _disposed;

    IGraphicDevice IGraphicsNativeOrigin.Device => Owner;

    object IGraphicsNativeOrigin.AllocationIdentity => ((IGraphicsNativeOrigin)texture).AllocationIdentity;

    public IGraphicsTexture Texture => texture;

    public TextureViewInfo Info { get; } = info;

    internal A.TextureView Native
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return native;
        }
    }

    internal WgpuDevice Owner => texture.Owner;

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
