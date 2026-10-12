using System.Collections.Immutable;

namespace Lumyte.Memory;

internal sealed class MemoryScope(MemoryManager manager, string owner) : IMemoryScope
{
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ScopeState _state = new([], 0, false);

    public string Owner { get; } = owner;

    public ValueTask<IMemoryReservation> ReserveAsync(MemoryRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnterOperation();
        try
        {
            MemoryReservation reservation = manager.Reserve(this, request);
            try
            {
                Track(reservation);
                return ValueTask.FromResult<IMemoryReservation>(reservation);
            }
            catch
            {
                reservation.Release();
                throw;
            }
        }
        finally
        {
            ExitOperation();
        }
    }

    public ValueTask<IMemoryBuffer<T>> RentAsync<T>(MemoryPoolId pool, int length, MemoryPurpose purpose, CancellationToken cancellationToken = default)
        where T : unmanaged
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        EnterOperation();
        try
        {
            MemoryPoolState allocationPool = manager.GetAllocationPool(pool);
            allocationPool.CheckCanRent();
            MemoryChunk<T> chunk = ThreadMemoryChunks<T>.Rent(allocationPool, length, manager.ChunkSizeBytes, out int offset);
            MemoryBuffer<T>? buffer = null;
            try
            {
                buffer = new(this, chunk, offset, length);
                Track(buffer);
                return ValueTask.FromResult<IMemoryBuffer<T>>(buffer);
            }
            catch
            {
                if (buffer is null)
                {
                    chunk.Release();
                }
                else
                {
                    buffer.Release();
                }

                throw;
            }
        }
        finally
        {
            ExitOperation();
        }
    }

    public ValueTask DisposeAsync()
    {
        while (true)
        {
            ScopeState previous = Volatile.Read(ref _state);
            if (previous.Closed)
            {
                return new(_completion.Task);
            }

            ScopeState next = previous with { Closed = true };
            if (!ReferenceEquals(Interlocked.CompareExchange(ref _state, next, previous), previous))
            {
                continue;
            }

            if (next.Operations == 0)
            {
                Drain(next);
            }

            return new(_completion.Task);
        }
    }

    internal void EnterOperation() => ObjectDisposedException.ThrowIf(!TryEnterOperation(), this);

    internal void ExitOperation()
    {
        while (true)
        {
            ScopeState previous = Volatile.Read(ref _state);
            ScopeState next = previous with { Operations = previous.Operations - 1 };
            if (!ReferenceEquals(Interlocked.CompareExchange(ref _state, next, previous), previous))
            {
                continue;
            }

            if (next.Closed && next.Operations == 0)
            {
                Drain(next);
            }

            return;
        }
    }

    internal void DisposeAllocation(IMemoryAllocation allocation)
    {
        // A closed scope already owns the release, including any operation it is draining.
        if (!TryEnterOperation())
        {
            return;
        }

        try
        {
            allocation.Release();
        }
        finally
        {
            ExitOperation();
        }
    }

    internal void Forget(IMemoryAllocation allocation)
    {
        while (true)
        {
            ScopeState previous = Volatile.Read(ref _state);
            ScopeState next = previous with { Owned = previous.Owned.Remove(allocation) };
            if (ReferenceEquals(Interlocked.CompareExchange(ref _state, next, previous), previous))
            {
                return;
            }
        }
    }

    internal bool TryEnterOperation()
    {
        while (true)
        {
            ScopeState previous = Volatile.Read(ref _state);
            if (previous.Closed)
            {
                return false;
            }

            ScopeState next = previous with { Operations = checked(previous.Operations + 1) };
            if (ReferenceEquals(Interlocked.CompareExchange(ref _state, next, previous), previous))
            {
                return true;
            }
        }
    }

    private void Track(IMemoryAllocation allocation)
    {
        while (true)
        {
            ScopeState previous = Volatile.Read(ref _state);
            ScopeState next = previous with { Owned = previous.Owned.Add(allocation) };
            if (ReferenceEquals(Interlocked.CompareExchange(ref _state, next, previous), previous))
            {
                return;
            }
        }
    }

    private void Drain(ScopeState closed)
    {
        try
        {
            foreach (IMemoryAllocation allocation in closed.Owned)
            {
                allocation.Release();
            }

            manager.ReleaseScope(this);
            _completion.TrySetResult();
        }
        catch (Exception error)
        {
            _completion.TrySetException(error);
        }
    }

    private sealed record ScopeState(ImmutableHashSet<IMemoryAllocation> Owned, int Operations, bool Closed);
}
