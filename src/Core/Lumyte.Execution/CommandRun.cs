using System.Runtime.ExceptionServices;

namespace Lumyte.Execution;

/// <summary>A batch completion and its retained typed results.</summary>
public sealed class CommandRun : IDisposable
{
    private readonly CancellationTokenRegistration _cancellation;
    private readonly object _sync;
    private readonly CommandBatch _batch;
    private readonly long _generation;
    private readonly long _bytes;
    private readonly int _count;
    private readonly CancellationToken _token;
    private ExecutionRuntime? _runtime;
    private BatchItem[] _items;
    private ExceptionDispatchInfo? _error;
    private TaskCompletionSource? _completion;
    private bool _cancelRequested;
    private bool _canceled;
    private int _remaining;
    private volatile bool _completed;
    private bool _disposed;

    internal CommandRun(ExecutionRuntime runtime, CommandBatch batch, BatchItem[] items, long bytes, CancellationToken token)
    {
        _sync = runtime.Sync;
        _runtime = runtime;
        _batch = batch;
        _generation = batch.Generation;
        _items = items;
        _remaining = items.Length;
        _count = items.Length;
        _bytes = bytes;
        _token = token;
        if (token.CanBeCanceled && items.Any(item => item.Definition!.Owner is null && item.Definition.Gates.Length > 0))
        {
            _cancellation = token.UnsafeRegister(static state => ((ExecutionRuntime)state!).NotifyCancellation(), runtime);
        }
    }

    /// <summary>Gets a value indicating whether every started handler is quiet.</summary>
    public bool IsCompleted => _completed;

    /// <summary>Gets a lazily created shared completion task.</summary>
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

    internal bool IsDisposed => _disposed;

    internal bool IsCancellationRequested => _cancelRequested || _error is not null || _token.IsCancellationRequested;

    /// <summary>Requests suppression of handlers which have not started.</summary>
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

    /// <summary>Throws the actual run failure or cancellation after all work is quiet.</summary>
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

    /// <summary>Retrieves an individual actual result, including successes from a failed batch.</summary>
    /// <typeparam name="TResult">The handler return type.</typeparam>
    /// <param name="key">The key from this batch generation.</param>
    /// <returns>The result.</returns>
    public TResult GetResult<TResult>(CommandResultKey<TResult> key)
    {
        lock (_sync)
        {
            CheckCompleted();
            if (key.Batch != _batch || key.Generation != _generation || (uint)key.Index >= (uint)_items.Length || _items[key.Index] is not BatchResult<TResult> item)
            {
                throw new ArgumentException("Result key does not belong to this run.", nameof(key));
            }

            return item.GetResult();
        }
    }

    /// <summary>Returns retained capacity after completion; performs no native cleanup.</summary>
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            CheckCompleted();
            foreach (BatchItem item in _items)
            {
                item.Clear();
            }

            _items = [];
            _runtime!.Release(1, _count, _bytes, false, 0);
            _runtime = null;
            _disposed = true;
        }
    }

    internal void ItemFinished(Exception? error, bool canceled)
    {
        lock (_sync)
        {
            if (canceled)
            {
                _canceled = true;
            }
            else if (error is not null)
            {
                _error ??= ExceptionDispatchInfo.Capture(error);
            }

            if (--_remaining == 0)
            {
                _cancellation.Unregister();
                _completed = true;
                PublishTask();
            }
        }
    }

    private void CheckCompleted()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_completed)
        {
            throw new InvalidOperationException("Run is not complete.");
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
