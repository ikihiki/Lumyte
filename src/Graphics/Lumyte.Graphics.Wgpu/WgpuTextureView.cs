using Lumyte.Graphics.Abstractions;
using A = Ahjo.Wgpu;

namespace Lumyte.Graphics.Wgpu;

internal sealed class WgpuTextureView(WgpuTexture texture, TextureViewInfo info, A.TextureView native) : IGraphicsTextureView
{
    private int _registrationCount;
    private bool _disposed;

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

        native.Dispose();
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
