namespace Lumyte.Memory;

internal sealed class MemoryReservation(MemoryManager manager, MemoryScope scope, MemoryRequest request) : IMemoryReservation, IMemoryAllocation
{
    private const int Pending = 0;
    private const int Committing = 1;
    private const int Committed = 2;
    private const int Returned = 3;
    private const int ReturnRequested = 4;

    private int _state;
    private MemoryCharge? _charge;

    public MemoryRequest Request { get; } = request;

    internal MemoryScope Scope => scope;

    public IMemoryCharge Commit(MemoryUsage usage)
    {
        scope.EnterOperation();
        try
        {
            if (Interlocked.CompareExchange(ref _state, Committing, Pending) != Pending)
            {
                throw new InvalidOperationException("The memory reservation has already been committed or returned.");
            }

            MemoryCharge charge;
            try
            {
                charge = manager.Commit(this, usage, false);
            }
            catch
            {
                if (Interlocked.CompareExchange(ref _state, Pending, Committing) == ReturnRequested)
                {
                    Volatile.Write(ref _state, Returned);
                    manager.ReleaseReservation(Request);
                    scope.Forget(this);
                }

                throw;
            }

            // Keep the existing scope entry: publishing ownership after ledger commit must not allocate.
            _charge = charge;
            Volatile.Write(ref _state, Committed);
            return charge;
        }
        finally
        {
            scope.ExitOperation();
        }
    }

    public void Dispose()
    {
        if (!scope.TryEnterOperation())
        {
            return;
        }

        try
        {
            Return();
        }
        finally
        {
            scope.ExitOperation();
        }
    }

    public void Release()
    {
        if (Volatile.Read(ref _state) == Committed)
        {
            _charge!.Release();
        }
        else
        {
            Return();
        }
    }

    private void Return()
    {
        while (true)
        {
            int previous = Volatile.Read(ref _state);
            if (previous is Committed or Returned or ReturnRequested)
            {
                return;
            }

            int next = previous == Committing ? ReturnRequested : Returned;
            if (Interlocked.CompareExchange(ref _state, next, previous) != previous)
            {
                continue;
            }

            if (next == Returned)
            {
                manager.ReleaseReservation(Request);
                scope.Forget(this);
            }

            return;
        }
    }
}
