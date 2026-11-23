using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

internal sealed class Direct3D12TextureView(Direct3D12Texture texture, TextureViewInfo info) : IGraphicsTextureView, IGraphicsNativeOrigin
{
    private bool _disposed;

    IGraphicDevice IGraphicsNativeOrigin.Device => Owner;

    object IGraphicsNativeOrigin.AllocationIdentity => ((IGraphicsNativeOrigin)texture).AllocationIdentity;

    public IGraphicsTexture Texture => texture;

    public TextureViewInfo Info { get; } = info;

    internal Direct3D12Device Owner => texture.Owner;

    internal nint Native
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return texture.Native;
        }
    }

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

        _disposed = true;
    }
}
