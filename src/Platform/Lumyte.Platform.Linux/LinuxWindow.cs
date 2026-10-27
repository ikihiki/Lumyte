using System.Numerics;
using System.Runtime.Versioning;
using System.Text;
using Lumyte.Windowing.Abstractions;

namespace Lumyte.Platform.Linux;

/// <summary>Implements the window contract directly using a borrowed Xlib display and native XID.</summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxWindow : IWindow
{
    private readonly LinuxPlatform _platform;
    private readonly Dictionary<WindowRequestId, PendingRequest> _pending = [];
    private readonly nuint _window;
    private WindowState _state;
    private ulong _nextRequest;
    private WindowRequestId? _close;
    private int _retained;
    private bool _destroySent;
    private bool _disposed;

    internal LinuxWindow(LinuxPlatform platform, WindowId id, WindowDescriptor descriptor)
    {
        _platform = platform;
        Id = id;
        _window = Xlib.XCreateSimpleWindow(platform.Display, Xlib.XDefaultRootWindow(platform.Display), 0, 0, (uint)Math.Ceiling(descriptor.ClientSize.X), (uint)Math.Ceiling(descriptor.ClientSize.Y), 0, 0, 0);
        if (_window == 0)
        {
            throw new InvalidOperationException("XCreateSimpleWindow failed.");
        }

        Xlib.XSelectInput(platform.Display, _window, (1 << 17) | (1 << 21) | 0x7F);
        nuint delete = platform.DeleteWindowAtom;
        Xlib.XSetWMProtocols(platform.Display, _window, ref delete, 1);
        if (descriptor.Owner is LinuxWindow owner)
        {
            Xlib.XSetTransientForHint(platform.Display, _window, owner._window);
        }

        if (descriptor.Kind == WindowKind.Popup)
        {
            var attributes = new Xlib.ChangeAttributes { OverrideRedirect = 1 };
            Xlib.XChangeWindowAttributes(platform.Display, _window, 1 << 9, ref attributes);
        }

        _state = new WindowState
        {
            Id = id,
            Owner = descriptor.Owner?.Id,
            Kind = descriptor.Kind,
            Activation = descriptor.Activation,
            Title = descriptor.Title,
            ClientSize = descriptor.ClientSize,
            PixelSize = new WindowPixelSize((int)Math.Ceiling(descriptor.ClientSize.X), (int)Math.Ceiling(descriptor.ClientSize.Y)),
            Scale = Vector2.One,
            IsVisible = false,
            IsActive = false,
            DisplayMode = WindowDisplayMode.Normal,
            Lifetime = WindowLifetime.Open,
            Features = WindowFeatures.OwnedWindow | (descriptor.Kind == WindowKind.Popup ? WindowFeatures.NonActivating : WindowFeatures.None),
        };
        SetNativeTitle(descriptor.Title);
        Xlib.XFlush(platform.Display);
    }

    /// <inheritdoc/>
    public event Action<WindowState>? StateChanged;

    /// <inheritdoc/>
    public event Action<WindowRequestResult>? RequestCompleted;

    /// <inheritdoc/>
    public event EventHandler<WindowClosingEventArgs>? Closing;

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

    /// <summary>Gets the borrowed Xlib Display pointer.</summary>
    public nint XDisplay
    {
        get
        {
            Check();
            return _platform.Display;
        }
    }

    /// <summary>Gets the borrowed X11 window identifier, valid until physical closure.</summary>
    public nuint XWindow
    {
        get
        {
            Check();
            if (_state.Lifetime == WindowLifetime.Closed)
            {
                throw new InvalidOperationException("The native window has closed.");
            }

            return _window;
        }
    }

    /// <inheritdoc/>
    public WindowRequestId Show() => Request(() => Xlib.XMapWindow(XDisplay, _window), s => s.IsVisible);

    /// <inheritdoc/>
    public WindowRequestId Hide() => Request(() => Xlib.XUnmapWindow(XDisplay, _window), s => !s.IsVisible);

    /// <inheritdoc/>
    public WindowRequestId SetTitle(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        if (title.Contains('\0'))
        {
            throw new ArgumentException("Title cannot contain NUL.", nameof(title));
        }

        return Request(
        () =>
        {
            SetNativeTitle(title);
            SetState(_state with { Title = title });
        },
        s => s.Title == title);
    }

    /// <inheritdoc/>
    public WindowRequestId Resize(Vector2 clientSize)
    {
        ValidateSize(clientSize);
        int width = (int)Math.Ceiling(clientSize.X);
        int height = (int)Math.Ceiling(clientSize.Y);
        return Request(() => Xlib.XResizeWindow(XDisplay, _window, (uint)width, (uint)height), s => s.PixelSize == new WindowPixelSize(width, height));
    }

    /// <inheritdoc/>
    public WindowRequestId Place(WindowPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        return Unsupported();
    }

    /// <inheritdoc/>
    public WindowRequestId Activate() => Unsupported();

    /// <inheritdoc/>
    public WindowRequestId BringToFront() => Unsupported();

    /// <inheritdoc/>
    public WindowRequestId SetDisplayMode(WindowDisplayMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        return mode == WindowDisplayMode.Normal ? Request(() => { }, s => s.DisplayMode == mode) : Unsupported();
    }

    /// <inheritdoc/>
    public WindowRequestId Close()
    {
        CheckMutation();
        if (_close is not null || _state.Lifetime != WindowLifetime.Open)
        {
            throw new InvalidOperationException("The window is closing or closed.");
        }

        WindowRequestId request = NextRequest(null);
        _close = request;
        _platform.Enqueue(() =>
        {
            if (_disposed)
            {
                Finish(request, WindowRequestStatus.Superseded);
            }
            else if (HasLiveChildren())
            {
                _close = null;
                Finish(request, WindowRequestStatus.Denied, "Close native children first.");
            }
            else
            {
                _platform.StopInput(this);
                Xlib.XUnmapWindow(XDisplay, _window);
                SetState(_state with { Lifetime = WindowLifetime.Closing, IsVisible = false, IsActive = false });
                Advance();
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
        if (_retained > 0 || HasLiveChildren())
        {
            throw new InvalidOperationException("Release retained targets and native children first.");
        }

        if (_state.Lifetime != WindowLifetime.Closed && !_destroySent)
        {
            IReadOnlyList<Xlib.ProtocolError> errors = X11Errors.Run(XDisplay, () => Xlib.XDestroyWindow(XDisplay, _window));
            if (errors.Any(e => e.Code != 3))
            {
                throw new AggregateException(errors.Select(X11Errors.Exception));
            }
        }

        foreach (WindowRequestId request in _pending.Keys.ToArray())
        {
            Finish(request, WindowRequestStatus.Superseded);
        }

        _disposed = true;
        _platform.Release(_window);
    }

    internal static void Validate(WindowDescriptor descriptor, LinuxPlatform platform)
    {
        ValidateSize(descriptor.ClientSize);
        if (descriptor.Title is null || descriptor.Title.Contains('\0') || !Enum.IsDefined(descriptor.Kind) || !Enum.IsDefined(descriptor.Activation))
        {
            throw new ArgumentException("Invalid window descriptor.", nameof(descriptor));
        }

        if (descriptor.Kind == WindowKind.Popup && (descriptor.Owner is null || descriptor.Activation != WindowActivation.PreserveFocus))
        {
            throw new ArgumentException("A popup requires an owner and PreserveFocus activation.", nameof(descriptor));
        }

        if (descriptor.Kind != WindowKind.Popup && descriptor.Activation == WindowActivation.PreserveFocus)
        {
            throw new NotSupportedException("Nonactivating X11 windows must be explicit override-redirect popups.");
        }

        if (descriptor.Owner is not null && (descriptor.Owner is not LinuxWindow owner || owner._platform != platform
            || owner.State.Lifetime != WindowLifetime.Open || owner._close is not null))
        {
            throw new ArgumentException("Owner must be open and on this platform.", nameof(descriptor));
        }
    }

    internal void NativeFailure(byte code, string message)
    {
        if (code == 3)
        {
            SetState(_state with { Lifetime = WindowLifetime.Closed, IsVisible = false, IsActive = false });
        }
        else if (_state.Lifetime == WindowLifetime.Closing)
        {
            SetState(_state with { Lifetime = WindowLifetime.Open });
        }

        _close = null;
        _destroySent = false;
        foreach (WindowRequestId request in _pending.Keys.ToArray())
        {
            Finish(request, WindowRequestStatus.Failed, message);
        }
    }

    internal void Observe(Xlib.Event nativeEvent)
    {
        if (nativeEvent.Type == 17)
        {
            SetState(_state with { Lifetime = WindowLifetime.Closed, IsVisible = false, IsActive = false });
            foreach (WindowRequestId request in _pending.Keys.ToArray())
            {
                Finish(request, request == _close ? WindowRequestStatus.Applied : WindowRequestStatus.Superseded);
            }
        }
        else if (nativeEvent.Type == 33 && (nuint)nativeEvent.Data0 == _platform.ProtocolsAtom && (nuint)nativeEvent.Data2 == _platform.DeleteWindowAtom)
        {
            _platform.UserClose(this);
        }
        else
        {
            Refresh();
        }
    }

    internal void Advance()
    {
        if (_disposed || _state.Lifetime == WindowLifetime.Closed)
        {
            return;
        }

        if (_state.Lifetime == WindowLifetime.Closing && _retained == 0 && !_destroySent)
        {
            Xlib.XDestroyWindow(XDisplay, _window);
            Xlib.XFlush(XDisplay);
            _destroySent = true;
        }

        if (_destroySent)
        {
            return;
        }

        Refresh();
        foreach (KeyValuePair<WindowRequestId, PendingRequest> pair in _pending.ToArray())
        {
            if (pair.Value.Condition?.Invoke(_state) == true)
            {
                Finish(pair.Key, WindowRequestStatus.Applied);
            }
            else if (pair.Value.Condition is not null && Environment.TickCount64 >= pair.Value.Deadline)
            {
                Finish(pair.Key, WindowRequestStatus.Denied, "The X11 window manager did not apply the request within two seconds.");
            }
        }
    }

    internal void UserClose()
    {
        var args = new WindowClosingEventArgs(Id);
        if (Closing is not null)
        {
            foreach (EventHandler<WindowClosingEventArgs> handler in Closing.GetInvocationList().Cast<EventHandler<WindowClosingEventArgs>>())
            {
                try
                {
                    handler(this, args);
                }
                catch
                {
                    args.Cancel = true;
                    throw;
                }
            }
        }

        if (!args.Cancel)
        {
            _platform.Enqueue(() =>
            {
                if (!_disposed && _state.Lifetime == WindowLifetime.Open && _close is null)
                {
                    Close();
                }
            });
        }
    }

    private static void ValidateSize(Vector2 size)
    {
        if (!float.IsFinite(size.X) || !float.IsFinite(size.Y) || size.X <= 0 || size.Y <= 0 || size.X > 32767 || size.Y > 32767)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }
    }

    private void SetNativeTitle(string title)
    {
        Xlib.XStoreName(XDisplay, _window, title);
        byte[] utf8 = Encoding.UTF8.GetBytes(title);
        Xlib.XChangeProperty(XDisplay, _window, Xlib.XInternAtom(XDisplay, "_NET_WM_NAME", 0), Xlib.XInternAtom(XDisplay, "UTF8_STRING", 0), 8, 0, utf8, utf8.Length);
    }

    private void Refresh()
    {
        if (_state.Lifetime == WindowLifetime.Closed || _destroySent)
        {
            return;
        }

        if (Xlib.XGetWindowAttributes(XDisplay, _window, out Xlib.Attributes attributes) == 0)
        {
            return;
        }

        Xlib.XGetInputFocus(XDisplay, out nuint focus, out _);
        SetState(_state with
        {
            ClientSize = new Vector2(attributes.Width, attributes.Height),
            PixelSize = new WindowPixelSize(attributes.Width, attributes.Height),
            IsVisible = attributes.MapState == 2,
            IsActive = focus == _window,
        });
    }

    private bool HasLiveChildren() => _platform.Windows.Any(w => w._state.Owner == Id && w._state.Lifetime != WindowLifetime.Closed);

    private void SetState(WindowState state)
    {
        if (_state != state)
        {
            _state = state;
            _platform.Notify(StateChanged, state);
        }
    }

    private WindowRequestId NextRequest(Func<WindowState, bool>? condition)
    {
        var request = new WindowRequestId(checked(++_nextRequest));
        _pending.Add(request, new PendingRequest(condition, Environment.TickCount64 + 2000));
        return request;
    }

    private WindowRequestId Request(Action action, Func<WindowState, bool> condition)
    {
        CheckMutation();
        if (_state.Lifetime != WindowLifetime.Open || _close is not null)
        {
            throw new InvalidOperationException("The window is closing or closed.");
        }

        WindowRequestId request = NextRequest(condition);
        _platform.Enqueue(() =>
        {
            if (_disposed || _state.Lifetime != WindowLifetime.Open)
            {
                Finish(request, WindowRequestStatus.Superseded);
            }
            else
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Finish(request, WindowRequestStatus.Failed, ex.Message);
                }
            }
        });
        return request;
    }

    private WindowRequestId Unsupported()
    {
        CheckMutation();
        WindowRequestId request = NextRequest(null);
        _platform.Enqueue(() => Finish(request, WindowRequestStatus.Unsupported));
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

    private sealed record PendingRequest(Func<WindowState, bool>? Condition, long Deadline);

    private sealed class Retention(LinuxWindow window) : IDisposable
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
