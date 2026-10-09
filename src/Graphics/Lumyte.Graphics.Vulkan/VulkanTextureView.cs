using Lumyte.Graphics.Abstractions;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

internal sealed unsafe class VulkanTextureView(VulkanTexture texture, TextureViewInfo info, ImageView native) : IGraphicsTextureView
{
    private int _registrationCount;
    private bool _disposed;

    public IGraphicsTexture Texture => texture;

    public TextureViewInfo Info { get; } = info;

    internal VulkanDevice Owner => texture.Owner;

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

        texture.Owner.Api.DestroyImageView(texture.Owner.NativeDevice, native, null);
        _disposed = true;
        texture.ReleaseView();
    }

    internal void RetainRegistration()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _registrationCount = checked(_registrationCount + 1);
    }

    internal void ReleaseRegistration() => _registrationCount--;
}
