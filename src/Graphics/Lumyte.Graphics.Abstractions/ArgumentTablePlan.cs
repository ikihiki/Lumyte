namespace Lumyte.Graphics.Abstractions;

/// <summary>Owns append-only logical registrations partitioned in whole-draw execution order.</summary>
/// <param name="device">The borrowed device supplying effective helper limits.</param>
/// <remarks>Collect and materialize serially. Native table leases do not own registered resources or synchronize GPU access.</remarks>
public sealed class ArgumentTablePlan(IGraphicDevice device) : IDisposable
{
    private readonly object _gate = new();
    private readonly List<ArgumentTableReservation> _reservations = [];
    private readonly List<ArgumentPartition> _partitions = [];
    private int _state;
    private int _users = 1;
    private bool _returned;
    private bool _recordingReady;

    /// <summary>Gets the borrowed owning device.</summary>
    public IGraphicDevice Device { get; } = device ?? throw new ArgumentNullException(nameof(device));

    /// <summary>Gets reservations in collection order; hosts may supply a different live execution order at materialization.</summary>
    public IReadOnlyList<ArgumentTableReservation> Reservations => _reservations.AsReadOnly();

    /// <summary>Gets successful native registration and partition accounting.</summary>
    public ArgumentTablePlanStatistics Statistics { get; private set; } = new();

    /// <summary>Gets the borrowed materialized tables; never mutate slots or dispose these directly.</summary>
    public IReadOnlyList<IArgumentTable> Tables
    {
        get
        {
            ValidateReferences();
            return _partitions.Where(p => p.Table is not null).Select(p => p.Table!).ToArray();
        }
    }

    /// <summary>Gets a value indicating whether native references are available in this live generation.</summary>
    public bool IsMaterialized => ReferencesAvailable;

    internal bool ReferencesAvailable => _state == 2 && _users != 0;

    /// <summary>Starts one indivisible draw/dispatch reservation; neither clip nor pipeline changes force a partition.</summary>
    /// <param name="colorAttachments">The actual color-attachment count consuming backend combined limits.</param>
    /// <returns>A reservation local to this owner generation.</returns>
    public ArgumentTableReservation Reserve(uint colorAttachments = 1)
    {
        ValidateCollecting();
        var reservation = new ArgumentTableReservation(this, colorAttachments);
        _reservations.Add(reservation);
        return reservation;
    }

    /// <summary>Creates an opaque resource set for one indivisible draw or dispatch.</summary>
    /// <returns>A resource set belonging to this generation.</returns>
    public ShaderResources CreateShaderResources() => new(Reserve());

    /// <summary>Gets whether a resource set still belongs to this generation.</summary>
    /// <param name="resources">The opaque resource set.</param>
    /// <returns>True for a current, owned resource set; otherwise false.</returns>
    public bool Owns(ShaderResources resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        return _reservations.Any(resources.Uses);
    }

    /// <summary>Configures the actual semantic output count before collection closes.</summary>
    /// <param name="resources">The current resource set.</param>
    /// <param name="colorAttachments">The actual color output count, or zero for compute.</param>
    public void ConfigureExecution(ShaderResources resources, uint colorAttachments)
    {
        ValidateCollecting();
        FindScope(resources).ConfigureColorAttachments(colorAttachments);
    }

    /// <summary>Materializes live opaque resource sets in their actual execution order.</summary>
    /// <param name="execution">Distinct, current resource sets owned by this generation.</param>
    /// <param name="cancellationToken">Cancels preparation without publishing partial resources.</param>
    public void MaterializeResources(IReadOnlyList<ShaderResources> execution, CancellationToken cancellationToken = default)
    {
        ValidateCollecting();
        ArgumentNullException.ThrowIfNull(execution);
        Materialize(execution.Select(FindScope).ToArray(), cancellationToken);
    }

    /// <summary>Captures an unpublished collection boundary for an allocation-strategy rollback.</summary>
    /// <returns>An owner-specific checkpoint; default is invalid.</returns>
    public ArgumentTableCheckpoint Checkpoint()
    {
        ValidateCollecting();
        return new(this, _reservations.Count);
    }

