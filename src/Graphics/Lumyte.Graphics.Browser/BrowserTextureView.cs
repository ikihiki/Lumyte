using System.Runtime.InteropServices.JavaScript;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal sealed class BrowserTextureView(BrowserTexture texture, TextureViewInfo info, JSObject native) : IGraphicsTextureView
{
    private bool _disposed;

    public IGraphicsTexture Texture => texture;

    public TextureViewInfo Info { get; } = info;

    internal JSObject Native
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return native;
        }
    }

    internal BrowserDevice Owner => texture.Owner;

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
