using System.Runtime.InteropServices.JavaScript;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal sealed class BrowserTextureView(BrowserTexture texture, TextureViewInfo info, JSObject native) : IGraphicsTextureView
{
    private int _registrationCount;
    private bool _disposed;

    public IGraphicsTexture Texture => texture;

    public TextureViewInfo Info { get; } = info;

    internal BrowserDevice Owner => texture.Owner;

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
