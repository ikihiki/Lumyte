using Lumyte.UI.Windowing;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Navigation.Benchmark;

/// <summary>Finite owner-advanced navigation fixture with real surfaces and typed snapshots.</summary>
public sealed class NavigationSession : IDisposable
{
    private readonly Provider _provider = new();
    private readonly Host _host = new();
    private readonly Lumyte.UI.Navigation.UiNavigator _navigator;
    private readonly UiPageKey<int[]> _key = new("/page");
    private int _sequence;

    /// <summary>Initializes a new instance of the <see cref="NavigationSession"/> class.</summary>
    public NavigationSession()
    {
        _navigator = new(_provider, _host, new() { MaxHistoryEntries = 4 });
        Navigate(UiNavigationMode.Push);
        Navigate(UiNavigationMode.Push);
    }

    /// <summary>Gets completed activation disposal count.</summary>
    public int Disposals => _provider.Disposals;

    /// <summary>Gets a detached current history snapshot.</summary>
    public UiNavigationSnapshot Snapshot => _navigator.CaptureSnapshot();

    /// <summary>Publishes a meaningful width change and verifies argument copies.</summary>
    /// <param name="mode">The actual history mutation mode.</param>
    /// <returns>The retained current argument.</returns>
    public int Navigate(UiNavigationMode mode)
    {
        int value = ++_sequence;
        int[] original = [value];
        UiNavigationRequest request = _navigator.RequestNavigate(_key, original, mode);
        original[0] = -1;
        Complete(request, UiNavigationStatus.Committed);
        UiNavigationSnapshot snapshot = Snapshot;
        int[] copy = snapshot.Entries[snapshot.CurrentIndex].Destination.GetArguments<int[]>();
        Require(copy[0] == value);
        copy[0] = -2;
        Require(snapshot.Entries[snapshot.CurrentIndex].Destination.GetArguments<int[]>()[0] == value);
        Require(_provider.Created - _provider.Disposals == 1 && _host.LiveSurfaces == 1);
        return value;
    }

    /// <summary>Recreates both sides of a history pair.</summary>
    /// <returns>The activation count after the pair.</returns>
    public int BackForward()
    {
        Guid? before = Snapshot.ActivationId;
        Complete(_navigator.RequestBack(), UiNavigationStatus.Committed);
        Complete(_navigator.RequestForward(), UiNavigationStatus.Committed);
        Require(Snapshot.ActivationId != before);
        return _provider.Created;
    }

    /// <summary>Checks rejected candidates preserve publication and history.</summary>
    /// <returns>The rejected status.</returns>
    public UiNavigationStatus Reject()
    {
        Guid? before = Snapshot.ActivationId;
        _provider.Allow = false;
        UiNavigationRequest request = _navigator.RequestNavigate(_key, [++_sequence]);
        Complete(request, UiNavigationStatus.Rejected);
        _provider.Allow = true;
        Require(Snapshot.ActivationId == before && _host.LiveSurfaces == 1);
        return request.Completion.Result.Status;
    }

    /// <summary>Checks pre-prepare cancellation cannot publish.</summary>
    /// <returns>The canceled status.</returns>
    public UiNavigationStatus Cancel()
    {
        Guid? before = Snapshot.ActivationId;
        UiNavigationRequest request = _navigator.RequestNavigate(_key, [++_sequence]);
        request.Cancel();
        Complete(request, UiNavigationStatus.Canceled);
        Require(Snapshot.ActivationId == before);
        return request.Completion.Result.Status;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _navigator.BeginStop();
        _navigator.RetireAsync().GetAwaiter().GetResult();
        _navigator.ReleaseAsync().GetAwaiter().GetResult();
        Require(_provider.Created == _provider.Disposals && _host.LiveSurfaces == 0);
    }

