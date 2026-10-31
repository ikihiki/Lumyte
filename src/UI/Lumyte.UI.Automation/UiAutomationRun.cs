using System.Runtime.ExceptionServices;

namespace Lumyte.UI.Automation;

internal sealed class UiAutomationRun : IDisposable
{
    private readonly Action<UiAutomationSnapshot> _rememberSurface;
    private readonly CancellationTokenSource _deadline;
    private readonly CancellationTokenSource _linked;
    private readonly long _started;
    private readonly TimeSpan _timeout;

    private readonly Queue<string> _trace = new();

    internal UiAutomationRun(IUiAutomationHost host, TimeSpan timeout, CancellationToken caller, Action<UiAutomationSnapshot> rememberSurface)
    {
        Host = host;
        _rememberSurface = rememberSurface;
        _timeout = timeout;
        _started = host.TimeProvider.GetTimestamp();
        _deadline = new(timeout, host.TimeProvider);
        _linked = CancellationTokenSource.CreateLinkedTokenSource(caller, _deadline.Token);
    }

    internal IUiAutomationHost Host { get; }

    internal CancellationToken Token => _linked.Token;

    internal UiAutomationSnapshot? LastSnapshot { get; private set; }

    internal long Frame { get; private set; }

    internal bool Expired => _deadline.IsCancellationRequested || Host.TimeProvider.GetElapsedTime(_started) >= _timeout;

    public void Dispose()
    {
        _linked.Dispose();
        _deadline.Dispose();
    }

    internal void Check()
    {
        Token.ThrowIfCancellationRequested();
        if (Expired)
        {
            throw Failure(UiAutomationFailureKind.Timeout, "The automation deadline expired.");
        }
    }

    internal void Trace(string message)
    {
        if (_trace.Count == 64)
        {
            _trace.Dequeue();
        }

        _trace.Enqueue($"frame={Frame} {message}");
    }

    internal string[] CaptureTrace() => _trace.ToArray();

    internal UiAutomationException Failure(UiAutomationFailureKind kind, string message, Exception? inner = null) => new(kind, message, LastSnapshot, _trace.ToArray(), inner);

    internal async ValueTask<UiAutomationSnapshot> SnapshotAsync()
    {
        Check();
        UiAutomationSnapshot snapshot = await Host.RunOnUiThreadAsync(surface => surface.CaptureAutomationSnapshot(), Token);
        Remember(snapshot);
        return snapshot;
    }

    internal void Remember(UiAutomationSnapshot snapshot)
    {
        _rememberSurface(snapshot);
        if (LastSnapshot is not null && LastSnapshot.SurfaceId != snapshot.SurfaceId)
        {
            throw Failure(UiAutomationFailureKind.HostUnavailable, "The host replaced its surface.");
        }

        LastSnapshot = snapshot;
    }

    internal async ValueTask NextFrameAsync(string reason)
    {
        Trace("wait: " + reason);
        Check();
        await Host.NextFrameAsync(Token);
        Frame++;
        Check();
    }

    internal async ValueTask<T> WithInputAsync<T>(Func<IUiAutomationInputLease, ValueTask<T>> action, Func<CancellationToken, ValueTask<IUiAutomationInputLease>>? acquire = null)
    {
        Check();
        Trace("acquire input lease");
        IUiAutomationInputLease lease = await (acquire is null ? Host.AcquireInputAsync(Token) : acquire(Token));
        if (Host is IUiAutomationRoutingHost routing && lease is not IUiAutomationOwnedRoutingLease)
        {
            lease = new RoutingLease(Host, routing, lease);
        }

        Exception? failure = null;
        T result = default!;
        try
        {
            Check();
            if (lease.PointerId < 0)
            {
                throw Failure(UiAutomationFailureKind.HostUnavailable, "The host returned an invalid pointer identity.");
            }

            result = await action(lease);
        }
        catch (Exception error)
        {
            failure = error;
        }

        try
        {
            await lease.DisposeAsync();
            Trace("input lease cleaned and released");
        }
        catch (Exception cleanup)
        {
            Trace("cleanup failed: " + cleanup.Message);
            failure ??= cleanup;
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return result;
    }

    internal UiPointerProbe Probe(UiSurface surface, System.Numerics.Vector2 position) => Host is IUiAutomationRoutingHost routing ? routing.ProbePointer(surface, position) : surface.ProbePointer(position);

    internal bool CanReceiveKeys(UiSurface surface) => Host is not IUiAutomationRoutingHost routing || routing.CanReceiveKeys(surface);

    internal void Send(IUiAutomationInputLease lease, UiSurface surface, UiInput input)
    {
        Trace("send " + input);
        lease.Dispatch(surface, input);
    }

    private sealed class RoutingLease(IUiAutomationHost host, IUiAutomationRoutingHost routing, IUiAutomationInputLease inner) : IUiAutomationInputLease
    {
        private readonly HashSet<Lumyte.Input.Abstractions.Key> _keys = [];
        private UiSurface? _pointerSurface;
        private bool _disposed;

        public long PointerId => inner.PointerId;

        public bool Dispatch(UiSurface surface, UiInput input)
        {
            if (input is UiKeyInput key)
            {
                if (key.IsDown)
                {
                    _keys.Add(key.Key);
                }
                else
                {
                    _keys.Remove(key.Key);
                }
            }

            if (input is UiPointerInput pointer)
            {
                if (pointer.Phase == UiPointerPhase.Down)
                {
                    _pointerSurface = surface;
                }
            }

            bool result = routing.Dispatch(surface, input);
            if (input is UiKeyInput { IsDown: false } released)
            {
                _keys.Remove(released.Key);
            }

            if (input is UiPointerInput { Phase: UiPointerPhase.Up or UiPointerPhase.Cancel })
            {
                _pointerSurface = null;
            }

            return result;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Exception? failure = null;
            try
            {
                await host.RunOnUiThreadAsync(surface =>
                {
                    var errors = new List<Exception>();
                    foreach (Lumyte.Input.Abstractions.Key key in _keys)
                    {
                        try
                        {
                            routing.Dispatch(surface, new UiKeyCancelInput(key));
                        }
                        catch (Exception error)
                        {
                            errors.Add(error);
                        }
                    }

                    if (_pointerSurface is not null)
                    {
                        try
                        {
                            routing.Dispatch(_pointerSurface, new UiPointerInput(PointerId, UiPointerPhase.Cancel, System.Numerics.Vector2.Zero));
                        }
                        catch (Exception error)
                        {
                            errors.Add(error);
                        }
                    }

                    if (errors.Count != 0)
                    {
                        throw new AggregateException(errors);
                    }

                    return true;
                });
            }
            catch (Exception error)
            {
                failure = error;
            }

            try
            {
                await inner.DisposeAsync();
            }
            catch (Exception error)
            {
                failure = failure is null ? error : new AggregateException(failure, error);
            }

            if (failure is not null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }
    }
}
