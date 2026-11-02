using System.Collections.Concurrent;

namespace Lumyte.UI.Application;

internal sealed class UiApplicationDispatcher(UiApplicationOptions options) : SynchronizationContext, IUiDispatcher
{
    private static readonly AsyncLocal<Admission?> _admission = new();
    private readonly ConcurrentQueue<Action> _continuations = new();
    private readonly ConcurrentQueue<Action> _external = new();
    private int _owner;
    private int _queued;
    private volatile bool _accepting;
    private volatile bool _closed;
    private AutoResetEvent? _wakeup;

    public bool CheckAccess() => _owner != 0 && Environment.CurrentManagedThreadId == _owner;

    public ValueTask InvokeAsync(Action action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return new(InvokeAsync(
            () =>
            {
                action();
                return true;
            },
            cancellationToken).AsTask());
    }

    public ValueTask<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_closed, this);
        Admission? admission = _admission.Value;
        bool cleanup = admission is { Active: true } && ReferenceEquals(admission.Dispatcher, this);
        if (_owner == 0 || (!_accepting && !cleanup))
        {
            throw new InvalidOperationException("The application dispatcher is not accepting work.");
        }

        if (!cleanup && Interlocked.Increment(ref _queued) > options.MaximumQueuedUiWork)
        {
            Interlocked.Decrement(ref _queued);
            throw new UiApplicationException("ui-queue-full");
        }

        if (cleanup && !admission!.TryReserve(options.MaximumWorkItemsPerTurn))
        {
            throw new UiApplicationException("ui-queue-full");
        }

        int execution = 0;
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration registration = cancellationToken.Register(() =>
        {
            if (Interlocked.CompareExchange(ref execution, 3, 0) == 0)
            {
                completion.TrySetCanceled(cancellationToken);
            }
        });
        void Run()
        {
            if (!cleanup)
            {
                Interlocked.Decrement(ref _queued);
            }
            else
            {
                admission!.Release();
            }

            try
            {
                if (_closed || (cleanup && !admission!.Active))
                {
                    completion.TrySetException(new ObjectDisposedException(nameof(UiApplicationDispatcher)));
                }
                else if (cancellationToken.IsCancellationRequested && Interlocked.CompareExchange(ref execution, 3, 0) == 0)
                {
                    completion.TrySetCanceled(cancellationToken);
                }
                else if (Interlocked.CompareExchange(ref execution, 1, 0) == 0)
                {
                    completion.TrySetResult(action());
                }
            }
            catch (Exception error)
            {
                completion.TrySetException(error);
            }
            finally
            {
                Volatile.Write(ref execution, 2);
                registration.Dispose();
            }
        }

        (cleanup ? _continuations : _external).Enqueue(Run);
        Wake();
        return new(completion.Task);
    }

    public override void Post(SendOrPostCallback callback, object? state)
    {
        _continuations.Enqueue(() => callback(state));
        Wake();
    }

    public override void Send(SendOrPostCallback callback, object? state) => throw new NotSupportedException("Synchronous UI dispatch is not supported.");

    internal void BindOwner()
    {
        _owner = Environment.CurrentManagedThreadId;
        _accepting = true;
    }

    internal void BeginStop() => _accepting = false;

    internal void EnableThreadWait() => _wakeup = new(false);

    internal void ReleaseThreadWait() => Interlocked.Exchange(ref _wakeup, null)?.Dispose();

    internal void WaitForWork(TimeSpan timeout, Action<WaitHandle, TimeSpan>? wait = null)
    {
        if (_continuations.IsEmpty && _external.IsEmpty)
        {
            if (wait is null)
            {
                _wakeup!.WaitOne(TimeSpan.FromMilliseconds(Math.Max(1, Math.Ceiling(timeout.TotalMilliseconds))));
            }
            else
            {
                wait(_wakeup!, timeout);
            }
        }
    }

    internal void Wake()
    {
        AutoResetEvent? wakeup = Volatile.Read(ref _wakeup);
        try
        {
            wakeup?.Set();
        }
        catch (ObjectDisposedException) when (!ReferenceEquals(wakeup, Volatile.Read(ref _wakeup)))
        {
            // A late completion can race the owner returning its native wait handle.
        }
    }

    internal void Close()
    {
        _accepting = false;
        _closed = true;
        while (_external.TryDequeue(out Action? action))
        {
            action();
        }

        while (_continuations.TryDequeue(out Action? continuation))
        {
            continuation();
        }
    }

    internal void Pump()
    {
        long started = options.TimeProvider.GetTimestamp();
        SynchronizationContext? previous = Current;
        SetSynchronizationContext(this);
        try
        {
            int count = Math.Min(options.MaximumWorkItemsPerTurn, _continuations.Count + _external.Count);
            for (int index = 0; index < count; index++)
            {
                if (_continuations.TryDequeue(out Action? continuation))
                {
                    continuation();
                }
                else if (_external.TryDequeue(out Action? action))
                {
                    action();
                }

                if (options.TimeProvider.GetElapsedTime(started) >= options.MaximumWorkTimePerTurn)
                {
                    break;
                }
            }
        }
        finally
        {
            SetSynchronizationContext(previous);
        }
    }

    internal async Task TrackAsync(Func<Task> action)
    {
        var admission = new Admission(this);
        Admission? previousAdmission = _admission.Value;
        SynchronizationContext? previous = Current;
        _admission.Value = admission;
        SetSynchronizationContext(this);
        try
        {
            await action();
        }
        finally
        {
            admission.Deactivate();
            _admission.Value = previousAdmission;
            SetSynchronizationContext(previous);
        }
    }

    internal void CheckOwner()
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        if (!CheckAccess())
        {
            throw new InvalidOperationException("Access the window from its UI owner or dispatcher.");
        }
    }

    private sealed class Admission(UiApplicationDispatcher dispatcher)
    {
        private bool _active = true;
        private int _queued;

        internal UiApplicationDispatcher Dispatcher { get; } = dispatcher;

        internal bool Active => Volatile.Read(ref _active);

        internal bool TryReserve(int maximum)
        {
            if (Interlocked.Increment(ref _queued) <= maximum)
            {
                return true;
            }

            Interlocked.Decrement(ref _queued);
            return false;
        }

        internal void Release() => Interlocked.Decrement(ref _queued);

        internal void Deactivate() => Volatile.Write(ref _active, false);
    }
}
