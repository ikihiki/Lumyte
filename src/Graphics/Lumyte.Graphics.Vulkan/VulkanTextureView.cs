using Lumyte.Graphics.Abstractions;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

internal sealed unsafe class VulkanTextureView(VulkanTexture texture, TextureViewInfo info, ImageView native) : IGraphicsTextureView
{
    private bool _disposed;

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
