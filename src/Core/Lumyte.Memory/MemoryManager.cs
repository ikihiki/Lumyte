namespace Lumyte.Memory;

/// <summary>Implements scoped budgets with pooled CPU buffers and explicit reclamation.</summary>
public sealed class MemoryManager : IMemoryManager
{
    private readonly object _gate = new();
    private readonly Dictionary<MemoryPoolId, MemoryBudget> _budgets;
    private readonly Dictionary<MemoryPoolId, PoolState> _pools = [];
    private readonly HashSet<MemoryScope> _scopes = [];
    private readonly HashSet<IMemoryReclaimer> _reclaimers = new(ReferenceEqualityComparer.Instance);
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="MemoryManager"/> class with unlimited pools.</summary>
    public MemoryManager()
        : this(new Dictionary<MemoryPoolId, MemoryBudget>())
    {
    }

    /// <summary>Initializes a new instance of the <see cref="MemoryManager"/> class with immutable pool budgets.</summary>
    /// <param name="budgets">The configured per-pool budgets.</param>
    public MemoryManager(IReadOnlyDictionary<MemoryPoolId, MemoryBudget> budgets)
    {
        ArgumentNullException.ThrowIfNull(budgets);
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

    /// <inheritdoc />
    public IMemoryScope CreateScope(string owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        lock (_gate)
        {
            ThrowIfDisposed();
            var scope = new MemoryScope(this, owner);
            _scopes.Add(scope);
            return scope;
        }
    }

    /// <inheritdoc />
    public MemorySnapshot GetSnapshot(MemoryPoolId pool)
    {
        ValidatePool(pool);
        lock (_gate)
        {
            return Snapshot(pool, GetPool(pool));
        }
    }

    /// <inheritdoc />
    public IDisposable RegisterReclaimer(IMemoryReclaimer reclaimer)
    {
        ArgumentNullException.ThrowIfNull(reclaimer);
        lock (_gate)
        {
            ThrowIfDisposed();
            if (!_reclaimers.Add(reclaimer))
            {
                throw new InvalidOperationException("The memory reclaimer is already registered.");
            }

            return new ReclaimerRegistration(this, reclaimer);
        }
    }

    /// <inheritdoc />
    public async ValueTask<ulong> ReclaimAsync(MemoryPoolId pool, ulong targetBytes, CancellationToken cancellationToken = default)
    {
        ValidatePool(pool);
        if (targetBytes == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetBytes));
        }

        SemaphoreSlim serial;
        lock (_gate)
        {
            ThrowIfDisposed();
            serial = GetPool(pool).ReclaimSerial;
        }

