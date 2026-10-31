using System.Numerics;
using Lumyte.Input.Abstractions;
using Lumyte.Platform.TextInput.Abstractions;
using Lumyte.UI.Automation;

namespace Lumyte.UI.Application;

internal class UiApplicationAutomationHost(UiApplication application, Func<UiSurface?> surface) : IUiAutomationHost, IUiAutomationRoutingHost
{
    private static long _nextPointer;
    private readonly UiApplication _application = application;
    private readonly Func<UiSurface?> _surface = surface;
    private readonly CancellationTokenSource _stop = new();
    private Lease? _lease;
    private bool _stopping;
    private bool _paused;
    private bool _disposed;
    private int _pending;

    public TimeProvider TimeProvider => _application.TimeProvider;

    internal bool HasLease => _lease is not null;

    internal bool IsStopped => _stopping && _lease is null && Volatile.Read(ref _pending) == 0;

    public ValueTask<T> RunOnUiThreadAsync<T>(Func<UiSurface, T> action, CancellationToken cancellationToken = default) =>
        _application.Dispatcher.InvokeAsync(
            () =>
            {
                ObjectDisposedException.ThrowIf(_stopping, this);
                return action(_surface() ?? throw new ObjectDisposedException(nameof(UiApplicationViewSlot)));
            },
            cancellationToken);

    public ValueTask NextFrameAsync(CancellationToken cancellationToken = default) => _application.NextFrameAsync(cancellationToken);

