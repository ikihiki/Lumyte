using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Pipelines;

internal sealed class MappedBufferOwner<TElement>(IGraphicsBuffer<TElement> buffer) : IDisposable
    where TElement : unmanaged
{
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (buffer.IsMapped)
        {
            buffer.Unmap();
        }

        buffer.Dispose();
        _disposed = true;
    }
}
