using System.Diagnostics;

namespace Lumyte.Diagnostics;

internal sealed class DiagnosticPump<TPoint>(IServiceProvider services, IEnumerable<DiagnosticRegistration> registrations, TimeProvider clock) : IDiagnosticPump<TPoint>, IDisposable
    where TPoint : class
{
    private readonly IServiceProvider _services = services;
    private readonly DiagnosticRegistration[] _registrations = registrations.Where(item => item.Point == typeof(TPoint)).ToArray();
    private readonly TimeProvider _clock = clock;
    private readonly object _gate = new();
    private readonly Lane _standard = new(192, 1024);
    private readonly Lane _stream = new(48, 1024);
    private readonly Lane _control = new(16, 256);
    private readonly Dictionary<(Guid Session, Guid Request), Entry> _requests = [];
    private readonly PriorityQueue<Entry, long> _expirations = new();
    private Dictionary<string, DiagnosticOperationSet> _sets = [];
    private Dictionary<(string Subsystem, string Operation), DiagnosticOperationTraffic> _traffic = [];
    private int? _owner;
    private bool _active;
    private bool _busy;
    private bool _disposed;
    private bool _streamNext = true;

    public IReadOnlyList<DiagnosticSubsystemCatalog> Catalog
    {
        get
        {
            lock (_gate)
            {
                return _registrations.Where(item => _sets.ContainsKey(item.Descriptor.Id))
                    .Select(item => new DiagnosticSubsystemCatalog(item.Descriptor, _sets[item.Descriptor.Id].Catalog)).ToArray();
            }
        }
    }

    public void Activate()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            CheckOwner();
            if (_active)
            {
                return;
            }

            var sets = new Dictionary<string, DiagnosticOperationSet>(StringComparer.Ordinal);
            var traffic = new Dictionary<(string Subsystem, string Operation), DiagnosticOperationTraffic>();
            foreach (DiagnosticRegistration registration in _registrations)
            {
                var set = new DiagnosticOperationSet(registration.Resolve(_services));
                sets.Add(registration.Descriptor.Id, set);
                foreach (OperationDescriptor operation in set.Catalog)
                {
                    traffic.Add((registration.Descriptor.Id, operation.Id), operation.Traffic);
                }
            }

            _sets = sets;
            _traffic = traffic;
            _active = true;
        }
    }

    public Task<DiagnosticOperationResult> SubmitAsync(DiagnosticRequest request)
    {
        lock (_gate)
        {
            if (!_active || _disposed)
            {
                return Task.FromResult(DiagnosticOperationResult.Reject("target-gone", "Execution point is not active."));
            }

            long now = _clock.GetTimestamp();
            if (request.DeadlineTimestamp <= now)
            {
                return Task.FromResult(DiagnosticOperationResult.Reject("expired", "Request has expired."));
            }

            Prune(now);

            (Guid SessionId, Guid RequestId) id = (request.Context.SessionId, request.Context.RequestId);
            if (_requests.TryGetValue(id, out Entry? existing))
            {
                return Same(existing.Request, request) ? existing.Completion.Task
                    : Task.FromResult(DiagnosticOperationResult.Reject("request-id-conflict", "Request ID was reused with different content."));
            }

            DiagnosticOperationTraffic traffic = _traffic.GetValueOrDefault((request.SubsystemId, request.OperationId));
            if (traffic == DiagnosticOperationTraffic.Stream && _clock.GetElapsedTime(now, request.DeadlineTimestamp) > TimeSpan.FromSeconds(1))
            {
                return Task.FromResult(DiagnosticOperationResult.Reject("invalid-timeout", "Stream requests must expire within one second."));
            }

            Lane lane = traffic switch
            {
                DiagnosticOperationTraffic.Stream => _stream,
                DiagnosticOperationTraffic.Control => _control,
                _ => _standard,
            };
            if (lane.Pending.Count >= lane.PendingCapacity || lane.RetainedCount >= lane.RetainedCapacity)
            {
                return Task.FromResult(DiagnosticOperationResult.Reject("busy", "Diagnostic queue is full."));
            }

            DiagnosticRequest owned = request with
            {
                Arguments = new Dictionary<string, DiagnosticValue>(request.Arguments, StringComparer.Ordinal),
                Permissions = new HashSet<DiagnosticPermission>(request.Permissions),
            };
            var entry = new Entry(owned, lane);
            _requests.Add(id, entry);
            lane.RetainedCount++;
            lane.Pending.Enqueue(entry);
            _expirations.Enqueue(entry, owned.DeadlineTimestamp);
            return entry.Completion.Task;
        }
    }

    public void Pump(DiagnosticFrame frame, DiagnosticBudget budget)
    {
        lock (_gate)
        {
            CheckOwner();
            if (_busy || !_active || _disposed)
            {
                throw new InvalidOperationException("Pump is inactive or reentrant.");
            }

            if (budget.MaxCommands < 0 || budget.MaxDuration < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(budget));
            }

            _busy = true;
        }

        long started = Stopwatch.GetTimestamp();
        try
        {
            for (int i = 0; i < budget.MaxCommands && Stopwatch.GetElapsedTime(started) < budget.MaxDuration; i++)
            {
                Entry entry;
                lock (_gate)
                {
                    if (!_active || !TryDequeue(out entry!))
                    {
                        break;
                    }
                }

                DiagnosticRequest request = entry.Request;
                DiagnosticOperationResult result = request.DeadlineTimestamp <= _clock.GetTimestamp()
                    ? DiagnosticOperationResult.Reject("expired", "Request expired before execution.")
                    : _sets.TryGetValue(request.SubsystemId, out DiagnosticOperationSet? set)
                        ? set.Invoke(request.OperationId, request.Arguments, request.Context with { Frame = frame }, request.Permissions)
                        : DiagnosticOperationResult.Reject("target-gone", "Subsystem was removed.");
                entry.Completion.TrySetResult(result);
            }
        }
        finally
        {
            lock (_gate)
            {
                _busy = false;
            }
        }
    }

    public void Deactivate()
    {
        lock (_gate)
        {
            CheckOwner();
            if (_busy)
            {
                throw new InvalidOperationException("Cannot deactivate inside a handler.");
            }

            Clear();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_busy)
            {
                throw new InvalidOperationException("Cannot dispose during Pump.");
            }

            Clear();
            _disposed = true;
        }
    }

    private static bool Same(DiagnosticRequest left, DiagnosticRequest right)
        => left.SubsystemId == right.SubsystemId && left.OperationId == right.OperationId
            && left.Context.ActorId == right.Context.ActorId && left.Context.ExpectedRevision == right.Context.ExpectedRevision
            && left.DeadlineTimestamp == right.DeadlineTimestamp && left.Arguments.Count == right.Arguments.Count
            && left.Arguments.All(pair => right.Arguments.TryGetValue(pair.Key, out DiagnosticValue value) && pair.Value == value)
            && left.Permissions.SetEquals(right.Permissions);

    private void CheckOwner()
    {
        _owner ??= Environment.CurrentManagedThreadId;
        if (_owner != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException("Wrong execution thread.");
        }
    }

    private void Prune(long now)
    {
        Entry? running = null;
        while (_expirations.TryPeek(out Entry? entry, out long deadline) && deadline <= now)
        {
            _expirations.Dequeue();
            if (entry.Started && !entry.Completion.Task.IsCompleted)
            {
                // A pump executes at most one handler; retain its identity without blocking other expirations.
                running = entry;
                continue;
            }

            _requests.Remove((entry.Request.Context.SessionId, entry.Request.Context.RequestId));
            entry.Lane.RetainedCount--;
            if (!entry.Started)
            {
                entry.Completion.TrySetResult(DiagnosticOperationResult.Reject("expired", "Request expired before execution."));
            }
        }

        if (running != null)
        {
            _expirations.Enqueue(running, running.Request.DeadlineTimestamp);
        }
    }

    private bool TryDequeue(out Entry? entry)
    {
        if (_control.Pending.TryDequeue(out entry))
        {
            entry.Started = true;
            return true;
        }

        Lane first = _streamNext ? _stream : _standard;
        Lane second = _streamNext ? _standard : _stream;
        if (!first.Pending.TryDequeue(out entry) && !second.Pending.TryDequeue(out entry))
        {
            return false;
        }

        _streamNext = entry.Lane == _standard;
        entry.Started = true;
        return true;
    }

    private void Clear()
    {
        _active = false;
        foreach (Entry entry in _requests.Values)
        {
            entry.Completion.TrySetResult(DiagnosticOperationResult.Reject("target-gone", "Execution point was stopped."));
        }

        _standard.Clear();
        _stream.Clear();
        _control.Clear();
        _requests.Clear();
        _expirations.Clear();
        _sets.Clear();
        _traffic.Clear();
        _streamNext = true;
    }

    private sealed class Entry(DiagnosticRequest request, Lane lane)
    {
        public DiagnosticRequest Request { get; } = request;

        public Lane Lane { get; } = lane;

        public bool Started { get; set; }

        public TaskCompletionSource<DiagnosticOperationResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class Lane(int pendingCapacity, int retainedCapacity)
    {
        public Queue<Entry> Pending { get; } = new();

        public int PendingCapacity { get; } = pendingCapacity;

        public int RetainedCapacity { get; } = retainedCapacity;

        public int RetainedCount { get; set; }

        public void Clear()
        {
            Pending.Clear();
            RetainedCount = 0;
        }
    }
}
