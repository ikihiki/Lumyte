using Lumyte.Input.Abstractions;
using Lumyte.UI.Automation;

namespace Lumyte.UI.SampleSupport;

// The dedicated automation sample forwards no external input. Call from its single UI execution loop.
internal sealed class UiSampleAutomationHost(UiSurface surface, Func<CancellationToken, ValueTask> nextFrame, TimeProvider? clock = null) : IUiAutomationHost, IAsyncDisposable
{
    private readonly UiSurface _surface = surface;
    private readonly SemaphoreSlim _input = new(1, 1);
    private Lease? _lease;
    private bool _closed;
    private long _identity;

    public TimeProvider TimeProvider { get; } = clock ?? TimeProvider.System;

    internal List<UiInput> Sent { get; } = [];

    public ValueTask<T> RunOnUiThreadAsync<T>(Func<UiSurface, T> action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_closed, this);
        return ValueTask.FromResult(action(_surface));
    }

    public ValueTask NextFrameAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_closed, this);
        return nextFrame(cancellationToken);
    }

    public async ValueTask<IUiAutomationInputLease> AcquireInputAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        await _input.WaitAsync(cancellationToken);
        if (_closed)
        {
            _input.Release();
            throw new ObjectDisposedException(nameof(UiSampleAutomationHost));
        }

        _lease = new(this, checked(++_identity));
        return _lease;
    }

    public async ValueTask DisposeAsync()
    {
        _closed = true;
        if (_lease is not null)
        {
            await _lease.DisposeAsync();
        }
    }

    private sealed class Lease(UiSampleAutomationHost host, long pointerId) : IUiAutomationInputLease
    {
        private readonly List<Key> _keys = [];
        private bool _pointerDown;
        private bool _disposed;

        public long PointerId { get; } = pointerId;

        public bool Dispatch(UiSurface target, UiInput input)
        {
            ObjectDisposedException.ThrowIf(_disposed || host._closed, this);
            if (!ReferenceEquals(target, host._surface))
            {
                throw new ArgumentException("The lease belongs to another surface.", nameof(target));
            }

            if (input is UiPointerInput pointer)
            {
                if (pointer.PointerId != PointerId)
                {
                    throw new ArgumentException("The pointer is not reserved by this lease.", nameof(input));
                }

                if (pointer.Phase == UiPointerPhase.Down)
                {
                    _pointerDown = true;
                }
            }
            else if (input is UiKeyInput { IsDown: true } key && !_keys.Contains(key.Key))
            {
                _keys.Add(key.Key);
            }
            else if (input is not UiKeyInput)
            {
                throw new ArgumentException("Only pointer and key transitions belong to an input lease.", nameof(input));
            }

            host.Sent.Add(input);
            bool consumed = host._surface.Dispatch(input);
            if (input is UiPointerInput { Phase: UiPointerPhase.Up or UiPointerPhase.Cancel })
            {
                _pointerDown = false;
            }
            else if (input is UiKeyInput { IsDown: false } released)
            {
                _keys.Remove(released.Key);
            }

            return consumed;
        }

        public ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;
            List<Exception>? failures = null;
            try
            {
                if (_pointerDown)
                {
                    Cleanup(new UiPointerInput(PointerId, UiPointerPhase.Cancel, default));
                }

                for (int i = _keys.Count - 1; i >= 0; i--)
                {
                    Cleanup(new UiKeyCancelInput(_keys[i]));
                    Cleanup(new UiKeyInput(_keys[i], false));
                }
            }
            finally
            {
                _keys.Clear();
                _pointerDown = false;
                host._lease = null;
                host._input.Release();
            }

            return failures is null ? ValueTask.CompletedTask : ValueTask.FromException(new AggregateException("Input cleanup failed.", failures));

            void Cleanup(UiInput input)
            {
                try
                {
                    host.Sent.Add(input);
                    host._surface.Dispatch(input);
                }
                catch (ObjectDisposedException)
                {
                    // Surface shutdown already cancelled its ownership. Return the host reservation.
                }
                catch (Exception error)
                {
                    (failures ??= []).Add(error);
                }
            }
        }
    }
}
