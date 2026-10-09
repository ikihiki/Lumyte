using Lumyte.Graphics.Abstractions;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

internal sealed unsafe class VulkanTextureView : IGraphicsTextureView
{
    private readonly VulkanTexture _texture;
    private readonly ImageView _native;
    private int _registrationCount;
    private bool _disposed;

    internal VulkanTextureView(VulkanTexture texture, TextureViewInfo info, ImageView native)
    {
        (_texture, Info, _native) = (texture, info, native);
    }

    public IGraphicsTexture Texture => _texture;

    public TextureViewInfo Info { get; }

    internal VulkanDevice Owner => _texture.Owner;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_registrationCount != 0)
        {
            throw new InvalidOperationException("Release all argument table registrations before disposing their resource.");
        }

        _texture.Owner.Api.DestroyImageView(_texture.Owner.NativeDevice, _native, null);
        _disposed = true;
        _texture.ReleaseView();
    }

    internal void RetainRegistration()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _registrationCount = checked(_registrationCount + 1);
    }

    internal void ReleaseRegistration() => _registrationCount--;
}
