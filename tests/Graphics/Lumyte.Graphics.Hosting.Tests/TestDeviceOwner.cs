namespace Lumyte.Graphics.Hosting.Tests;

internal sealed class TestDeviceOwner(TestDevice device) : IGraphicsDeviceOwner
{
    private bool _disposed;

    public IGraphicDevice Device
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return device;
        }
    }

    public async ValueTask DisposeAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await device.DisposeAsync();
        _disposed = true;
    }
}
