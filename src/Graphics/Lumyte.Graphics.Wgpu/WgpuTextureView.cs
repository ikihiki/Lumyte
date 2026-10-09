using Lumyte.Graphics.Abstractions;
using A = Ahjo.Wgpu;

namespace Lumyte.Graphics.Wgpu;

internal sealed class WgpuTextureView : IGraphicsTextureView
{
    private readonly WgpuTexture _texture;
    private readonly A.TextureView _native;
    private int _registrationCount;
    private bool _disposed;

    internal WgpuTextureView(WgpuTexture texture, TextureViewInfo info, A.TextureView native)
    {
        (_texture, Info, _native) = (texture, info, native);
    }

    public IGraphicsTexture Texture => _texture;

    public TextureViewInfo Info { get; }

    internal WgpuDevice Owner => _texture.Owner;

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

        _native.Dispose();
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
