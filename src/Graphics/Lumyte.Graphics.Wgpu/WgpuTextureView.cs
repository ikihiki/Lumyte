using Lumyte.Graphics.Abstractions;
using A = Ahjo.Wgpu;

namespace Lumyte.Graphics.Wgpu;

internal sealed class WgpuTextureView : IGraphicsTextureView
{
    private readonly WgpuTexture _texture;
    private readonly A.TextureView _native;
    private bool _disposed;

    internal WgpuTextureView(WgpuTexture texture, TextureViewInfo info, A.TextureView native)
    {
        (_texture, Info, _native) = (texture, info, native);
    }

    public IGraphicsTexture Texture => _texture;

    public TextureViewInfo Info { get; }

    public void Dispose()
    {
        lock (_texture.Owner.ResourceGate)
        {
            if (_disposed)
            {
                return;
            }

            _native.Dispose();
            _disposed = true;
            _texture.ReleaseView();
        }
    }
}
