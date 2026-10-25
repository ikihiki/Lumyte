using System.Diagnostics;
using System.Threading.Tasks.Sources;

namespace Lumyte.Execution;

internal sealed class PendingWorkflow<TCommand, TResult> : QueuedWork, IValueTaskSource<TResult>
    where TCommand : struct
{
    private readonly long _bytes;
    private readonly WorkflowContext _context;
    private readonly Action _onComplete;
    private Queue<(SendOrPostCallback Callback, object? State)>? _callbacks;
    private ManualResetValueTaskSourceCore<TResult> _source;
    private ExecutionRuntime? _runtime;
    private WorkflowDefinition<TCommand, TResult>? _work;
    private ValueTask<TResult> _operation;
    private TCommand _command;
    private TResult? _result;
    private long _sliceStarted;
    private int _observed;
    private bool _started;
    private bool _queued = true;
    private bool _running;
    private bool _completed;
    private bool _terminal;
    private bool _completionSignaled;
    private bool _handlerCanceledException;

    internal PendingWorkflow(WorkflowDefinition<TCommand, TResult> work, in TCommand command, CancellationToken token)
        : base(work, token)
    {
        _runtime = work.Module.Runtime;
        _work = work;
        _command = command;
        _bytes = work.PayloadBytes;
        _context = new WorkflowContext(this);
        _onComplete = OnOperationCompleted;
        _source.RunContinuationsAsynchronously = true;
    }

    internal override bool IsCanceled => !_started && base.IsCanceled;

    internal override bool CancelOnShutdown => !_started && base.CancelOnShutdown;

    internal override bool IsWorkflow => true;

    public TResult GetResult(short token)
    {
        if (token != 0 || _source.GetStatus(0) == ValueTaskSourceStatus.Pending || Interlocked.CompareExchange(ref _observed, 1, 0) != 0)
        {
            throw new InvalidOperationException("Observe a completed workflow once with token zero.");
        }

        try
        {
            return _source.GetResult(0);
        }
        finally
        {
            _source.Reset();
            _runtime!.Release(1, 1, _bytes, false, 0);
            _runtime = null;
        }
    }

    public ValueTaskSourceStatus GetStatus(short token)
    {
        if (token != 0 || Volatile.Read(ref _observed) != 0)
        {
            throw new InvalidOperationException("Invalid or already observed workflow.");
        }

        ValueTaskSourceStatus status = _source.GetStatus(0);
        return _handlerCanceledException && status == ValueTaskSourceStatus.Canceled ? ValueTaskSourceStatus.Faulted : status;
    }

    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
    {
        if (token != 0 || Volatile.Read(ref _observed) != 0)
        {
            throw new InvalidOperationException("Invalid or already observed workflow.");
        }

        WorkflowContinuation.Prepare(_runtime!, ref continuation, ref state, ref flags);
        _source.OnCompleted(continuation, state, 0, flags);
    }

    internal override void Perform(in WorkContext context)
    {
        bool enter;
        (SendOrPostCallback Callback, object? State) callback = default;
        lock (_runtime!.Sync)
        {
            _queued = false;
            _running = true;
            enter = !_started;
            _started = true;
            if (!enter && _callbacks is { Count: > 0 })
            {
                callback = _callbacks.Dequeue();
            }
        }

        SynchronizationContext? previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(_context);
        _sliceStarted = Stopwatch.GetTimestamp();
        try
        {
            if (enter)
            {
                try
                {
                    _operation = _work!.Invoke(_command, Token);
                }
                finally
                {
                    _work = null;
                    _command = default;
                }

                if (!_operation.IsCompleted)
                {
                    // Observe completion without capturing the workflow context a second time.
                    _operation.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(_onComplete);
                }
            }
            else
            {
                callback.Callback?.Invoke(callback.State);
            }

            if (_operation.IsCompleted)
            {
                _result = _operation.GetAwaiter().GetResult();
                _completed = true;
            }
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    internal override WorkFinish Finish(Exception? error)
    {
        _running = false;
        if (error is not null || _completed)
        {
            _terminal = true;
            _command = default;
            _work = null;
            _operation = default;
            _callbacks = null;
            if (error is not null)
            {
                _handlerCanceledException = error is OperationCanceledException && !CanceledBeforeStart;
                _source.SetException(error);
            }
            else
            {
                _source.SetResult(_result!);
            }

            _result = default;
            return WorkFinish.Completed;
        }

        if (_callbacks is { Count: > 0 } || _completionSignaled)
        {
            _queued = true;
            return WorkFinish.Requeue;
        }

        return WorkFinish.Suspended;
    }

    private void OnOperationCompleted()
    {
        ExecutionRuntime? runtime = Volatile.Read(ref _runtime);
        if (runtime is null)
        {
            return;
        }

        lock (runtime.Sync)
        {
            if (!_terminal)
            {
                _completionSignaled = true;
                Schedule(runtime);
            }
        }
    }

    private void Post(SendOrPostCallback callback, object? state)
    {
        ExecutionRuntime? runtime = Volatile.Read(ref _runtime);
        if (runtime is null)
        {
            throw new InvalidOperationException("The workflow has already completed.");
        }

        lock (runtime.Sync)
        {
            if (_terminal)
            {
                throw new InvalidOperationException("The workflow has already completed.");
            }

            (_callbacks ??= new()).Enqueue((callback, state));
            Schedule(runtime);
        }
    }

    private void Schedule(ExecutionRuntime runtime)
    {
        if (!_running && !_queued)
        {
            _queued = true;
            runtime.QueueReady(this);
            runtime.Notify(false);
        }
    }

    private sealed class WorkflowContext(PendingWorkflow<TCommand, TResult> workflow) : SynchronizationContext, IWorkflowContext
    {
        public ExecutionRuntime? Runtime => workflow._runtime;

        public bool ShouldYield(TimeSpan budget) => Stopwatch.GetElapsedTime(workflow._sliceStarted) >= budget;

        public override SynchronizationContext CreateCopy() => this;

        public override void Post(SendOrPostCallback d, object? state) => workflow.Post(d, state);

        public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException("Workflow callbacks must be asynchronous.");

        public override void OperationStarted() => throw new NotSupportedException("Async void is not supported in workflows.");
    }
}
