using System.Numerics;
using System.Runtime.InteropServices.JavaScript;
using Lumyte.Windowing.Abstractions;

namespace Lumyte.Platform.Browser;

/// <summary>Implements IWindow directly for an application-provided HTML canvas.</summary>
public sealed class BrowserWindow : IWindow
{
    private readonly BrowserPlatform _platform;
    private readonly JSObject _handle;
    private readonly JSObject _context;
    private readonly HashSet<WindowRequestId> _pending = [];
    private WindowState _state;
    private WindowRequestId? _close;
    private ulong _nextRequest;
    private int _retained;
    private bool _detached;
    private bool _disposed;

    internal BrowserWindow(BrowserPlatform platform, WindowId id, JSObject canvas, WindowDescriptor descriptor)
    {
        _platform = platform;
        Id = id;
        _handle = BrowserInterop.Attach(canvas, descriptor.Title, descriptor.ClientSize.X, descriptor.ClientSize.Y, descriptor.Activation == WindowActivation.PreserveFocus);
        try
        {
            _context = BrowserInterop.Context(_handle);
        }
        catch
        {
            BrowserInterop.Detach(_handle);
            _handle.Dispose();
            throw;
        }

        _state = new WindowState
        {
            Id = id,
            Owner = null,
            Kind = WindowKind.Normal,
            Activation = descriptor.Activation,
            Title = descriptor.Title,
            ClientSize = descriptor.ClientSize,
            PixelSize = default,
            Scale = Vector2.One,
            IsVisible = false,
            IsActive = false,
            DisplayMode = WindowDisplayMode.Normal,
            Lifetime = WindowLifetime.Open,
            Features = WindowFeatures.NonActivating,
        };
        ApplySnapshot(BrowserInterop.Snapshot(_handle), 0);
    }

    /// <inheritdoc/>
    public event Action<WindowState>? StateChanged;

    /// <inheritdoc/>
    public event Action<WindowRequestResult>? RequestCompleted;

    /// <inheritdoc/>
    public event EventHandler<WindowClosingEventArgs>? Closing
    {
        add => Check();
        remove => Check();
    }

    /// <inheritdoc/>
    public WindowId Id { get; }

    /// <inheritdoc/>
    public WindowState State
    {
        get
        {
            Check();
            return _state;
        }
    }

    /// <summary>Gets the borrowed GPUCanvasContext. Retain the target while Graphics uses it.</summary>
    public JSObject CanvasContext
    {
        get
        {
            Check();
            if (_state.Lifetime == WindowLifetime.Closed)
            {
                throw new InvalidOperationException("The canvas window has closed.");
            }

            return _context;
        }
    }

    /// <inheritdoc/>
    public WindowRequestId Show() => Request(() => BrowserInterop.Show(_handle, true), s => s.IsVisible);

    /// <inheritdoc/>
    public WindowRequestId Hide() => Request(() => BrowserInterop.Show(_handle, false), s => !s.IsVisible);

    /// <inheritdoc/>
    public WindowRequestId SetTitle(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        return Request(() =>
        {
            BrowserInterop.Title(_handle, title);
            SetState(_state with { Title = title });
        });
    }

    /// <inheritdoc/>
    public WindowRequestId Resize(Vector2 clientSize)
    {
        ValidateSize(clientSize);
        return Request(() => BrowserInterop.Resize(_handle, clientSize.X, clientSize.Y), s => s.ClientSize == clientSize);
    }

    /// <inheritdoc/>
    public WindowRequestId Place(WindowPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        return Unsupported();
    }

    /// <inheritdoc/>
    public WindowRequestId Activate()
    {
        if (_state.Activation == WindowActivation.PreserveFocus)
        {
            return Unsupported(WindowRequestStatus.Denied);
        }

        return Request(() => BrowserInterop.Activate(_handle), s => s.IsActive);
    }

    /// <inheritdoc/>
    public WindowRequestId BringToFront() => Unsupported();

    /// <inheritdoc/>
    public WindowRequestId SetDisplayMode(WindowDisplayMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        return mode == WindowDisplayMode.Normal ? Request(() => { }) : Unsupported();
    }

    /// <inheritdoc/>
    public WindowRequestId Close()
    {
        CheckMutation();
        if (_state.Lifetime != WindowLifetime.Open || _close is not null)
        {
            throw new InvalidOperationException("The canvas is closing or closed.");
        }

        WindowRequestId request = NextRequest();
        _close = request;
        _platform.Enqueue(() =>
        {
            if (!_disposed)
            {
                SetState(_state with { Lifetime = WindowLifetime.Closing });
                AdvanceClose();
            }
        });
        return request;
    }

