using Lumyte.Graphics.Abstractions;
using A = Ahjo.Wgpu;

namespace Lumyte.Graphics.Wgpu;

internal sealed class WgpuTextureView(WgpuTexture texture, TextureViewInfo info, A.TextureView native) : IGraphicsTextureView
{
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

        native.Dispose();
        _disposed = true;
    }
}
