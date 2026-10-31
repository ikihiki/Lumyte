using System.Runtime.ExceptionServices;

namespace Lumyte.Signals;

/// <summary>Publishes asynchronous results through the ordinary signal graph, with latest-operation semantics.</summary>
/// <typeparam name="T">The result type.</typeparam>
public sealed class AsyncSignal<T> : IReadSignal<AsyncSignalState<T>>, IDisposable
{
    private readonly Signal<AsyncSignalState<T>> _state = Signal.Create(new AsyncSignalState<T>(AsyncSignalStatus.Pending));
    private Func<CancellationToken, Task<T>>? _load;
    private Operation? _operation;
    private Task<AsyncSignalState<T>> _completion = null!;
    private bool _disposed;

    internal AsyncSignal(Func<CancellationToken, Task<T>> load)
    {
        _load = load;
        _ = ReloadAsync();
    }

    /// <inheritdoc/>
    public AsyncSignalState<T> Value => _state.Value;

    /// <summary>Gets the completion of the latest operation, including failure or cancellation as a snapshot.</summary>
    /// <remarks>Observer delivery failures fault this task rather than becoming loader failures.</remarks>
    public Task<AsyncSignalState<T>> Completion
    {
        get
        {
            lock (SignalContext.Gate)
            {
                return _completion;
            }
        }
    }

    /// <inheritdoc/>
    public AsyncSignalState<T> Peek() => _state.Peek();

    /// <summary>Starts another operation, canceling the previous operation and revoking its publication.</summary>
    /// <returns>The new operation's terminal snapshot; loader failures are returned as Failed.</returns>
    public Task<AsyncSignalState<T>> ReloadAsync()
    {
        Operation? previous;
        Operation next;
        Func<CancellationToken, Task<T>> load;
        Exception? failure = null;
        lock (SignalContext.Gate)
        {
            SignalContext.CheckMutation();
            ObjectDisposedException.ThrowIf(_disposed, this);
            previous = _operation;
            next = new();
            load = _load!;
            _operation = next;
            _completion = next.Completion.Task;
            previous?.Completion.TrySetResult(new(AsyncSignalStatus.Canceled));
            try
            {
                _state.Value = new(AsyncSignalStatus.Pending);
            }
            catch (Exception error)
            {
                failure = error;
                if (ReferenceEquals(_operation, next))
                {
                    _operation = null;
                }

                next.Completion.TrySetException(error);
            }
        }

        try
        {
            previous?.Cancel();
        }
        finally
        {
            if (failure is null)
            {
                _ = ExecuteAsync(next, load);
            }
            else
            {
                next.Dispose();
            }
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return next.Completion.Task;
    }

    /// <summary>Cancels the active operation and publishes Canceled without disposing the signal.</summary>
    public void Cancel()
    {
        Operation? operation = null;
        try
        {
            lock (SignalContext.Gate)
            {
                SignalContext.CheckMutation();
                ObjectDisposedException.ThrowIf(_disposed, this);
                operation = _operation;
                _operation = null;
                if (operation is not null)
                {
                    try
                    {
                        _state.Value = new(AsyncSignalStatus.Canceled);
                    }
                    finally
                    {
                        operation.Completion.TrySetResult(new(AsyncSignalStatus.Canceled));
                    }
                }
            }
        }
        finally
        {
            operation?.Cancel();
        }
    }

    /// <summary>Revokes publication and cancels active work, leaving the last snapshot readable without notifying.</summary>
    public void Dispose()
    {
        Operation? operation;
        lock (SignalContext.Gate)
        {
            SignalContext.CheckMutation();
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _load = null;
            operation = _operation;
            _operation = null;
            operation?.Completion.TrySetResult(new(AsyncSignalStatus.Canceled));
        }

        operation?.Cancel();
    }

    private async Task ExecuteAsync(Operation operation, Func<CancellationToken, Task<T>> load)
    {
        AsyncSignalState<T> result;
        try
        {
            Task<T> pending = Signal.Untracked(() =>
            {
                if (_disposed || !ReferenceEquals(_operation, operation))
                {
                    throw new OperationCanceledException(operation.Token);
                }

                operation.Token.ThrowIfCancellationRequested();
                return load(operation.Token);
            }) ?? throw new InvalidOperationException("The asynchronous factory returned no task.");
            T value = await pending.ConfigureAwait(false);
            result = new(AsyncSignalStatus.Succeeded, value);
        }
        catch (OperationCanceledException)
        {
            result = new(AsyncSignalStatus.Canceled);
        }
        catch (Exception error)
        {
            result = new(AsyncSignalStatus.Failed, Error: error);
        }

        try
        {
            lock (SignalContext.Gate)
            {
                if (!_disposed && ReferenceEquals(_operation, operation))
                {
                    _operation = null;
                    _state.Value = result;
                }
            }

            operation.Completion.TrySetResult(result);
        }
        catch (Exception error)
        {
            operation.Completion.TrySetException(error);
        }
        finally
        {
            operation.Dispose();
        }
    }

    private sealed class Operation : IDisposable
    {
        private readonly object _gate = new();
        private readonly CancellationTokenSource _cancellation = new();
        private bool _disposed;

        internal Operation()
        {
            Token = _cancellation.Token;
        }

        internal CancellationToken Token { get; }

        internal TaskCompletionSource<AsyncSignalState<T>> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Dispose()
        {
            lock (_gate)
            {
                if (!_disposed)
                {
                    _disposed = true;
                    _cancellation.Dispose();
                }
            }
        }

        internal void Cancel()
        {
            Completion.TrySetResult(new(AsyncSignalStatus.Canceled));
            lock (_gate)
            {
                if (!_disposed)
                {
                    _cancellation.Cancel();
                }
            }
        }
    }
}
