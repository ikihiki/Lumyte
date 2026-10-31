using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Navigation;

/// <summary>Serializes preparation, guarded publication and retryable page retirement for one window.</summary>
public sealed class UiNavigator : IUiNavigator
{
    private readonly IUiPageActivationProvider _provider;
    private readonly IUiNavigationHost _host;
    private readonly UiNavigationOptions _options;
    private readonly Action<UiNavigationResult>? _onCompleted;
    private readonly List<UiNavigationEntry> _entries = [];
    private readonly List<Activation> _owned = [];
    private readonly Queue<UiNavigationResult> _notifications = [];
    private readonly Guid _id = Guid.NewGuid();
    private Activation? _active;
    private Work? _work;
    private int _index = -1;
    private UiNavigationPhase _phase;
    private UiNavigationResult? _last;
    private Exception? _retirementFailure;
    private bool _stopping;
    private bool _released;
    private Exception? _terminal;
    private Task? _retire;
    private Task? _release;
    private Task? _stop;

    /// <summary>Initializes a new instance of the <see cref="UiNavigator"/> class.</summary>
    /// <param name="provider">Captures arguments and creates lightweight page leases.</param>
    /// <param name="host">Stages presentation on the same window and owner.</param>
    /// <param name="options">The positive history limit.</param>
    /// <param name="onCompleted">An owner callback invoked after state and result settlement.</param>
    public UiNavigator(IUiPageActivationProvider provider, IUiNavigationHost host, UiNavigationOptions? options = null, Action<UiNavigationResult>? onCompleted = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(host);
        _provider = provider;
        _host = host;
        _options = options ?? new();
        _onCompleted = onCompleted;
        if (_options.MaxHistoryEntries < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }
    }

    /// <summary>Gets a value indicating whether guard and preparation have stopped, independently of GPU and scope release.</summary>
    public bool IsStopped => _stopping && _stop?.IsCompleted == true;

