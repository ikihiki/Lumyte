using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal sealed class BrowserSemaphore(BrowserDevice owner) : IGraphicsSemaphore
{
    private bool _disposed;

    internal BrowserDevice Owner => owner;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        owner.Validation?.Release(this);

        _disposed = true;
    }
}
