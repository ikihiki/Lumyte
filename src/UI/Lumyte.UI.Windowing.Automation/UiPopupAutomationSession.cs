using System.Numerics;
using Lumyte.UI.Automation;

namespace Lumyte.UI.Windowing.Automation;

/// <summary>Creates routed automation hosts and explicit popup pages above the same physical input domain.</summary>
public sealed class UiPopupAutomationSession : IAsyncDisposable
{
    private readonly UiPopupManager _manager;

    private readonly IUiAutomationHost _dispatcher;

    private readonly Func<UiSurface, IUiAutomationHost> _factory;

    private readonly Dictionary<Guid, Host> _popups = [];

    private readonly List<Host> _owners = [];

    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="UiPopupAutomationSession"/> class.</summary>
    /// <param name="manager">The borrowed popup router.</param>
    /// <param name="dispatcherHost">A host dispatching onto the manager's UI owner.</param>
    /// <param name="hostFactory">Creates borrowed hosts for exactly each popup surface, sharing physical input exclusion.</param>
    public UiPopupAutomationSession(UiPopupManager manager, IUiAutomationHost dispatcherHost, Func<UiSurface, IUiAutomationHost> hostFactory)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(dispatcherHost);
        ArgumentNullException.ThrowIfNull(hostFactory);
        _manager = manager;
        _dispatcher = dispatcherHost;
        _factory = hostFactory;
    }

    /// <summary>Lists detached popup identities on the UI owner.</summary>
    /// <param name="cancellationToken">Cancels dispatch.</param>
    /// <returns>Current popup records.</returns>
    public async Task<IReadOnlyList<UiPopupInfo>> ListPopupsAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return await _dispatcher.RunOnUiThreadAsync(
            _ =>
        {
            Prune();
            return _manager.CapturePopups();
        },
            cancellationToken);
    }

    /// <summary>Creates a routed owner host; use it for both local pages and Diagnostics registration.</summary>
    /// <param name="surface">A registered owner surface, borrowed on its UI thread.</param>
    /// <returns>A borrowed routing host retained until session disposal.</returns>
    public IUiAutomationHost CreateOwnerHost(UiSurface surface)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(surface);
        if (_manager.FindWindow(surface) is null)
        {
            throw new ArgumentException("Unknown owner surface.", nameof(surface));
        }

        Host host = Wrap(surface, _factory(surface));
        _owners.Add(host);
        return host;
    }

    /// <summary>Gets a routed host and binds its cancellation/drain to the explicit open popup.</summary>
    /// <param name="popupId">The current popup identity.</param>
    /// <returns>A host suitable for local pages and Diagnostics registration.</returns>
    public IUiAutomationHost PopupHost(Guid popupId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Prune();
        if (_popups.TryGetValue(popupId, out Host? existing))
        {
            return existing;
        }

        if (!_manager.TryGetSurface(popupId, out UiSurface? surface))
        {
            throw new InvalidOperationException("popup-unavailable");
        }

        IUiAutomationHost borrowed = _factory(surface!);
        Host host = Wrap(surface!, borrowed);
        _manager.BindLifetime(popupId, host);
        _popups.Add(popupId, host);
        return host;
    }

    /// <summary>Creates a page for an explicitly selected open popup; it is never searched as a normal child.</summary>
    /// <param name="popupId">The current popup identity.</param>
    /// <returns>A routed automation page.</returns>
    public UiAutomationPage Popup(Guid popupId) => new(new DeferredHost(this, popupId));

    /// <summary>Cancels and drains this session's adapters, without disposing borrowed hosts or the manager.</summary>
    /// <returns>Completion after pending input cleanup.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Task[] tasks = await _dispatcher.RunOnUiThreadAsync(_ => _popups.Values.Concat(_owners).Select(h => h.DisposeAsync().AsTask()).ToArray());
        await Task.WhenAll(tasks);
        _popups.Clear();
        _owners.Clear();
    }

    private void Prune()
    {
        foreach (Guid id in _popups.Where(pair => pair.Value.IsReleased).Select(pair => pair.Key).ToArray())
        {
            _popups.Remove(id);
        }
    }

    private Host Wrap(UiSurface surface, IUiAutomationHost host)
    {
        ValueTask<bool> check = host.RunOnUiThreadAsync(candidate => ReferenceEquals(candidate, surface));
        if (!check.IsCompletedSuccessfully || !check.Result)
        {
            throw new ArgumentException("Host factory must synchronously borrow the exact surface on its owner.");
        }

        return host is IUiAutomationScreenshotHost screenshot ? new ScreenshotHost(_manager, surface, host, screenshot) : new Host(_manager, surface, host);
    }

    private class Host : IUiAutomationHost, IUiAutomationRoutingHost, IAsyncDisposable
    {
        private readonly UiPopupManager _manager;

        private readonly CancellationTokenSource _stop = new();

        private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private UiSurface? _surface;

        private IUiAutomationHost? _inner;

        private int _calls;

        private int _leases;

        private int _closing;

        internal Host(UiPopupManager manager, UiSurface surface, IUiAutomationHost inner)
        {
            _manager = manager;
            _surface = surface;
            _inner = inner;
            TimeProvider = inner.TimeProvider;
        }

        public TimeProvider TimeProvider { get; }

        internal bool IsReleased => _drained.Task.IsCompleted;

        public async ValueTask<T> RunOnUiThreadAsync<T>(Func<UiSurface, T> action, CancellationToken cancellationToken = default)
        {
            bool cleanup = Volatile.Read(ref _closing) != 0 && !cancellationToken.CanBeCanceled && Volatile.Read(ref _leases) > 0;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cleanup ? CancellationToken.None : _stop.Token);
            Interlocked.Increment(ref _calls);
            try
            {
                linked.Token.ThrowIfCancellationRequested();
                IUiAutomationHost inner = _inner ?? throw new ObjectDisposedException(nameof(Host));
                UiSurface surface = _surface ?? throw new ObjectDisposedException(nameof(Host));
                return await inner.RunOnUiThreadAsync(candidate => ReferenceEquals(candidate, surface) ? action(candidate) : throw new InvalidOperationException("popup-unavailable"), linked.Token);
            }
            finally
            {
                Interlocked.Decrement(ref _calls);
                Complete();
            }
        }

        public async ValueTask NextFrameAsync(CancellationToken cancellationToken = default)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
            Interlocked.Increment(ref _calls);
            try
            {
                await (_inner ?? throw new ObjectDisposedException(nameof(Host))).NextFrameAsync(linked.Token);
            }
            finally
            {
                Interlocked.Decrement(ref _calls);
                Complete();
            }
        }

        public async ValueTask<IUiAutomationInputLease> AcquireInputAsync(CancellationToken cancellationToken = default)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
            Interlocked.Increment(ref _calls);
            try
            {
                IUiAutomationInputLease lease = await (_inner ?? throw new ObjectDisposedException(nameof(Host))).AcquireInputAsync(linked.Token);
                Interlocked.Increment(ref _leases);
                return new Lease(this, lease);
            }
            finally
            {
                Interlocked.Decrement(ref _calls);
                Complete();
            }
        }

        public UiPointerProbe ProbePointer(UiSurface candidate, Vector2 position) => ReferenceEquals(candidate, _surface) ? _manager.ProbePointer(candidate, position) : default;

        public bool CanReceiveKeys(UiSurface candidate) => ReferenceEquals(candidate, _surface) && _manager.CanReceiveKeys(candidate);

        public bool Dispatch(UiSurface candidate, UiInput input)
        {
            if (!ReferenceEquals(candidate, _surface))
            {
                throw new ArgumentException("Wrong surface.", nameof(candidate));
            }

            return _manager.Dispatch(candidate, input);
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _closing, 1) == 0)
            {
                _stop.Cancel();
            }

            Complete();
            return new(_drained.Task);
        }

        protected async ValueTask<byte[]> CaptureAsync(IUiAutomationScreenshotHost screenshot, CancellationToken token)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token);
            Interlocked.Increment(ref _calls);
            try
            {
                linked.Token.ThrowIfCancellationRequested();
                return await screenshot.CapturePngAsync(linked.Token);
            }
            finally
            {
                Interlocked.Decrement(ref _calls);
                Complete();
            }
        }

        protected virtual void ReleaseReferences()
        {
        }

        private void Complete()
        {
            if (Volatile.Read(ref _closing) != 0 && Volatile.Read(ref _calls) == 0 && Volatile.Read(ref _leases) == 0)
            {
                _surface = null;
                _inner = null;
                ReleaseReferences();
                _drained.TrySetResult();
            }
        }

        private sealed class Lease(Host owner, IUiAutomationInputLease inner) : IUiAutomationInputLease
        {
            private bool _released;

            public long PointerId => inner.PointerId;

            public bool Dispatch(UiSurface surface, UiInput input) => owner.Dispatch(surface, input);

            public async ValueTask DisposeAsync()
            {
                if (_released)
                {
                    return;
                }

                _released = true;
                try
                {
                    await inner.DisposeAsync();
                }
                finally
                {
                    Interlocked.Decrement(ref owner._leases);
                    owner.Complete();
                }
            }
        }
    }

    private sealed class ScreenshotHost : Host, IUiAutomationScreenshotHost
    {
        private IUiAutomationScreenshotHost? _screenshot;

        internal ScreenshotHost(UiPopupManager manager, UiSurface surface, IUiAutomationHost inner, IUiAutomationScreenshotHost screenshot)
            : base(manager, surface, inner)
        {
            _screenshot = screenshot;
        }

        public ValueTask<byte[]> CapturePngAsync(CancellationToken cancellationToken = default) => CaptureAsync(_screenshot ?? throw new ObjectDisposedException(nameof(ScreenshotHost)), cancellationToken);

        protected override void ReleaseReferences() => _screenshot = null;
    }

    private sealed class DeferredHost(UiPopupAutomationSession session, Guid popupId) : IUiAutomationHost, IUiAutomationRoutingHost, IUiAutomationScreenshotHost
    {
        private IUiAutomationHost? _resolved;

        public TimeProvider TimeProvider => session._dispatcher.TimeProvider;

        public async ValueTask<T> RunOnUiThreadAsync<T>(Func<UiSurface, T> action, CancellationToken cancellationToken = default) => await (await ResolveAsync(cancellationToken)).RunOnUiThreadAsync(action, cancellationToken);

        public async ValueTask NextFrameAsync(CancellationToken cancellationToken = default) => await (await ResolveAsync(cancellationToken)).NextFrameAsync(cancellationToken);

        public async ValueTask<IUiAutomationInputLease> AcquireInputAsync(CancellationToken cancellationToken = default) => await (await ResolveAsync(cancellationToken)).AcquireInputAsync(cancellationToken);

        public UiPointerProbe ProbePointer(UiSurface surface, Vector2 position) => ((IUiAutomationRoutingHost)_resolved!).ProbePointer(surface, position);

        public bool CanReceiveKeys(UiSurface surface) => ((IUiAutomationRoutingHost)_resolved!).CanReceiveKeys(surface);

        public bool Dispatch(UiSurface surface, UiInput input) => ((IUiAutomationRoutingHost)_resolved!).Dispatch(surface, input);

        public async ValueTask<byte[]> CapturePngAsync(CancellationToken cancellationToken = default)
        {
            IUiAutomationHost host = await ResolveAsync(cancellationToken);
            return host is IUiAutomationScreenshotHost screenshot ? await screenshot.CapturePngAsync(cancellationToken) : throw new NotSupportedException("Screenshot is not supported by this popup host.");
        }

        private async ValueTask<IUiAutomationHost> ResolveAsync(CancellationToken token)
        {
            // Initial creation and factory validation run on the dispatcher owner, never on the caller thread.
            if (_resolved is null)
            {
                _resolved = await session._dispatcher.RunOnUiThreadAsync(_ => session.PopupHost(popupId), token);
            }

            return _resolved;
        }
    }
}
