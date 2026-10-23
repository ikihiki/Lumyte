namespace Lumyte.Execution;

internal sealed class PendingCommand<TCommand, TResult> : QueuedWork, System.Threading.Tasks.Sources.IValueTaskSource<TResult>
    where TCommand : struct
{
    private readonly CancellationTokenRegistration _cancellation;
    private readonly long _bytes;
    private readonly bool _cleanup;
    private System.Threading.Tasks.Sources.ManualResetValueTaskSourceCore<TResult> _source;
    private ExecutionRuntime? _runtime;
    private WorkDefinition<TCommand, TResult>? _work;
    private TCommand _command;
    private int _observed;
    private bool _handlerCanceledException;

    internal PendingCommand(WorkDefinition<TCommand, TResult> work, in TCommand command, CancellationToken token, bool cleanup)
        : base(work, token)
    {
        _runtime = work.Module.Runtime;
        _work = work;
        _command = command;
        _bytes = work.PayloadBytes;
        _cleanup = cleanup;
        _source.RunContinuationsAsynchronously = true;
        if (work.Owner is null && work.Gates.Length > 0 && token.CanBeCanceled)
        {
            _cancellation = token.UnsafeRegister(static state => ((ExecutionRuntime)state!).NotifyCancellation(), _runtime);
        }
    }

    public TResult GetResult(short token)
    {
        if (token != 0 || _source.GetStatus(0) == System.Threading.Tasks.Sources.ValueTaskSourceStatus.Pending || Interlocked.CompareExchange(ref _observed, 1, 0) != 0)
        {
            throw new InvalidOperationException("Observe a completed command once with token zero.");
        }

        try
        {
            return _source.GetResult(0);
        }
        finally
        {
            _source.Reset();
            _runtime!.Release(1, 1, _bytes, _cleanup, 0);
            _runtime = null;
        }
    }

    public System.Threading.Tasks.Sources.ValueTaskSourceStatus GetStatus(short token)
    {
        if (token != 0 || Volatile.Read(ref _observed) != 0)
        {
            throw new InvalidOperationException("Invalid or already observed command.");
        }

        System.Threading.Tasks.Sources.ValueTaskSourceStatus status = _source.GetStatus(0);
        return _handlerCanceledException && status == System.Threading.Tasks.Sources.ValueTaskSourceStatus.Canceled ? System.Threading.Tasks.Sources.ValueTaskSourceStatus.Faulted : status;
    }

    public void OnCompleted(Action<object?> continuation, object? state, short token, System.Threading.Tasks.Sources.ValueTaskSourceOnCompletedFlags flags)
    {
        if (token != 0 || Volatile.Read(ref _observed) != 0)
        {
            throw new InvalidOperationException("Invalid or already observed command.");
        }

        _source.OnCompleted(continuation, state, 0, flags);
    }

    internal override void Perform(in WorkContext context)
    {
        TResult result;
        try
        {
            result = _work!.Invoke(in _command, in context);
        }
        finally
        {
            _command = default;
            _work = null;
        }

        _source.SetResult(result);
    }

    internal override void Finish(Exception? error)
    {
        _cancellation.Unregister();
        _command = default;
        _work = null;
        if (error is not null)
        {
            _handlerCanceledException = error is OperationCanceledException && !CanceledBeforeStart;
            _source.SetException(error);
        }
    }
}
