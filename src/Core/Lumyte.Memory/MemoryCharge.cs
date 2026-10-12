namespace Lumyte.Memory;

internal sealed class MemoryCharge(MemoryManager manager, MemoryReservation reservation, MemoryPoolId pool, MemoryUsage usage) : IMemoryCharge, IMemoryAllocation
{
    private int _disposed;

    public MemoryPoolId Pool { get; } = pool;

    public MemoryUsage Usage { get; } = usage;

    public void Dispose() => reservation.Scope.DisposeAllocation(this);

    public void Release()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            manager.ReleaseCharge(Pool, Usage);
            reservation.Scope.Forget(reservation);
        }
    }
}
