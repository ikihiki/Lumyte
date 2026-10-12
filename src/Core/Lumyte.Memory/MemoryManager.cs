using System.Collections.Immutable;

namespace Lumyte.Memory;

/// <summary>Implements scoped budgets with thread-owned CPU chunks and explicit reclamation.</summary>
public sealed class MemoryManager : IMemoryManager
{
    private readonly Dictionary<MemoryPoolId, MemoryBudget> _budgets;
    private ImmutableDictionary<MemoryPoolId, MemoryPoolState> _pools = ImmutableDictionary<MemoryPoolId, MemoryPoolState>.Empty;
    private ReclaimerRegistration[] _reclaimers = [];
    private int _owners;

    /// <summary>Initializes a new instance of the <see cref="MemoryManager"/> class with unlimited pools.</summary>
    public MemoryManager()
        : this(new Dictionary<MemoryPoolId, MemoryBudget>())
    {
    }

    /// <summary>Initializes a new instance of the <see cref="MemoryManager"/> class with immutable pool budgets.</summary>
    /// <param name="budgets">The configured per-pool budgets.</param>
    /// <param name="chunkSizeBytes">The preferred byte capacity of each thread-owned CPU chunk.</param>
    public MemoryManager(IReadOnlyDictionary<MemoryPoolId, MemoryBudget> budgets, int chunkSizeBytes = 65536)
    {
        ArgumentNullException.ThrowIfNull(budgets);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chunkSizeBytes);
        ChunkSizeBytes = chunkSizeBytes;
        _budgets = new(budgets.Count);
        foreach ((MemoryPoolId pool, MemoryBudget budget) in budgets)
        {
            ValidatePool(pool);
            if (budget.SoftLimitBytes > budget.HardLimitBytes)
            {
                throw new ArgumentException("A memory budget soft limit cannot exceed its hard limit.", nameof(budgets));
            }

            _budgets.Add(pool, budget);
        }
    }

    internal int ChunkSizeBytes { get; }

    /// <inheritdoc />
    public IMemoryScope CreateScope(string owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        AcquireOwner();
        try
        {
            return new MemoryScope(this, owner);
        }
        catch
        {
            ReleaseOwner();
            throw;
        }
    }

    /// <inheritdoc />
    public MemorySnapshot GetSnapshot(MemoryPoolId pool)
    {
        ValidatePool(pool);
        return GetPool(pool).GetSnapshot();
    }

    /// <inheritdoc />
    public IDisposable RegisterReclaimer(IMemoryReclaimer reclaimer)
    {
        ArgumentNullException.ThrowIfNull(reclaimer);
        AcquireOwner();
        try
        {
            var registration = new ReclaimerRegistration(this, reclaimer);
            while (true)
            {
                ReclaimerRegistration[] before = Volatile.Read(ref _reclaimers);
                if (Array.Exists(before, existing => ReferenceEquals(existing.Reclaimer, reclaimer)))
                {
                    throw new InvalidOperationException("The memory reclaimer is already registered.");
                }

                ReclaimerRegistration[] after = [.. before, registration];
                if (ReferenceEquals(Interlocked.CompareExchange(ref _reclaimers, after, before), before))
                {
                    return registration;
                }
            }
        }
        catch
        {
            ReleaseOwner();
            throw;
        }
    }

    /// <inheritdoc />
    public async ValueTask<ulong> ReclaimAsync(MemoryPoolId pool, ulong targetBytes, CancellationToken cancellationToken = default)
    {
        ValidatePool(pool);
        ArgumentOutOfRangeException.ThrowIfZero(targetBytes);
        AcquireOwner();
        try
        {
            MemoryPoolState state = GetPool(pool);
            await state.ReclaimSerial.WaitAsync(cancellationToken).ConfigureAwait(false);
            List<Exception>? failures = null;
            ulong reclaimed = 0;
            try
            {
                state.SetReclaiming(true);
                ReclaimerRegistration[] reclaimers = Volatile.Read(ref _reclaimers);
                foreach (ReclaimerRegistration registration in reclaimers)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ulong before = state.ReleasedBytes;
                    try
                    {
                        await registration.Reclaimer.ReclaimAsync(pool, targetBytes - Math.Min(targetBytes, reclaimed), cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception error)
                    {
                        (failures ??= []).Add(error);
                    }

                    // The cumulative release counter also observes frees obscured by concurrent commits.
                    reclaimed = checked(reclaimed + unchecked(state.ReleasedBytes - before));
                    if (reclaimed >= targetBytes)
                    {
                        break;
                    }
                }
            }
            finally
            {
                state.SetReclaiming(false);
                state.ReclaimSerial.Release();
            }

            if (failures is not null)
            {
                throw new AggregateException("One or more memory reclaimers failed.", failures);
            }

            return reclaimed;
        }
        finally
        {
            ReleaseOwner();
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        int owners = Interlocked.CompareExchange(ref _owners, -1, 0);
        if (owners > 0)
        {
            throw new InvalidOperationException("Dispose all memory scopes, charges, reservations and reclaimer registrations before disposing the manager.");
        }

        return ValueTask.CompletedTask;
    }

    internal MemoryPoolState GetAllocationPool(MemoryPoolId pool)
    {
        ValidatePool(pool);
        ThrowIfDisposed();
        return GetPool(pool);
    }

    internal MemoryReservation Reserve(MemoryScope scope, MemoryRequest request)
    {
        ValidateRequest(request);
        ThrowIfDisposed();
        var reservation = new MemoryReservation(this, scope, request);
        GetPool(request.Pool).Reserve(request.Bytes);
        return reservation;
    }

    internal MemoryCharge Commit(MemoryReservation reservation, MemoryUsage usage, bool enforceHardLimit)
    {
        var charge = new MemoryCharge(this, reservation, reservation.Request.Pool, usage);
        GetPool(reservation.Request.Pool).Commit(reservation.Request.Bytes, usage, enforceHardLimit);
        return charge;
    }

    internal void ReleaseReservation(MemoryRequest request) => GetPool(request.Pool).ReleaseReservation(request.Bytes);

    internal void ReleaseCharge(MemoryPoolId pool, MemoryUsage usage) => GetPool(pool).ReleaseCharge(usage);

    internal void ReleaseScope(MemoryScope scope) => ReleaseOwner();

    private static void ValidatePool(MemoryPoolId pool)
    {
        if (string.IsNullOrWhiteSpace(pool.Value))
        {
            throw new ArgumentException("Supply an initialized memory pool identifier.", nameof(pool));
        }
    }

    private static void ValidateRequest(MemoryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidatePool(request.Pool);
        if (request.Bytes == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "A memory reservation must contain at least one byte.");
        }
    }

    private MemoryPoolState GetPool(MemoryPoolId pool)
    {
        MemoryPoolState? candidate = null;
        while (true)
        {
            ImmutableDictionary<MemoryPoolId, MemoryPoolState> before = Volatile.Read(ref _pools);
            if (before.TryGetValue(pool, out MemoryPoolState? existing))
            {
                return existing;
            }

            candidate ??= new(pool, _budgets.GetValueOrDefault(pool, MemoryBudget.Unlimited));
            ImmutableDictionary<MemoryPoolId, MemoryPoolState> after = before.Add(pool, candidate);
            if (ReferenceEquals(Interlocked.CompareExchange(ref _pools, after, before), before))
            {
                return candidate;
            }
        }
    }

    private void AcquireOwner()
    {
        while (true)
        {
            int before = Volatile.Read(ref _owners);
            ObjectDisposedException.ThrowIf(before < 0, this);
            int after = checked(before + 1);
            if (Interlocked.CompareExchange(ref _owners, after, before) == before)
            {
                return;
            }
        }
    }

    private void ReleaseOwner() => Interlocked.Decrement(ref _owners);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _owners) < 0, this);

    private void Unregister(ReclaimerRegistration registration)
    {
        while (true)
        {
            ReclaimerRegistration[] before = Volatile.Read(ref _reclaimers);
            int index = Array.IndexOf(before, registration);
            var after = new ReclaimerRegistration[before.Length - 1];
            Array.Copy(before, 0, after, 0, index);
            Array.Copy(before, index + 1, after, index, before.Length - index - 1);
            if (ReferenceEquals(Interlocked.CompareExchange(ref _reclaimers, after, before), before))
            {
                ReleaseOwner();
                return;
            }
        }
    }

    private sealed class ReclaimerRegistration(MemoryManager owner, IMemoryReclaimer reclaimer) : IDisposable
    {
        private int _disposed;

        internal IMemoryReclaimer Reclaimer { get; } = reclaimer;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.Unregister(this);
            }
        }
    }
}