    private static void Require(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Navigation lifecycle oracle failed.");
        }
    }

    private void Complete(UiNavigationRequest request, UiNavigationStatus expected)
    {
        for (int i = 0; i < 64; i++)
        {
            _navigator.Advance();
            if (request.Completion.IsCompleted && Snapshot.Phase == UiNavigationPhase.Idle)
            {
                Require(request.Completion.Result.Status == expected && request.VisualCompletion.IsCompleted);
                return;
            }
        }

        throw new InvalidOperationException("Navigation failed to settle within bounded owner stages.");
    }

    private sealed class Provider : IUiPageActivationProvider, IUiPageLeaveGuard
    {
        internal bool Allow { get; set; } = true;

        internal int Created { get; private set; }

        internal int Disposals { get; private set; }

        public UiPageDestination Capture<TArgs>(UiPageKey<TArgs> key, TArgs arguments) => UiPageDestination.Capture(key, arguments, static value => value is int[] array ? (TArgs)(object)array.ToArray() : value);

        public IUiPageActivation Create(UiPageActivationContext context)
        {
            Created++;
            return new Activation(this, context.Destination.GetArguments<int[]>()[0]);
        }

        public ValueTask<UiPageLeaveDecision> CanLeaveAsync(UiPageLeaveContext context, CancellationToken cancellationToken) => new(new UiPageLeaveDecision(Allow, Allow ? string.Empty : "unsaved"));

        private sealed class Activation(Provider provider, int value) : IUiPageActivation
        {
            public bool IsPrepared { get; private set; }

            public Widget Root { get; } = Panel(width: 40 + (value % 17), height: 40)[Button(width: 20, height: 20)];

            public IUiPageLeaveGuard? LeaveGuard => provider;

            public ValueTask PrepareAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IsPrepared = true;
                return ValueTask.CompletedTask;
            }

            public ValueTask RetireAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

            public ValueTask DisposeAsync()
            {
                provider.Disposals++;
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class Host : IUiNavigationHost
    {
        private UiSurface? _current;

        public UiWindowId Window { get; } = new(Guid.NewGuid(), 1);

        public IUiDispatcher Dispatcher { get; } = new OwnerDispatcher();

        internal int LiveSurfaces { get; private set; }

        public Guid? CaptureInputOperation() => null;

        public IUiNavigationViewChange BeginChange(Guid activationId) => new Change(this);

        private sealed class Change(Host host) : IUiNavigationViewChange
        {
            private UiSurface? _candidate;
            private UiSurface? _retired;
            private bool _committed;

            public ValueTask PrepareAsync(Widget root, CancellationToken cancellationToken)
            {
                _candidate = new(root);
                _candidate.UpdateLayout();
                host.LiveSurfaces++;
                return ValueTask.CompletedTask;
            }

            public ValueTask QuiesceAsync(Guid? originatingInputOperation, CancellationToken cancellationToken) => ValueTask.CompletedTask;

            public bool TryValidateCommit(out string code)
            {
                code = string.Empty;
                return _candidate is not null;
            }

            public void Commit(UiNavigationPublication publication)
            {
                _retired = host._current;
                host._current = _candidate;
                _committed = true;
            }

            public ValueTask DrainRetiredAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

            public ValueTask ReleaseRetiredAsync(CancellationToken cancellationToken)
            {
                Release(ref _retired);
                return ValueTask.CompletedTask;
            }

            public ValueTask DrainCandidateAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

            public ValueTask RunPageCleanupAsync(Func<CancellationToken, ValueTask> cleanup, CancellationToken cancellationToken) => cleanup(cancellationToken);

            public ValueTask DiscardAsync(CancellationToken cancellationToken)
            {
                if (!_committed)
                {
                    Release(ref _candidate);
                }

                return ValueTask.CompletedTask;
            }

            public ValueTask DisposeAsync()
            {
                if (_committed && ReferenceEquals(host._current, _candidate))
                {
                    host._current = null;
                    Release(ref _candidate);
                }

                return ValueTask.CompletedTask;
            }

            private void Release(ref UiSurface? surface)
            {
                if (surface is not null)
                {
                    surface.Dispose();
                    surface = null;
                    host.LiveSurfaces--;
                }
            }
        }
    }

    private sealed class OwnerDispatcher : IUiDispatcher
    {
        private readonly int _ownerThread = Environment.CurrentManagedThreadId;

        public bool CheckAccess() => Environment.CurrentManagedThreadId == _ownerThread;

        public ValueTask InvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
            return ValueTask.CompletedTask;
        }

        public ValueTask<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(action());
        }
    }
}
