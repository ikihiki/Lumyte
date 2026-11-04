using System.Collections.Concurrent;
using Lumyte.UI.Automation;

namespace Lumyte.UI.SampleSupport;

// Dedicated diagnostic sample: external UI input is not forwarded while automation is enabled.
internal sealed class UiQueuedAutomationHost : IUiAutomationHost, IAsyncDisposable
{
    private readonly UiSurface _surface;
    private readonly UiSampleAutomationHost _input;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly ConcurrentQueue<Action> _queue = new();
    private readonly List<TaskCompletionSource> _frames = [];
    private int _pending;
    private bool _closed;

    internal UiQueuedAutomationHost(UiSurface surface, TimeProvider? clock = null)
    {
        _surface = surface;
        TimeProvider = clock ?? TimeProvider.System;
        _input = new(surface, static _ => ValueTask.CompletedTask, TimeProvider);
    }

    public TimeProvider TimeProvider { get; }

    public ValueTask<T> RunOnUiThreadAsync<T>(Func<UiSurface, T> action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_closed, this);
        if (Environment.CurrentManagedThreadId == _owner)
        {
            return ValueTask.FromResult(action(_surface));
        }

        if (Interlocked.Increment(ref _pending) > 256)
        {
            Interlocked.Decrement(ref _pending);
            throw new InvalidOperationException("The bounded UI queue is full.");
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Enqueue(() =>
        {
            Interlocked.Decrement(ref _pending);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(_closed, this);
                completion.SetResult(action(_surface));
            }
            catch (Exception failure)
            {
                completion.SetException(failure);
            }
        });
        return new(completion.Task);
    }

    public async ValueTask NextFrameAsync(CancellationToken cancellationToken = default)
    {
        Task frame = await RunOnUiThreadAsync(
            _ =>
            {
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _frames.Add(completion);
                return completion.Task;
            },
            cancellationToken);
        await frame.WaitAsync(cancellationToken);
    }

    public async ValueTask<IUiAutomationInputLease> AcquireInputAsync(CancellationToken cancellationToken = default)
    {
        ValueTask<IUiAutomationInputLease> acquisition = await RunOnUiThreadAsync(_ => _input.AcquireInputAsync(cancellationToken), cancellationToken);
        return new Lease(this, await acquisition);
    }

    public ValueTask DisposeAsync()
    {
        if (Environment.CurrentManagedThreadId != _owner)
        {
            throw new InvalidOperationException("Dispose the sample host on its owner after diagnostics IsStopped.");
        }

        _closed = true;
        while (_queue.TryDequeue(out Action? work))
        {
            work();
        }

        foreach (TaskCompletionSource frame in _frames)
        {
            frame.TrySetCanceled();
        }

        _frames.Clear();
        return _input.DisposeAsync();
    }

    internal void AdvanceFrame()
    {
        if (Environment.CurrentManagedThreadId != _owner)
        {
            throw new InvalidOperationException("Advance the sample UI on its owner.");
        }

        TaskCompletionSource[] previous = _frames.ToArray();
        _frames.Clear();
        for (int budget = 0; budget < 64 && _queue.TryDequeue(out Action? work); budget++)
        {
            work();
        }

        _surface.UpdateLayout();
        foreach (TaskCompletionSource frame in previous)
        {
            frame.TrySetResult();
        }
    }

    private sealed class Lease(UiQueuedAutomationHost host, IUiAutomationInputLease inner) : IUiAutomationInputLease
    {
        public long PointerId => inner.PointerId;

        public bool Dispatch(UiSurface surface, UiInput input)
        {
            if (Environment.CurrentManagedThreadId != host._owner)
            {
                throw new InvalidOperationException("Dispatch synthetic input on the UI owner.");
            }

            return inner.Dispatch(surface, input);
        }

        public async ValueTask DisposeAsync()
        {
            ValueTask cleanup = await host.RunOnUiThreadAsync(_ => inner.DisposeAsync());
            await cleanup;
        }
    }
}
