using System.Threading.Tasks.Sources;

namespace Lumyte.Resources;

// One producer waits at a time. Reuse the scheduling state instead of letting the
// value-task source allocate a callback tuple for every synchronization-context post.
internal sealed class ResourceStreamProducerSignal : IValueTaskSource
{
    private ManualResetValueTaskSourceCore<bool> _completion;
    private SynchronizationContext? _context;
    private Action<object?>? _continuation;
    private object? _state;
    private int _waiting;

    public void GetResult(short token) => _completion.GetResult(token);

    public ValueTaskSourceStatus GetStatus(short token) => _completion.GetStatus(token);

    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
    {
        SynchronizationContext? context = SynchronizationContext.Current;
        if ((flags & ValueTaskSourceOnCompletedFlags.UseSchedulingContext) != 0
            && (flags & ValueTaskSourceOnCompletedFlags.FlowExecutionContext) == 0
            && context is not null && context.GetType() != typeof(SynchronizationContext))
        {
            _context = context;
            _continuation = continuation;
            _state = state;
            _completion.RunContinuationsAsynchronously = false;
            _completion.OnCompleted(
                static source =>
                {
                    var signal = (ResourceStreamProducerSignal)source!;
                    signal._context!.Post(static posted => ((ResourceStreamProducerSignal)posted!).Resume(), signal);
                },
                this,
                token,
                flags & ~ValueTaskSourceOnCompletedFlags.UseSchedulingContext);
        }
        else
        {
            // Preserve task-scheduler and execution-context semantics for custom executors.
            _completion.OnCompleted(continuation, state, token, flags);
        }
    }

    internal ValueTask WaitAsync()
    {
        _completion.Reset();
        _completion.RunContinuationsAsynchronously = true;
        Volatile.Write(ref _waiting, 1);
        return new ValueTask(this, _completion.Version);
    }

    internal void Signal()
    {
        if (Interlocked.Exchange(ref _waiting, 0) != 0)
        {
            _completion.SetResult(true);
        }
    }

    private void Resume()
    {
        Action<object?> continuation = _continuation!;
        object? state = _state;
        _context = null;
        _continuation = null;
        _state = null;
        continuation(state);
    }
}
