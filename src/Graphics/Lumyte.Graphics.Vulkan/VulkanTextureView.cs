using Lumyte.Graphics.Abstractions;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

internal sealed unsafe class VulkanTextureView : IGraphicsTextureView
{
    private readonly VulkanTexture _texture;
    private readonly ImageView _native;
    private bool _disposed;

    internal VulkanTextureView(VulkanTexture texture, TextureViewInfo info, ImageView native)
    {
        (_texture, Info, _native) = (texture, info, native);
    }

    public IGraphicsTexture Texture => _texture;

    public TextureViewInfo Info { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _texture.Owner.Api.DestroyImageView(_texture.Owner.NativeDevice, _native, null);
        _disposed = true;
        _texture.ReleaseView();
    }
}
