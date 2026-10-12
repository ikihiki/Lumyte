namespace Lumyte.Memory;

internal sealed class MemoryCharge(MemoryManager manager, MemoryPoolId pool, MemoryUsage usage) : IMemoryCharge
{
    private int _disposed;

    public MemoryPoolId Pool { get; } = pool;

    public MemoryUsage Usage { get; } = usage;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            manager.ReleaseCharge(Pool, Usage);
        }
    }
}