    public async ValueTask<IUiAutomationInputLease> AcquireInputAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        Interlocked.Increment(ref _pending);
        bool reserved = false;
        try
        {
            await _application.InputDomain.WaitAsync(linked.Token);
            reserved = true;
            while (true)
            {
                IUiAutomationInputLease? lease = await _application.Dispatcher.InvokeAsync<IUiAutomationInputLease?>(
                () =>
                {
                    ObjectDisposedException.ThrowIf(_stopping, this);
                    ITextInputService? service = _application.Driver.TextInput;
                    bool candidateIdle = service is ITextInputInputGate { IsCandidateInputIdle: true };
                    bool textIdle = service is ITextInputQuiescence { IsInputIdle: true };
                    if (_paused || !_application.Driver.IsPhysicalInputIdle || (service is ITextInputInputGate && !candidateIdle && !textIdle))
                    {
                        return null;
                    }

                    IDisposable? gate = (service as ITextInputInputGate)?.AcquireInputGate(candidateIdle);
                    _application.HasInputLease = true;
                    _lease = new(this, gate, NextPointerIdentity());
                    return _lease;
                },
                linked.Token);
                if (lease is not null)
                {
                    reserved = false;
                    return lease;
                }

                await NextFrameAsync(linked.Token);
            }
        }
        finally
        {
            if (reserved)
            {
                _application.InputDomain.Release();
            }

            Interlocked.Decrement(ref _pending);
        }
    }

    public UiPointerProbe ProbePointer(UiSurface surface, Vector2 logicalPosition) => _application.Popups.ProbePointer(surface, logicalPosition);

    public bool CanReceiveKeys(UiSurface surface) => _application.Popups.CanReceiveKeys(surface);

    public bool Dispatch(UiSurface surface, UiInput input) => (_lease ?? throw new InvalidOperationException("Reserve input before dispatch.")).Dispatch(surface, input);

    internal static UiApplicationAutomationHost Create(UiApplication application, Func<UiSurface?> surface) => application.Driver.SupportsScreenshots ? new ScreenshotHost(application, surface) : new UiApplicationAutomationHost(application, surface);

    internal static long NextPointerIdentity() => Interlocked.Increment(ref _nextPointer);

    internal void Pause() => _paused = true;

    internal void Resume() => _paused = false;

    internal void BeginStop()
    {
        if (_stopping)
        {
            return;
        }

        _stopping = true;
        _stop.Cancel();
        _lease?.ReleaseOnOwner();
    }

    internal void Dispose()
    {
        if (!_disposed)
        {
            if (!IsStopped)
            {
                throw new InvalidOperationException("Drain input leases before releasing their host.");
            }

            _stop.Dispose();
            _disposed = true;
        }
    }

    private sealed class ScreenshotHost(UiApplication application, Func<UiSurface?> surface) : UiApplicationAutomationHost(application, surface), IUiAutomationScreenshotHost
    {
        public async ValueTask<byte[]> CapturePngAsync(CancellationToken cancellationToken = default)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
            Interlocked.Increment(ref _pending);
            try
            {
                Task<byte[]> capture = await _application.Dispatcher.InvokeAsync(() => _application.Driver.CaptureAsync(_surface() ?? throw new ObjectDisposedException(nameof(UiSurface)), linked.Token).AsTask(), linked.Token);
                return await capture;
            }
            finally
            {
                Interlocked.Decrement(ref _pending);
            }
        }
    }

    private sealed class Lease(UiApplicationAutomationHost host, IDisposable? gate, long pointerId) : IUiAutomationOwnedRoutingLease
    {
        private readonly List<Key> _keys = [];
        private readonly Stack<IDisposable> _guards = [];
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _disposed;
        private bool _pointer;
        private Vector2 _position;
        private UiSurface? _surface;

        public long PointerId { get; } = pointerId;

        public bool Dispatch(UiSurface surface, UiInput input)
        {
            host._application.Owner.CheckOwner();
            ObjectDisposedException.ThrowIf(_disposed || host._stopping, this);
            if (!ReferenceEquals(surface, host._surface()))
            {
                throw new ArgumentException("The input lease belongs to another view.", nameof(surface));
            }

            _surface = surface;
            if (input is UiPointerInput pointer)
            {
                if (pointer.PointerId != PointerId)
                {
                    throw new ArgumentException("The pointer identity was not reserved.", nameof(input));
                }

                _position = pointer.Position;
                _pointer |= pointer.Phase == UiPointerPhase.Down;
            }
            else if (input is UiKeyInput key)
            {
                if (key.IsDown && !_keys.Contains(key.Key))
                {
                    _keys.Add(key.Key);
                }
            }
            else
            {
                throw new ArgumentException("Only pointer and key transitions are lease-owned.", nameof(input));
            }

            bool consumed;
            try
            {
                consumed = host._application.Popups.Dispatch(surface, input);
            }
            catch (Exception error)
            {
                host._application.FailInput(error);
                throw;
            }

            if (input is UiPointerInput { Phase: UiPointerPhase.Up or UiPointerPhase.Cancel })
            {
                _pointer = false;
            }
            else if (input is UiKeyInput { IsDown: false } released)
            {
                _keys.Remove(released.Key);
            }

            return consumed;
        }

        public void RegisterCleanup(IDisposable cleanup)
        {
            host._application.Owner.CheckOwner();
            ArgumentNullException.ThrowIfNull(cleanup);
            ObjectDisposedException.ThrowIf(_disposed, this);
            _guards.Push(cleanup);
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                await _released.Task;
                return;
            }

            if (host._application.Owner.CheckAccess())
            {
                ReleaseOnOwner();
            }
            else
            {
                try
                {
                    await host._application.Dispatcher.InvokeAsync(ReleaseOnOwner);
                }
                catch (InvalidOperationException) when (host._stopping || host._application.State is UiApplicationState.Stopping or UiApplicationState.StopFailed or UiApplicationState.Stopped or UiApplicationState.Failed)
                {
                    await _released.Task;
                }
            }

            await _released.Task;
        }

        internal void ReleaseOnOwner()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            List<Exception>? failures = null;
            if (_surface is { } surface)
            {
                if (_pointer)
                {
                    Cancel(new UiPointerInput(PointerId, UiPointerPhase.Cancel, _position));
                }

                foreach (Key key in _keys)
                {
                    Cancel(new UiKeyCancelInput(key));
                }

                void Cancel(UiInput input)
                {
                    try
                    {
                        host._application.Popups.Dispatch(surface, input);
                    }
                    catch (Exception error)
                    {
                        (failures ??= []).Add(error);
                    }
                }
            }

            while (_guards.TryPop(out IDisposable? guard))
            {
                try
                {
                    guard.Dispose();
                }
                catch (Exception error)
                {
                    (failures ??= []).Add(error);
                }
            }

            try
            {
                gate?.Dispose();
            }
            catch (Exception error)
            {
                (failures ??= []).Add(error);
            }
            finally
            {
                host._lease = null;
                host._application.HasInputLease = false;
                host._application.InputDomain.Release();
            }

            if (failures is null)
            {
                _released.TrySetResult();
            }
            else
            {
                _released.TrySetException(new AggregateException(failures));
            }
        }
    }
}
