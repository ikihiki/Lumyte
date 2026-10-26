using System.Threading.Tasks.Sources;

namespace Lumyte.Resources;

// A single consumer observes each wait before starting another one. Signals do not accumulate;
// register the wait before rechecking the condition that the producer publishes.
internal sealed class ResourceStreamSignal : IValueTaskSource
{
    private readonly object _gate = new();
    private ManualResetValueTaskSourceCore<bool> _completion = new() { RunContinuationsAsynchronously = true };
    private CancellationTokenRegistration _cancellation;
    private CancellationToken _token;
    private bool _waiting;

    public void GetResult(short token)
    {
        // Reject premature/stale observation before disposing the current wait's registration.
        if (_completion.GetStatus(token) == ValueTaskSourceStatus.Pending)
        {
            throw new InvalidOperationException("The stream notification has not completed.");
        }

        try
        {
            _completion.GetResult(token);
        }
        finally
        {
            // Do not hold _gate while joining an in-flight cancellation callback. Once this
            // returns, that callback cannot complete the source after the next Reset.
            _cancellation.Dispose();
            lock (_gate)
            {
                _cancellation = default;
                _token = default;

                // Release the completed consumer's continuation and destination immediately.
                // Synchronize with Signal/Cancel finishing SetResult/SetException before reuse.
                _completion.Reset();
            }
        }
    }

    public ValueTaskSourceStatus GetStatus(short token) => _completion.GetStatus(token);

    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        => _completion.OnCompleted(continuation, state, token, flags);

    internal ValueTask WaitAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _token = cancellationToken;
            _waiting = true;
            _cancellation = cancellationToken.UnsafeRegister(static state => ((ResourceStreamSignal)state!).Cancel(), this);
            return new ValueTask(this, _completion.Version);
        }
    }

    internal void Signal()
    {
        if (!Volatile.Read(ref _waiting))
        {
            return;
        }

        lock (_gate)
        {
            if (_waiting)
            {
                _waiting = false;
                _completion.SetResult(true);
            }
        }
    }

    private void Cancel()
    {
        lock (_gate)
        {
            if (_waiting)
            {
                _waiting = false;
                _completion.SetException(new OperationCanceledException(_token));
            }
        }
    }
}