    /// <summary>Discards only new reservation requests, without releasing or altering earlier registrations.</summary>
    /// <param name="checkpoint">A checkpoint from this still-collecting generation.</param>
    public void Rollback(ArgumentTableCheckpoint checkpoint)
    {
        ValidateCollecting();
        if (!ReferenceEquals(checkpoint.Owner, this) || checkpoint.Count > _reservations.Count)
        {
            throw new ArgumentException("The checkpoint does not belong to this collection.", nameof(checkpoint));
        }

        for (int i = checkpoint.Count; i < _reservations.Count; i++)
        {
            _reservations[i].ClearRequests();
        }

        _reservations.RemoveRange(checkpoint.Count, _reservations.Count - checkpoint.Count);
    }

    /// <summary>Discards selected unpublished reservations without altering another module's intervening requests.</summary>
    /// <param name="reservations">Distinct reservations still belonging to this collecting generation.</param>
    public void Discard(IReadOnlyList<ArgumentTableReservation> reservations)
    {
        ValidateCollecting();
        if (reservations.Distinct().Count() != reservations.Count || reservations.Any(scope => !_reservations.Contains(scope)))
        {
            throw new ArgumentException("Discard only distinct reservations owned by this collection.", nameof(reservations));
        }

        foreach (ArgumentTableReservation scope in reservations)
        {
            scope.ClearRequests();
            _reservations.Remove(scope);
        }
    }

    /// <summary>Resolves all active draws, greedily partitions them, then atomically publishes real native references.</summary>
    /// <param name="execution">Live reservations in actual execution order, or null for collection order; duplicates are rejected.</param>
    /// <param name="cancellationToken">Cancels without publishing partially populated tables.</param>
    public void Materialize(IReadOnlyList<ArgumentTableReservation>? execution = null, CancellationToken cancellationToken = default)
    {
        ValidateCollecting();
        _state = 1;
        try
        {
            IReadOnlyList<ArgumentTableReservation> active = execution ?? _reservations;
            var seen = new HashSet<ArgumentTableReservation>();
            var resolved = new List<(ArgumentTableReservation Scope, List<(ArgumentRequest Request, ResolvedArgument Value)> Requests)>();
            foreach (ArgumentTableReservation scope in active)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!ReferenceEquals(scope.Owner, this) || !_reservations.Contains(scope) || !seen.Add(scope))
                {
                    throw new ArgumentException("Execution contains a foreign, rolled-back, or repeated reservation.", nameof(execution));
                }

                var requests = new List<(ArgumentRequest Request, ResolvedArgument Value)>();
                foreach (ArgumentRequest request in scope.Requests)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ResolvedArgument value = request.Resolve();
                    ValidateRange(value.Key);
                    scope.Inspect(value.Key);
                    requests.Add((request, value));
                }

                // Test a complete draw in an empty partition before any native allocation.
                var probe = new ArgumentPartition(Device.ArgumentLimits());
                if (requests.Count != 0 && !probe.Fits(requests.Select(r => r.Value), scope.ColorAttachments))
                {
                    throw new NotSupportedException("One draw exceeds the enabled argument slot, binding, range, or combined descriptor limits.");
                }