    /// <inheritdoc/>
    public IDisposable RetainNativeTarget()
    {
        CheckMutation();
        if (_state.Lifetime != WindowLifetime.Open || _close is not null)
        {
            throw new InvalidOperationException("Retain only open targets.");
        }

        _retained = checked(_retained + 1);
        return new Retention(this);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CheckMutation();
        if (_retained != 0)
        {
            throw new InvalidOperationException("Release native retention tokens first.");
        }

        Detach();
        foreach (WindowRequestId request in _pending.ToArray())
        {
            Finish(request, WindowRequestStatus.Superseded);
        }

        _context.Dispose();
        _handle.Dispose();
        _disposed = true;
        _platform.Release();
    }

    internal static void Validate(WindowDescriptor descriptor)
    {
        ValidateSize(descriptor.ClientSize);
        if (descriptor.Title is null || !Enum.IsDefined(descriptor.Activation))
        {
            throw new ArgumentException("Invalid canvas window descriptor.", nameof(descriptor));
        }
    }

    internal void Observe()
    {
        if (!_detached && _state.Lifetime != WindowLifetime.Closed)
        {
            double[] snapshots = BrowserInterop.Drain(_handle);
            for (int i = 0; i < snapshots.Length; i += 8)
            {
                ApplySnapshot(snapshots, i);
            }

            ApplySnapshot(BrowserInterop.Snapshot(_handle), 0);
            _platform.ReceiveInput(BrowserInterop.DrainInput(_handle));
        }

        AdvanceClose();
    }

    private static void ValidateSize(Vector2 size)
    {
        if (!float.IsFinite(size.X) || !float.IsFinite(size.Y) || size.X <= 0 || size.Y <= 0 || size.X > 32767 || size.Y > 32767)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }
    }

    private void ApplySnapshot(double[] values, int offset)
    {
        bool connected = values[offset + 7] != 0;
        SetState(_state with
        {
            ClientSize = new Vector2((float)values[offset], (float)values[offset + 1]),
            PixelSize = new WindowPixelSize((int)values[offset + 2], (int)values[offset + 3]),
            Scale = new Vector2((float)values[offset + 4]),
            IsVisible = values[offset + 5] != 0 && connected,
            IsActive = values[offset + 6] != 0 && connected,
            Lifetime = connected ? _state.Lifetime : WindowLifetime.Closed,
        });
        if (!connected)
        {
            foreach (WindowRequestId request in _pending.ToArray())
            {
                Finish(request, WindowRequestStatus.Failed, "The application removed the borrowed canvas.");
            }
        }
    }

    private void AdvanceClose()
    {
        if (_close is { } request && _state.Lifetime == WindowLifetime.Closing && _retained == 0)
        {
            Detach();
            SetState(_state with { Lifetime = WindowLifetime.Closed, IsVisible = false, IsActive = false });
            Finish(request, WindowRequestStatus.Applied);
        }
    }

    private void Detach()
    {
        if (!_detached)
        {
            BrowserInterop.Detach(_handle);
            _detached = true;
        }
    }

    private void SetState(WindowState state)
    {
        if (_state != state)
        {
            _state = state;
            _platform.Notify(StateChanged, state);
        }
    }

    private WindowRequestId NextRequest()
    {
        var request = new WindowRequestId(checked(++_nextRequest));
        _pending.Add(request);
        return request;
    }

    private WindowRequestId Request(Action action, Func<WindowState, bool>? condition = null)
    {
        CheckMutation();
        if (_state.Lifetime != WindowLifetime.Open || _close is not null)
        {
            throw new InvalidOperationException("The canvas is closing or closed.");
        }

        WindowRequestId request = NextRequest();
        _platform.Enqueue(() =>
        {
            if (_disposed || _state.Lifetime != WindowLifetime.Open)
            {
                Finish(request, WindowRequestStatus.Superseded);
                return;
            }

            try
            {
                action();
                Observe();
                Finish(request, condition is null || condition(_state) ? WindowRequestStatus.Applied : WindowRequestStatus.Denied);
            }
            catch (Exception ex)
            {
                Finish(request, WindowRequestStatus.Failed, ex.Message);
            }
        });
        return request;
    }

    private WindowRequestId Unsupported(WindowRequestStatus status = WindowRequestStatus.Unsupported)
    {
        CheckMutation();
        WindowRequestId request = NextRequest();
        _platform.Enqueue(() => Finish(request, status));
        return request;
    }

    private void Finish(WindowRequestId request, WindowRequestStatus status, string? error = null)
    {
        if (_pending.Remove(request))
        {
            _platform.Notify(RequestCompleted, new WindowRequestResult(Id, request, status, error));
        }
    }

    private void Check()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _platform.Check();
    }

    private void CheckMutation()
    {
        Check();
        _platform.CheckMutation();
    }

    private sealed class Retention(BrowserWindow window) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                window.CheckMutation();
                window._retained--;
                _disposed = true;
            }
        }
    }
}
