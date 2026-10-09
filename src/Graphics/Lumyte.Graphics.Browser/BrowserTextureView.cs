using System.Runtime.InteropServices.JavaScript;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal sealed class BrowserTextureView : IGraphicsTextureView
{
    private readonly BrowserTexture _texture;
    private readonly JSObject _native;
    private int _registrationCount;
    private bool _disposed;

    internal BrowserTextureView(BrowserTexture texture, TextureViewInfo info, JSObject native)
    {
        (_texture, Info, _native) = (texture, info, native);
    }

    public IGraphicsTexture Texture => _texture;

    public TextureViewInfo Info { get; }

    internal BrowserDevice Owner => _texture.Owner;

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
