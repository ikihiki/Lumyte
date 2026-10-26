using System.Runtime.ExceptionServices;

namespace Lumyte.Execution;

/// <summary>A graph completion which retains its reserved capacity until disposed.</summary>
public sealed class JobRun : IDisposable
{
    private readonly CancellationTokenRegistration _cancellation;
    private readonly object _sync;
    private readonly int _commands;
    private readonly int _nodes;
    private readonly long _bytes;
    private readonly CancellationToken _token;
    private ExecutionRuntime? _runtime;
    private GraphState? _state;
    private ExceptionDispatchInfo? _error;
    private TaskCompletionSource? _completion;
    private bool _cancelRequested;
    private bool _canceled;
    private bool _disposed;
    private volatile bool _completed;

    internal JobRun(ExecutionRuntime runtime, int commands, int nodes, long bytes, CancellationToken token)
    {
        _runtime = runtime;
        _sync = runtime.Sync;
        _commands = commands;
        _nodes = nodes;
        _bytes = bytes;
        _token = token;
        if (token.CanBeCanceled)
        {
            _cancellation = token.UnsafeRegister(static state => ((ExecutionRuntime)state!).NotifyCancellation(), runtime);
        }
    }

    /// <summary>Gets a value indicating whether propagation and all started chunks are complete.</summary>
    public bool IsCompleted => _completed;

    /// <summary>Gets a lazily created completion task.</summary>
    public Task Completion
    {
        get
        {
            lock (_sync)
            {
                if (_completion is null)
                {
                    _completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    if (_completed)
                    {
                        PublishTask();
                    }
                }

                return _completion.Task;
            }
        }
    }

    internal bool IsCancellationRequested => _cancelRequested || _error is not null;

    /// <summary>Suppresses unstarted graph work; running chunks finish normally.</summary>
    public void RequestCancel()
    {
        ExecutionRuntime.VerifyOutsideHandler();
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _cancelRequested = true;
            _runtime!.Notify();
        }
    }

    /// <summary>Throws the actual graph failure or cancellation after completion.</summary>
    public void ThrowIfFailed()
    {
        lock (_sync)
        {
            CheckCompleted();
            _error?.Throw();
            if (_canceled)
            {
                throw new OperationCanceledException(_token);
            }
        }
    }

    /// <summary>Releases retained graph capacity after completion.</summary>
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            CheckCompleted();
            _state!.Clear();
            _state = null;
            _runtime!.Release(1, _commands, _bytes, false, _nodes);
            _runtime = null;
            _disposed = true;
        }
    }

    internal void Attach(GraphState state) => _state = state;

    internal void Record(Exception? error, bool canceled)
    {
        if (canceled)
        {
            _canceled = true;
        }
        else if (error is not null)
        {
            _error ??= ExceptionDispatchInfo.Capture(error);
        }
    }

    internal void Complete()
    {
        _cancellation.Unregister();
        _completed = true;
        PublishTask();
    }

    private void CheckCompleted()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_completed)
        {
            throw new InvalidOperationException("Graph is not complete.");
        }
    }

    private void PublishTask()
    {
        if (_error is not null)
        {
            _completion?.TrySetException(_error.SourceException);
        }
        else if (_canceled)
        {
            _completion?.TrySetCanceled(_token);
        }
        else
        {
            _completion?.TrySetResult();
        }
    }
}
