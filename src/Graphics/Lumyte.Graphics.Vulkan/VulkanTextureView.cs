using Lumyte.Graphics.Abstractions;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

internal sealed unsafe class VulkanTextureView(VulkanTexture texture, TextureViewInfo info, ImageView native) : IGraphicsTextureView, IGraphicsNativeOrigin
{
    private bool _disposed;

    IGraphicDevice IGraphicsNativeOrigin.Device => Owner;

    object IGraphicsNativeOrigin.AllocationIdentity => ((IGraphicsNativeOrigin)texture).AllocationIdentity;

    public IGraphicsTexture Texture => texture;

    public TextureViewInfo Info { get; } = info;

    internal ImageView Native
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return native;
        }
    }

    internal VulkanDevice Owner => texture.Owner;

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

        texture.Owner.Api.DestroyImageView(texture.Owner.NativeDevice, native, null);
        _disposed = true;
    }
}