    /// <inheritdoc/>
    public UiNavigationRequest RequestNavigate<TArgs>(UiPageKey<TArgs> key, TArgs arguments, UiNavigationMode mode = UiNavigationMode.Push, CancellationToken cancellationToken = default)
    {
        CheckAdmission();
        UiPageDestination destination = _provider.Capture(key, arguments);
        UiNavigationKind kind = mode switch
        {
            UiNavigationMode.Push => UiNavigationKind.Push,
            UiNavigationMode.Replace => UiNavigationKind.Replace,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
        return Enqueue(new(Guid.NewGuid(), destination), kind, -1, cancellationToken);
    }

    /// <inheritdoc/>
    public UiNavigationRequest RequestNavigate(string route, UiNavigationMode mode = UiNavigationMode.Push, CancellationToken cancellationToken = default) => RequestNavigate(new UiPageKey<UiNoArgs>(route), default, mode, cancellationToken);

    /// <inheritdoc/>
    public UiNavigationRequest RequestBack(CancellationToken cancellationToken = default) => History(-1, UiNavigationKind.Back, cancellationToken);

    /// <inheritdoc/>
    public UiNavigationRequest RequestForward(CancellationToken cancellationToken = default) => History(1, UiNavigationKind.Forward, cancellationToken);

    /// <inheritdoc/>
    public UiNavigationSnapshot CaptureSnapshot()
    {
        CheckOwner();
        return new(_id, _phase, Array.AsReadOnly(_entries.ToArray()), _index, _active?.Context.ActivationId, _index > 0, _index >= 0 && _index < _entries.Count - 1, _last, _retirementFailure);
    }

    /// <summary>Advances one asynchronous stage and dispatches settled callbacks on the owner.</summary>
    public void Advance()
    {
        CheckOwner();
        if (!_stopping && _work is { Cleanup.IsCompleted: true } completed)
        {
            FinishCleanup(completed);
        }

        Work? work = _work;
        int notifications = _notifications.Count;
        for (int i = 0; i < notifications; i++)
        {
            UiNavigationResult notification = _notifications.Dequeue();
            _onCompleted?.Invoke(notification);
        }

        if (work is null || !ReferenceEquals(_work, work))
        {
            return;
        }

        if (_stopping)
        {
            if (IsStopped && !work.Request.Completion.IsCompleted)
            {
                Complete(work.Request, UiNavigationStatus.Canceled, "canceled", null, null);
            }

            return;
        }

        if (work.Cleanup is { } cleanup)
        {
            if (!cleanup.IsCompleted)
            {
                return;
            }

            FinishCleanup(work);

            return;
        }

        if (_terminal is not null)
        {
            _phase = UiNavigationPhase.Faulted;
            return;
        }

        if (work.Pending is { IsCompleted: false })
        {
            return;
        }

        try
        {
            work.Pending?.GetAwaiter().GetResult();
            work.Pending = null;
            work.Cancellation.Token.ThrowIfCancellationRequested();
            switch (work.Stage++)
            {
                case 0:
                    _phase = UiNavigationPhase.Preparing;
                    var lifetime = new CancellationTokenSource();
                    var context = new UiPageActivationContext(_host.Window, work.Entry.Destination, this, _host.Dispatcher, lifetime.Token);
                    work.Candidate = new(context, lifetime);
                    _owned.Add(work.Candidate);
                    work.Candidate.View = _host.BeginChange(context.ActivationId);
                    _ = work.Cancellation.Token.Register(work.Candidate.Cancel);
                    work.Candidate.Page = _provider.Create(context) ?? throw new InvalidOperationException("The activation provider returned no lease.");
                    work.Pending = work.Candidate.Page.PrepareAsync(work.Cancellation.Token).AsTask();
                    break;
                case 1:
                    IUiPageActivation page = work.Candidate!.Page!;
                    if (!page.IsPrepared)
                    {
                        throw new InvalidOperationException("The activation did not finish preparing.");
                    }

                    Widget root = page.Root;
                    if (root.Parent is not null)
                    {
                        throw new InvalidOperationException("A page must supply a disconnected root.");
                    }

                    work.Candidate.Root = root;
                    work.Pending = work.Candidate.View!.PrepareAsync(root, work.Cancellation.Token).AsTask();
                    break;
                case 2:
                    _phase = UiNavigationPhase.AwaitingGuard;
                    if (_active?.Page?.LeaveGuard is { } guard)
                    {
                        work.Guard = guard.CanLeaveAsync(new(_entries[_index], work.Entry.Destination, work.Kind, _host.Dispatcher), work.Cancellation.Token).AsTask();
                        work.Pending = work.Guard;
                    }

                    break;
                case 3:
                    if (work.Guard?.Result is { Allowed: false } decision)
                    {
                        Abort(work, UiNavigationStatus.Rejected, decision.Code);
                        break;
                    }

                    _phase = UiNavigationPhase.Quiescing;
                    work.Pending = work.Candidate!.View!.QuiesceAsync(work.Origin, work.Cancellation.Token).AsTask();
                    break;
                case 4:
                    if (!ReferenceEquals(work.Previous, _active) || !ReferenceEquals(work.Candidate!.Root, work.Candidate.Page!.Root) || !work.Candidate.View!.TryValidateCommit(out string code))
                    {
                        Abort(work, UiNavigationStatus.Failed, "candidate-stale");
                        break;
                    }

                    Commit(work);
                    break;
            }
        }
        catch (OperationCanceledException) when (work.Cancellation.IsCancellationRequested)
        {
            Abort(work, UiNavigationStatus.Canceled, "canceled");
        }
        catch (Exception error)
        {
            if (work.Committed)
            {
                _terminal = error;
                _phase = UiNavigationPhase.Faulted;
                throw;
            }

            Abort(work, UiNavigationStatus.Failed, "navigation-failed", error);
        }
    }

    /// <inheritdoc/>
    public ValueTask RetryRetirementAsync(CancellationToken cancellationToken = default)
    {
        CheckAdmission();
        if (_phase != UiNavigationPhase.RetirementBlocked || _work is not { } work)
        {
            throw new InvalidOperationException("There is no retryable retained retirement.");
        }

        work.Cleanup = CleanupAsync(work, false);
        _phase = UiNavigationPhase.Retiring;
        return new(ObserveRetryAsync(work).WaitAsync(cancellationToken));
    }

    /// <summary>Stops accepting work and cancels preparation and every activation lifetime.</summary>
    public void BeginStop()
    {
        CheckOwner();
        if (_stopping)
        {
            return;
        }

        _stopping = true;
        _phase = UiNavigationPhase.Stopping;
        if (_work is { Committed: false, Cleanup: null } work)
        {
            work.Cancellation.Cancel();
        }

        foreach (Activation activation in _owned)
        {
            activation.Cancel();
        }

        _stop = StopPreparationAsync();
    }

    /// <summary>Retires all pages after preparation stops and current GPU use is drained by the host.</summary>
    /// <param name="cancellationToken">Cancels only the caller wait.</param>
    /// <returns>Retryable retirement completion.</returns>
    public ValueTask RetireAsync(CancellationToken cancellationToken = default)
    {
        CheckOwner();
        if (!IsStopped)
        {
            throw new InvalidOperationException("Stop and drain preparation before retiring pages.");
        }

        if (_retire is null || _retire.IsFaulted)
        {
            _retire = RetireCoreAsync();
        }

        return new(_retire.WaitAsync(cancellationToken));
    }

    /// <summary>Disposes leases once after the host has released its current surface.</summary>
    /// <returns>Terminal final release completion.</returns>
    public ValueTask ReleaseAsync()
    {
        CheckOwner();
        if (_retire?.IsCompletedSuccessfully != true)
        {
            throw new InvalidOperationException("Retire pages and release the slot before final disposal.");
        }

        _release ??= ReleaseCoreAsync();
        return new(_release);
    }

    private void FinishCleanup(Work work)
    {
        Task cleanup = work.Cleanup!;
        if (cleanup.Exception is { } error)
        {
            _retirementFailure = error.InnerException ?? error;
            _phase = _terminal is null ? UiNavigationPhase.RetirementBlocked : UiNavigationPhase.Faulted;
            if (!work.Request.Completion.IsCompleted)
            {
                Complete(work.Request, UiNavigationStatus.Failed, "cleanup-blocked", null, null, _retirementFailure);
            }
        }
        else
        {
            _work = null;
            _retirementFailure = null;
            _phase = UiNavigationPhase.Idle;
            if (!work.Request.Completion.IsCompleted)
            {
                Complete(work.Request, work.FailureStatus, work.FailureCode, null, null, work.Failure);
            }
        }
    }

    private UiNavigationRequest History(int direction, UiNavigationKind kind, CancellationToken token)
    {
        CheckAdmission();
        int index = _index + direction;
        if (_work is not null)
        {
            return Settled(UiNavigationStatus.Busy, "busy");
        }

        return index < 0 || index >= _entries.Count ? Settled(UiNavigationStatus.NoHistory, "no-history") : Enqueue(_entries[index], kind, index, token);
    }

    private UiNavigationRequest Enqueue(UiNavigationEntry entry, UiNavigationKind kind, int index, CancellationToken token)
    {
        if (_work is not null)
        {
            return Settled(UiNavigationStatus.Busy, "busy");
        }

        var request = new UiNavigationRequest();
        _work = new(request, entry, kind, index, _active, _host.CaptureInputOperation(), token);
        return request;
    }

    private UiNavigationRequest Settled(UiNavigationStatus status, string code)
    {
        var request = new UiNavigationRequest();
        Complete(request, status, code, null, null);
        return request;
    }

    private void Complete(UiNavigationRequest request, UiNavigationStatus status, string code, Guid? entry, Guid? activation, Exception? error = null)
    {
        var result = new UiNavigationResult(request.Id, status, code, entry, activation, error);
        _last = result;
        request.Complete(result);
        _notifications.Enqueue(result);
    }

    private void Commit(Work work)
    {
        // Allocate the entire history replacement before crossing the no-fail publication boundary.
        List<UiNavigationEntry> entries = new(_entries);
        int index = _index;
        switch (work.Kind)
        {
            case UiNavigationKind.Push:
                entries.RemoveRange(index + 1, entries.Count - index - 1);
                entries.Add(work.Entry);
                index++;
                int overflow = Math.Max(0, entries.Count - _options.MaxHistoryEntries);
                entries.RemoveRange(0, overflow);
                index -= overflow;
                break;
            case UiNavigationKind.Replace:
                if (index < 0)
                {
                    entries.Add(work.Entry);
                    index = 0;
                }
                else
                {
                    entries[index] = work.Entry;
                }

                break;
            default:
                index = work.Index;
                break;
        }

        var publication = new UiNavigationPublication(_id, work.Request.Id, work.Entry.Id, work.Candidate!.Context.ActivationId, work.Entry.Destination.Route);
        _entries.EnsureCapacity(entries.Count);

        // A custom host violating Commit's contract cannot be rolled back safely.
        work.Committed = true;
        work.Candidate.View!.Commit(publication);
        _entries.Clear();
        _entries.AddRange(entries);
        _index = index;
        _active = work.Candidate;
        work.Cancellation.Dispose();
        _phase = UiNavigationPhase.Retiring;
        Complete(work.Request, UiNavigationStatus.Committed, string.Empty, work.Entry.Id, _active.Context.ActivationId);
        work.Cleanup = CleanupAsync(work, false);
    }

    private void Abort(Work work, UiNavigationStatus status, string code, Exception? error = null)
    {
        work.Candidate?.Cancel();
        work.FailureStatus = status;
        work.FailureCode = code;
        work.Failure = error;
        _phase = UiNavigationPhase.Retiring;
        work.Cleanup = CleanupAsync(work, false);
    }

    private async Task CleanupAsync(Work work, bool stopping)
    {
        Activation? page = work.Committed ? work.Previous : work.Candidate;
        if (page is null)
        {
            if (work.Committed && work.Candidate?.View is { } initial && !work.SurfaceReleased)
            {
                await OnOwnerAsync(() => initial.DrainRetiredAsync(CancellationToken.None));
                await OnOwnerAsync(() => initial.ReleaseRetiredAsync(CancellationToken.None));
                work.SurfaceReleased = true;
            }

            work.Cancellation.Dispose();
            return;
        }

        IUiNavigationViewChange? view = work.Candidate?.View;
        if (!work.Drained && view is not null)
        {
            await OnOwnerAsync(() => work.Committed ? view.DrainRetiredAsync(CancellationToken.None) : view.DrainCandidateAsync(CancellationToken.None));
            work.Drained = true;
        }

        await RetirePageAsync(page);
        if (!work.SurfaceReleased && view is not null)
        {
            await OnOwnerAsync(() => work.Committed ? view.ReleaseRetiredAsync(CancellationToken.None) : view.DiscardAsync(CancellationToken.None));
            work.SurfaceReleased = true;
        }

        if (!stopping)
        {
            await DisposePageAsync(page);
            await DisposeViewAsync(page);
            await OnOwnerActionAsync(() => _owned.Remove(page));
        }

        work.Cancellation.Dispose();
    }

    private async Task RetirePageAsync(Activation activation)
    {
        await OnOwnerActionAsync(activation.Cancel);
        if (!activation.Retired && activation.Page is { } page)
        {
            await OnOwnerAsync(() => activation.View is { } view ? view.RunPageCleanupAsync(page.RetireAsync, CancellationToken.None) : page.RetireAsync(CancellationToken.None));
            activation.Retired = true;
        }
    }

    private async Task DisposePageAsync(Activation activation)
    {
        if (activation.DisposeAttempted || activation.Page is null)
        {
            return;
        }

        try
        {
            await OnOwnerAsync(() =>
            {
                activation.DisposeAttempted = true;
                return activation.Page.DisposeAsync();
            });
        }
        catch (Exception error)
        {
            _terminal = error;
            throw;
        }
    }

    private async Task DisposeViewAsync(Activation activation)
    {
        if (!activation.ViewDisposed && activation.View is { } view)
        {
            await OnOwnerAsync(view.DisposeAsync);
            activation.ViewDisposed = true;
        }

        activation.Lifetime.Dispose();
    }

    private async Task ObserveRetryAsync(Work work)
    {
        await work.Cleanup!;
        await _host.Dispatcher.InvokeAsync(Advance);
    }

    private async Task StopPreparationAsync()
    {
        if (_work is { } work)
        {
            try
            {
                if (work.Pending is { } pending)
                {
                    await pending;
                }

                if (work.Cleanup is { } cleanup)
                {
                    await cleanup;
                }
            }
            catch (Exception)
            {
                // The stopped host retains the lease and retries cleanup from RetireAsync.
            }

            if (!work.Request.Completion.IsCompleted)
            {
                await OnOwnerActionAsync(() => Complete(work.Request, UiNavigationStatus.Canceled, "canceled", null, null));
            }

            await OnOwnerActionAsync(Advance);
        }
    }

    private async Task RetireCoreAsync()
    {
        _terminal ??= _stop?.Exception?.InnerException;
        if (_work is { } work)
        {
            if (work.Pending is { } pending)
            {
                try
                {
                    await pending;
                }
                catch (Exception)
                {
                    // Preparation failure is settled as a canceled stop request; the lease remains owned.
                }
            }

            if (!work.Request.Completion.IsCompleted)
            {
                Complete(work.Request, UiNavigationStatus.Canceled, "canceled", null, null);
            }

            await CleanupAsync(work, true);
        }

        foreach (Activation activation in _owned)
        {
            await RetirePageAsync(activation);
        }
    }

    private async Task ReleaseCoreAsync()
    {
        _released = true;
        try
        {
            foreach (Activation activation in _owned)
            {
                await DisposePageAsync(activation);
                await DisposeViewAsync(activation);
            }

            await OnOwnerActionAsync(() =>
            {
                _owned.Clear();
                _active = null;
                _work = null;
                _phase = UiNavigationPhase.Stopped;
            });
            if (_terminal is { } terminal)
            {
                throw terminal;
            }
        }
        catch (Exception error)
        {
            _terminal = error;
            _phase = UiNavigationPhase.Faulted;
            throw;
        }
    }

    private ValueTask OnOwnerActionAsync(Action action)
    {
        if (_host.Dispatcher.CheckAccess())
        {
            action();
            return ValueTask.CompletedTask;
        }

        return _host.Dispatcher.InvokeAsync(action);
    }

    private async ValueTask OnOwnerAsync(Func<ValueTask> action)
    {
        Task task = _host.Dispatcher.CheckAccess() ? action().AsTask() : await _host.Dispatcher.InvokeAsync(() => action().AsTask());
        await task;
    }

    private void CheckAdmission()
    {
        CheckOwner();
        if (_stopping || _released || _terminal is not null)
        {
            throw new InvalidOperationException("Navigation has stopped or faulted.");
        }
    }

    private void CheckOwner()
    {
        if (!_host.Dispatcher.CheckAccess())
        {
            throw new InvalidOperationException("Use the window's dispatcher to access navigation.");
        }
    }

    private sealed class Activation(UiPageActivationContext context, CancellationTokenSource lifetime)
    {
        internal UiPageActivationContext Context { get; } = context;

        internal CancellationTokenSource Lifetime { get; } = lifetime;

        internal IUiPageActivation? Page { get; set; }

        internal IUiNavigationViewChange? View { get; set; }

        internal Widget? Root { get; set; }

        internal bool Retired { get; set; }

        internal bool DisposeAttempted { get; set; }

        internal bool ViewDisposed { get; set; }

        internal void Cancel()
        {
            if (!ViewDisposed)
            {
                Lifetime.Cancel();
            }
        }
    }

    private sealed class Work(UiNavigationRequest request, UiNavigationEntry entry, UiNavigationKind kind, int index, Activation? previous, Guid? origin, CancellationToken token)
    {
        internal UiNavigationRequest Request { get; } = request;

        internal UiNavigationEntry Entry { get; } = entry;

        internal UiNavigationKind Kind { get; } = kind;

        internal int Index { get; } = index;

        internal Activation? Previous { get; } = previous;

        internal Guid? Origin { get; } = origin;

        internal CancellationTokenSource Cancellation { get; } = CancellationTokenSource.CreateLinkedTokenSource(request.Cancellation, token);

        internal Activation? Candidate { get; set; }

        internal Task? Pending { get; set; }

        internal Task<UiPageLeaveDecision>? Guard { get; set; }

        internal Task? Cleanup { get; set; }

        internal int Stage { get; set; }

        internal bool Committed { get; set; }

        internal bool Drained { get; set; }

        internal bool SurfaceReleased { get; set; }

        internal UiNavigationStatus FailureStatus { get; set; }

        internal string FailureCode { get; set; } = string.Empty;

        internal Exception? Failure { get; set; }
    }
}
