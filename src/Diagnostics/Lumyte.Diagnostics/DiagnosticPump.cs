using System.Diagnostics;

namespace Lumyte.Diagnostics;

internal sealed class DiagnosticPump<TPoint>(IServiceProvider services, IEnumerable<DiagnosticRegistration> registrations, TimeProvider clock) : IDiagnosticPump<TPoint>, IDisposable
    where TPoint : class
{
    private readonly IServiceProvider _services = services;
    private readonly DiagnosticRegistration[] _registrations = registrations.Where(item => item.Point == typeof(TPoint)).ToArray();
    private readonly TimeProvider _clock = clock;
    private readonly object _gate = new();
    private readonly Queue<Entry> _pending = new();
    private readonly Dictionary<(Guid Session, Guid Request), Entry> _requests = [];
    private Dictionary<string, DiagnosticOperationSet> _sets = [];
    private int? _owner;
    private bool _active;
    private bool _busy;
    private bool _disposed;

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
            foreach (DiagnosticRegistration registration in _registrations)
            {
                sets.Add(registration.Descriptor.Id, new(registration.Resolve(_services)));
            }

            _sets = sets;
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

            foreach ((Guid Session, Guid Request) key in _requests.Where(pair => pair.Value.Request.DeadlineTimestamp <= now && pair.Value.Completion.Task.IsCompleted).Select(pair => pair.Key).ToArray())
            {
                _requests.Remove(key);
            }

            (Guid SessionId, Guid RequestId) id = (request.Context.SessionId, request.Context.RequestId);
            if (_requests.TryGetValue(id, out Entry? existing))
            {
                return Same(existing.Request, request) ? existing.Completion.Task
                    : Task.FromResult(DiagnosticOperationResult.Reject("request-id-conflict", "Request ID was reused with different content."));
            }

            if (_pending.Count >= 256 || _requests.Count >= 1024)
            {
                return Task.FromResult(DiagnosticOperationResult.Reject("busy", "Diagnostic queue is full."));
            }

            DiagnosticRequest owned = request with
            {
                Arguments = new Dictionary<string, DiagnosticValue>(request.Arguments, StringComparer.Ordinal),
                Permissions = new HashSet<DiagnosticPermission>(request.Permissions),
            };
            var entry = new Entry(owned);
            _requests.Add(id, entry);
            _pending.Enqueue(entry);
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
                    if (!_active || !_pending.TryDequeue(out entry!))
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

    private void Clear()
    {
        _active = false;
        foreach (Entry entry in _pending)
        {
            entry.Completion.TrySetResult(DiagnosticOperationResult.Reject("target-gone", "Execution point was stopped."));
        }

        _pending.Clear();
        _requests.Clear();
        _sets.Clear();
    }

    private sealed class Entry(DiagnosticRequest request)
    {
        public DiagnosticRequest Request { get; } = request;

        public TaskCompletionSource<DiagnosticOperationResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
