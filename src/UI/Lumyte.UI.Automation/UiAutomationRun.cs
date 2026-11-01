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

    internal async ValueTask<T> WithInputAsync<T>(Func<IUiAutomationInputLease, ValueTask<T>> action)
    {
        Check();
        Trace("acquire input lease");
        IUiAutomationInputLease lease = await Host.AcquireInputAsync(Token);
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

    internal void Send(IUiAutomationInputLease lease, UiSurface surface, UiInput input)
    {
        Trace("send " + input);
        lease.Dispatch(surface, input);
    }
}
