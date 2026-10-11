using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Wgpu;

internal sealed class WgpuSemaphore(WgpuDevice owner) : IGraphicsSemaphore
{
    private bool _disposed;

    internal WgpuDevice Owner => owner;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
    }
}
