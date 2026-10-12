namespace Lumyte.Memory;

internal sealed class MemoryReservation(MemoryManager manager, MemoryScope scope, MemoryRequest request) : IMemoryReservation
{
    private readonly object _gate = new();
    private int _state;

    public MemoryRequest Request { get; } = request;

    public IMemoryCharge Commit(MemoryUsage usage) => CommitCore(usage, false);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_state != 0)
            {
                return;
            }

            _state = 2;
            manager.ReleaseReservation(Request);
            scope.Forget(this);
        }
    }

    internal MemoryCharge CommitCore(MemoryUsage usage, bool enforceHardLimit)
    {
        lock (_gate)
        {
            if (_state != 0)
            {
                throw new InvalidOperationException("The memory reservation has already been committed or returned.");
            }

            MemoryCharge charge = manager.Commit(this, usage, enforceHardLimit);
            _state = 1;
            scope.Replace(this, charge);
            return charge;
        }
    }
}
