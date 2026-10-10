using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal sealed class BrowserSemaphore(BrowserDevice owner) : IGraphicsSemaphore
{
    private bool _disposed;

    internal BrowserDevice Owner => owner;

    internal BinarySemaphoreState State { get; } = new();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        State.ValidateDispose();
        State.MarkDisposed();
        _disposed = true;
        owner.ReleaseSemaphore();
    }
}
