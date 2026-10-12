using System.Buffers;
using System.Runtime.CompilerServices;

namespace Lumyte.Memory;

internal sealed class MemoryScope(MemoryManager manager, string owner) : IMemoryScope
{
    private readonly object _gate = new();
    private readonly HashSet<IDisposable> _owned = new(ReferenceEqualityComparer.Instance);
    private bool _disposed;

    public string Owner { get; } = owner;

    public ValueTask<IMemoryReservation> ReserveAsync(MemoryRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            MemoryReservation reservation = manager.Reserve(this, request);
            _owned.Add(reservation);
            return ValueTask.FromResult<IMemoryReservation>(reservation);
        }
    }

    public ValueTask<IMemoryBuffer<T>> RentAsync<T>(MemoryPoolId pool, int length, MemoryPurpose purpose, CancellationToken cancellationToken = default)
        where T : unmanaged
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (length <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        ulong requestedBytes = checked((ulong)length * (uint)Unsafe.SizeOf<T>());
        MemoryReservation reservation;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            reservation = manager.Reserve(this, new() { Pool = pool, Bytes = requestedBytes, Purpose = purpose });
            _owned.Add(reservation);
        }

        T[]? array = null;
        try
        {
            array = ArrayPool<T>.Shared.Rent(length);
            ulong chargedBytes = checked((ulong)array.Length * (uint)Unsafe.SizeOf<T>());
            MemoryCharge charge = reservation.CommitCore(new(chargedBytes, MemoryAccuracy.Exact), true);
            var buffer = new MemoryBuffer<T>(this, charge, array, length, chargedBytes);
            Replace(charge, buffer);
            return ValueTask.FromResult<IMemoryBuffer<T>>(buffer);
        }
        catch
        {
            if (array is not null)
            {
                ArrayPool<T>.Shared.Return(array);
            }

            reservation.Dispose();
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        IDisposable[] owned;
        lock (_gate)
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;
            owned = [.. _owned];
            _owned.Clear();
        }

        foreach (IDisposable item in owned)
        {
            item.Dispose();
        }

        manager.ReleaseScope(this);
        return ValueTask.CompletedTask;
    }

    internal void Replace(IDisposable previous, IDisposable next)
    {
        bool dispose;
        lock (_gate)
        {
            _owned.Remove(previous);
            dispose = _disposed;
            if (!dispose)
            {
                _owned.Add(next);
            }
        }

        if (dispose)
        {
            next.Dispose();
        }
    }

    internal void Forget(IDisposable item)
    {
        lock (_gate)
        {
            _owned.Remove(item);
        }
    }
}
