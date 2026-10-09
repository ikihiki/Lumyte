using System.Runtime.InteropServices.JavaScript;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal sealed class BrowserTextureView : IGraphicsTextureView
{
    private readonly BrowserTexture _texture;
    private readonly JSObject _native;
    private bool _disposed;

    internal BrowserTextureView(BrowserTexture texture, TextureViewInfo info, JSObject native)
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