        await serial.WaitAsync(cancellationToken).ConfigureAwait(false);
        List<Exception>? failures = null;
        ulong reclaimed = 0;
        try
        {
            IMemoryReclaimer[] reclaimers;
            lock (_gate)
            {
                ThrowIfDisposed();
                GetPool(pool).IsReclaiming = true;
                reclaimers = [.. _reclaimers];
            }

            foreach (IMemoryReclaimer reclaimer in reclaimers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ulong before = Committed(pool);
                try
                {
                    await reclaimer.ReclaimAsync(pool, targetBytes - Math.Min(targetBytes, reclaimed), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception error)
                {
                    (failures ??= []).Add(error);
                }

                ulong after = Committed(pool);
                reclaimed = checked(reclaimed + (before > after ? before - after : 0));
                if (reclaimed >= targetBytes)
                {
                    break;
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                GetPool(pool).IsReclaiming = false;
            }

            serial.Release();
        }

        if (failures is not null)
        {
            throw new AggregateException("One or more memory reclaimers failed.", failures);
        }

        return reclaimed;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            if (_scopes.Count != 0 || _reclaimers.Count != 0 || _pools.Values.Any(static state => state.ReservedBytes != 0 || state.CommittedBytes != 0 || state.IsReclaiming))
            {
                throw new InvalidOperationException("Dispose all memory scopes, charges, reservations and reclaimer registrations before disposing the manager.");
            }

            _disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    internal MemoryReservation Reserve(MemoryScope scope, MemoryRequest request)
    {
        ValidateRequest(request);
        lock (_gate)
        {
            ThrowIfDisposed();
            PoolState state = GetPool(request.Pool);
            if (state.IsReclaiming)
            {
                throw new InvalidOperationException($"Memory pool '{request.Pool}' cannot be reserved from a reclaimer callback.");
            }

            MemoryBudget budget = GetBudget(request.Pool);
            if (!Fits(state.CommittedBytes, state.ReservedBytes, request.Bytes, budget.HardLimitBytes))
            {
                throw new MemoryBudgetExceededException(request.Pool, request.Bytes, Snapshot(request.Pool, state));
            }

            state.ReservedBytes = checked(state.ReservedBytes + request.Bytes);
            return new(this, scope, request);
        }
    }

    internal MemoryCharge Commit(MemoryReservation reservation, MemoryUsage usage, bool enforceHardLimit)
    {
        lock (_gate)
        {
            PoolState state = GetPool(reservation.Request.Pool);
            ulong remainingReserved = state.ReservedBytes - reservation.Request.Bytes;
            if (enforceHardLimit && !Fits(state.CommittedBytes, remainingReserved, usage.Bytes, GetBudget(reservation.Request.Pool).HardLimitBytes))
            {
                throw new MemoryBudgetExceededException(reservation.Request.Pool, usage.Bytes, Snapshot(reservation.Request.Pool, state));
            }

            ulong committedBytes = checked(state.CommittedBytes + usage.Bytes);
            ulong estimatedBytes = usage.Accuracy == MemoryAccuracy.Estimate ? checked(state.EstimatedBytes + usage.Bytes) : state.EstimatedBytes;
            state.ReservedBytes = remainingReserved;
            state.CommittedBytes = committedBytes;
            state.EstimatedBytes = estimatedBytes;

            return new(this, reservation.Request.Pool, usage);
        }
    }

    internal void ReleaseReservation(MemoryRequest request)
    {
        lock (_gate)
        {
            PoolState state = GetPool(request.Pool);
            state.ReservedBytes -= request.Bytes;
        }
    }

    internal void ReleaseCharge(MemoryPoolId pool, MemoryUsage usage)
    {
        lock (_gate)
        {
            PoolState state = GetPool(pool);
            state.CommittedBytes -= usage.Bytes;
            if (usage.Accuracy == MemoryAccuracy.Estimate)
            {
                state.EstimatedBytes -= usage.Bytes;
            }
        }
    }

    internal void ReleaseScope(MemoryScope scope)
    {
        lock (_gate)
        {
            _scopes.Remove(scope);
        }
    }

    private bool Fits(ulong committed, ulong reserved, ulong requested, ulong hardLimit)
        => committed <= hardLimit && reserved <= hardLimit - committed && requested <= hardLimit - committed - reserved;

    private ulong Committed(MemoryPoolId pool)
    {
        lock (_gate)
        {
            return GetPool(pool).CommittedBytes;
        }
    }

    private MemoryBudget GetBudget(MemoryPoolId pool) => _budgets.GetValueOrDefault(pool, MemoryBudget.Unlimited);

    private PoolState GetPool(MemoryPoolId pool)
    {
        if (!_pools.TryGetValue(pool, out PoolState? state))
        {
            state = new();
            _pools.Add(pool, state);
        }

        return state;
    }

    private MemorySnapshot Snapshot(MemoryPoolId pool, PoolState state)
    {
        MemoryBudget budget = GetBudget(pool);
        bool overBudget = state.CommittedBytes > budget.HardLimitBytes || state.ReservedBytes > budget.HardLimitBytes - Math.Min(state.CommittedBytes, budget.HardLimitBytes);
        return new()
        {
            CommittedBytes = state.CommittedBytes,
            ReservedBytes = state.ReservedBytes,
            EstimatedBytes = state.EstimatedBytes,
            SoftLimitBytes = budget.SoftLimitBytes,
            HardLimitBytes = budget.HardLimitBytes,
            IsOverBudget = overBudget,
        };
    }

    private void ValidatePool(MemoryPoolId pool)
    {
        if (string.IsNullOrWhiteSpace(pool.Value))
        {
            throw new ArgumentException("Supply an initialized memory pool identifier.", nameof(pool));
        }
    }

    private void ValidateRequest(MemoryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidatePool(request.Pool);
        if (request.Bytes == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "A memory reservation must contain at least one byte.");
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed class PoolState
    {
        internal ulong CommittedBytes { get; set; }

        internal ulong ReservedBytes { get; set; }

        internal ulong EstimatedBytes { get; set; }

        internal bool IsReclaiming { get; set; }

        internal SemaphoreSlim ReclaimSerial { get; } = new(1, 1);
    }

    private sealed class ReclaimerRegistration(MemoryManager owner, IMemoryReclaimer reclaimer) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            lock (owner._gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                owner._reclaimers.Remove(reclaimer);
            }
        }
    }
}
