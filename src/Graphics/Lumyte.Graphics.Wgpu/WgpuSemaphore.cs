using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Wgpu;

internal sealed class WgpuSemaphore(WgpuDevice owner) : IGraphicsSemaphore
{
    private bool _disposed;

    internal WgpuDevice Owner => owner;

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