                resolved.Add((scope, requests));
            }

            ArgumentPartition? current = null;
            var selected = new List<(ArgumentTableReservation Scope, ArgumentPartition? Partition, List<(ArgumentRequest Request, ResolvedArgument Value)> Requests)>();
            foreach ((ArgumentTableReservation scope, List<(ArgumentRequest Request, ResolvedArgument Value)> requests) in resolved)
            {
                if (requests.Count == 0)
                {
                    selected.Add((scope, null, requests));
                    continue;
                }

                if (current is null || !current.Fits(requests.Select(r => r.Value), scope.ColorAttachments))
                {
                    current = new(Device.ArgumentLimits());
                    _partitions.Add(current);
                }

                current.Append(requests.Select(r => r.Value));
                selected.Add((scope, current, requests));
            }

            long textures = 0;
            long samplers = 0;
            long buffers = 0;
            foreach (ArgumentPartition partition in _partitions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                partition.Create(Device, cancellationToken);
                textures += partition.TextureCount;
                samplers += partition.SamplerCount;
                buffers += partition.BufferCount;
            }

            // No handle is published until every write to every table succeeded.
            foreach ((ArgumentTableReservation scope, ArgumentPartition? partition, List<(ArgumentRequest Request, ResolvedArgument Value)> requests) in selected)
            {
                foreach ((ArgumentRequest request, ResolvedArgument value) in requests)
                {
                    request.Publish(partition!.Reference(value.Key));
                }

                scope.Select(partition);
            }

            Statistics = new()
            {
                Tables = _partitions.Count,
                Textures = textures,
                Samplers = samplers,
                Buffers = buffers,
                ReusedTextures = resolved.Sum(r => r.Scope.ReusedTextures + r.Requests.Count(v => v.Value.Key.Namespace == 0)) - textures,
                ReusedSamplers = resolved.Sum(r => r.Scope.ReusedSamplers + r.Requests.Count(v => v.Value.Key.Namespace == 1)) - samplers,
                ReusedBuffers = resolved.Sum(r => r.Scope.ReusedBuffers + r.Requests.Count(v => v.Value.Key.Namespace == 2)) - buffers,
                Rollovers = Math.Max(0, _partitions.Count - 1),
            };
            _state = 2;
            foreach (ArgumentTableReservation scope in selected.Select(selection => selection.Scope))
            {
                cancellationToken.ThrowIfCancellationRequested();
                scope.FinalizeResources();
                ValidateReferences();
            }

            cancellationToken.ThrowIfCancellationRequested();
            _recordingReady = true;
        }
        catch
        {
            Invalidate();
            throw;
        }
        finally
        {
            foreach (ArgumentTableReservation scope in _reservations)
            {
                scope.ClearRequests();
            }

            foreach (ArgumentPartition partition in _partitions)
            {
                partition.ClearPlanning();
            }
        }
    }

    /// <summary>Retains only this generation's table ownership through GPU completion and command disposal.</summary>
    /// <returns>An independently retryable, idempotent lease.</returns>
    public IDisposable Retain()
    {
        lock (_gate)
        {
            if (_users == 0 || _state == 3)
            {
                throw new InvalidOperationException("The argument generation is no longer usable.");
            }

            _users++;
            return new Lease(this);
        }
    }

    /// <summary>Invalidates this generation's references and recording after preparation fails; ownership remains available for cleanup.</summary>
    public void Invalidate()
    {
        _recordingReady = false;
        _state = 3;
    }

    /// <summary>Returns the owner token; failed native disposal remains owned and can be retried.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (!_returned)
            {
                Release();
                _returned = true;
            }
        }
    }

    internal void ValidateCollecting()
    {
        if (_state != 0 || _users == 0)
        {
            throw new InvalidOperationException("Argument collection is closed.");
        }
    }

    internal void ValidateReferences()
    {
        if (!ReferencesAvailable)
        {
            throw new InvalidOperationException("References require a live, successfully materialized argument generation.");
        }
    }

    internal void ValidateRecording()
    {
        ValidateReferences();
        if (!_recordingReady)
        {
            throw new InvalidOperationException("Recording requires every resource finalizer to complete successfully.");
        }
    }

    internal void ValidateScope(ArgumentTableReservation scope)
    {
        if (!ReferenceEquals(scope.Owner, this) || !_reservations.Contains(scope))
        {
            throw new InvalidOperationException("The resource set was discarded or rolled back.");
        }
    }

    private ArgumentTableReservation FindScope(ShaderResources resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        return _reservations.FirstOrDefault(resources.Uses)
            ?? throw new ArgumentException("The resource set is foreign, discarded, or rolled back.", nameof(resources));
    }

    private void ValidateRange(ArgumentKey key)
    {
        if (key.Namespace != 2)
        {
            return;
        }

        if (key.Count > uint.MaxValue)
        {
            throw new NotSupportedException("The helper element count exceeds its uint wire representation.");
        }

        ArgumentTableBindingLimits limits = Device.ArgumentLimits();
        uint alignment = Math.Max(1u, limits.BufferRangeAlignment);
        uint sizeAlignment = Math.Max(1u, limits.BufferRangeSizeAlignment);
        if (key.Stride == 0 || key.Size == 0 || key.Offset % alignment != 0 || key.Size % sizeAlignment != 0 || key.Size > limits.MaximumBufferRangeBytes)
        {
            throw new NotSupportedException("The registered storage range exceeds the enabled binding range or alignment.");
        }
    }

    private void Release()
    {
        if (_users == 1)
        {
            for (int i = _partitions.Count - 1; i >= 0; i--)
            {
                _partitions[i].Dispose();
            }

            _partitions.Clear();
            foreach (ArgumentTableReservation scope in _reservations)
            {
                scope.ClearRequests();
            }
        }

        _users--;
    }

    private sealed class Lease(ArgumentTablePlan owner) : IDisposable
    {
        private bool _returned;

        public void Dispose()
        {
            lock (owner._gate)
            {
                if (!_returned)
                {
                    owner.Release();
                    _returned = true;
                }
            }
        }
    }
}
