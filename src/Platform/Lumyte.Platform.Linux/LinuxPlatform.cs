using System.Numerics;
using System.Runtime.Versioning;
using Lumyte.Input.Abstractions;
using Lumyte.Platform.Abstractions;
using Lumyte.Platform.Internal;
using Lumyte.Windowing.Abstractions;

namespace Lumyte.Platform.Linux;

/// <summary>Creates X11 presentation windows on a single thread.</summary>
/// <remarks>The backend provides X11 windows and logical keyboard/mouse input. Clipboard selection and text services are not implemented.</remarks>
[SupportedOSPlatform("linux")]
public sealed class LinuxPlatform : IPlatform
{
    private readonly HashSet<MouseButton> _mouseButtons = [];
    private readonly HashSet<Key> _pressed = [];
    private readonly Func<uint, Key> _keyMapping;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly Dictionary<nuint, LinuxWindow> _windows = [];
    private readonly Queue<Action> _operations = new();
    private readonly Queue<Action> _notifications = new();
    private readonly List<Exception> _errors = [];
    private readonly nint _display;
    private BufferedInputSource? _source;
    private LinuxWindow? _captureWindow;
    private bool _mouseBlocked;
    private ulong _nextWindow;
    private bool _disposed;
    private bool _pumping;
    private bool _notifying;

    /// <summary>Initializes a new instance of the <see cref="LinuxPlatform"/> class.</summary>
    /// <param name="keyMapping">An optional X11 keycode mapping; the default assumes an evdev-based XKB keymap.</param>
    /// <exception cref="PlatformNotSupportedException">No X11 display is available.</exception>
    public LinuxPlatform(Func<uint, Key>? keyMapping = null)
    {
        if (!OperatingSystem.IsLinux() || IntPtr.Size != 8)
        {
            throw new PlatformNotSupportedException("The X11 backend requires 64-bit Linux.");
        }

        _keyMapping = keyMapping ?? (code => code >= 8 ? PhysicalKeys.FromEvdevCode(code - 8) : Key.Unknown);
        _display = Xlib.XOpenDisplay(0);
        if (_display == 0)
        {
            throw new PlatformNotSupportedException("Cannot open an X11 display. A Wayland-only environment requires a Wayland backend.");
        }

        Xlib.XkbSetDetectableAutoRepeat(_display, 1, out _);
        DeleteWindowAtom = Xlib.XInternAtom(_display, "WM_DELETE_WINDOW", 0);
        ProtocolsAtom = Xlib.XInternAtom(_display, "WM_PROTOCOLS", 0);
    }

    /// <inheritdoc/>
    public Guid Id { get; } = Guid.NewGuid();

    /// <inheritdoc/>
    public PlatformFeatures Features => PlatformFeatures.NativeWindows | PlatformFeatures.DeviceInput;

    internal nint Display => _display;

    internal nuint DeleteWindowAtom { get; }

    internal nuint ProtocolsAtom { get; }

    internal IEnumerable<LinuxWindow> Windows => _windows.Values;

    /// <inheritdoc/>
    public IWindow CreateWindow(WindowDescriptor descriptor)
    {
        CheckMutation();
        ArgumentNullException.ThrowIfNull(descriptor);
        LinuxWindow.Validate(descriptor, this);
        LinuxWindow? window = null;
        IReadOnlyList<Xlib.ProtocolError> errors = X11Errors.Run(_display, () => window = new LinuxWindow(this, new WindowId(Id, checked(++_nextWindow)), descriptor));
        if (errors.Count > 0)
        {
            window?.Dispose();
            throw new AggregateException(errors.Select(X11Errors.Exception));
        }

        _windows.Add(window!.XWindow, window);
        return window;
    }

    /// <inheritdoc/>
    public IInputSource CreateInputSource()
    {
        CheckMutation();
        if (_source is not null)
        {
            throw new InvalidOperationException("Only one input source is supported.");
        }

        _source = new BufferedInputSource(CheckMutation, _ => _source = null, () => _windows.Values, "X11", CancelMouse);
        return _source;
    }

    /// <inheritdoc/>
    public IClipboard CreateClipboard() => throw new NotSupportedException("X11 selection transfers are not supported by this backend.");

    /// <inheritdoc/>
    public void PumpEvents()
    {
        CheckMutation();
        if (_pumping)
        {
            throw new InvalidOperationException("Reentrant pumping is forbidden.");
        }

        _pumping = true;
        try
        {
            IReadOnlyList<Xlib.ProtocolError> errors = X11Errors.Run(_display, () =>
            {
                while (_operations.TryDequeue(out Action? operation))
                {
                    Execute(operation);
                }

                Xlib.XFlush(_display);
                while (Xlib.XPending(_display) > 0)
                {
                    Xlib.XNextEvent(_display, out Xlib.Event nativeEvent);
                    if (_windows.TryGetValue(nativeEvent.Window, out LinuxWindow? window))
                    {
                        Xlib.Event captured = nativeEvent;
                        Execute(() =>
                        {
                            window.Observe(captured);
                            Receive(captured);
                        });
                    }
                }

                foreach (LinuxWindow window in _windows.Values.ToArray())
                {
                    Execute(window.Advance);
                }
            });
            foreach (Xlib.ProtocolError error in errors)
            {
                Exception failure = X11Errors.Exception(error);
                _errors.Add(failure);
                if (_windows.TryGetValue(error.Resource, out LinuxWindow? window))
                {
                    window.NativeFailure(error.Code, failure.Message);
                }
            }

            _notifying = true;
            while (_notifications.TryDequeue(out Action? notification))
            {
                Execute(notification);
            }
        }
        finally
        {
            _notifying = false;
            _pumping = false;
        }

        if (_errors.Count > 0)
        {
            Exception[] errors = _errors.ToArray();
            _errors.Clear();
            throw new AggregateException(errors);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CheckMutation();
        if (_pumping || _windows.Count > 0 || _source is not null)
        {
            throw new InvalidOperationException("Dispose native windows first.");
        }

        Xlib.XCloseDisplay(_display);
        _disposed = true;
    }

    internal void CheckMutation()
    {
        Check();
        if (_notifying)
        {
            throw new InvalidOperationException("Mutation during notifications is forbidden.");
        }
    }

    internal void Check()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Environment.CurrentManagedThreadId != _thread)
        {
            throw new InvalidOperationException("Use the creating thread.");
        }
    }

    internal void Enqueue(Action operation) => _operations.Enqueue(operation);

    internal void StopInput(LinuxWindow window)
    {
        if (_captureWindow == window)
        {
            CancelMouse();
        }

        _source?.Close(window.Id);
    }

    internal void Notify<T>(Action<T>? handlers, T value) => _notifications.Enqueue(() =>
    {
        if (handlers is not null)
        {
            foreach (Action<T> handler in handlers.GetInvocationList().Cast<Action<T>>())
            {
                Execute(() => handler(value));
            }
        }
    });

    internal void UserClose(LinuxWindow window)
    {
        _notifying = true;
        try
        {
            window.UserClose();
        }
        finally
        {
            _notifying = false;
        }
    }

    internal void Release(nuint window) => _windows.Remove(window);

    private void Receive(Xlib.Event data)
    {
        if (_source is null || !_windows.TryGetValue(data.Window, out LinuxWindow? window))
        {
            return;
        }

        if (_captureWindow == window && (!window.State.IsVisible || window.State.Lifetime != WindowLifetime.Open))
        {
            CancelMouse();
        }

        if (window.State.Lifetime != WindowLifetime.Open)
        {
            _source.Close(window.Id);
            return;
        }

        if (data.Type is 9 or 10)
        {
            if (data.Type == 10)
            {
                _pressed.Clear();
                if (_captureWindow == window)
                {
                    CancelMouse();
                }
            }

            _source.Focus(window.Id, data.Type == 9);
        }
        else if (data.Type is 2 or 3)
        {
            uint code = (uint)((nuint)data.Data5 >> 32);
            Key key = _keyMapping(code);
            if (!Enum.IsDefined(key))
            {
                key = Key.Unknown;
            }

            bool down = data.Type == 2;
            bool repeat = down && !_pressed.Add(key);
            if (!down)
            {
                _pressed.Remove(key);
            }

            _source.Keyboard(window.Id, new KeyData(key, down, repeat));
        }
        else if (data.Type == 6 && !_mouseBlocked)
        {
            _source.Mouse(window.Id, new MouseMoveData(new Vector2((int)data.Data3, (int)(data.Data3 >> 32))));
        }
        else if (data.Type is 4 or 5)
        {
            uint button = (uint)((nuint)data.Data5 >> 32);
            if (button is >= 4 and <= 7)
            {
                if (data.Type == 4)
                {
                    _source.Mouse(window.Id, new MouseWheelData(button switch
                    {
                        4 => new Vector2(0, 1),
                        5 => new Vector2(0, -1),
                        6 => new Vector2(-1, 0),
                        _ => new Vector2(1, 0),
                    }));
                }
            }
            else if (button is 1 or 2 or 3 or 8 or 9)
            {
                MouseButton mapped = button switch
                {
                    1 => MouseButton.Left,
                    2 => MouseButton.Middle,
                    3 => MouseButton.Right,
                    8 => MouseButton.X1,
                    _ => MouseButton.X2,
                };
                bool down = data.Type == 4;
                if (down)
                {
                    if (_mouseButtons.Count == 0 && !_mouseBlocked)
                    {
                        // ownerEvents=false keeps coordinates in the grabbing client's space.
                        if (Xlib.XGrabPointer(_display, window.XWindow, 0, (1U << 2) | (1U << 3) | (1U << 6), 1, 1, 0, 0, 0) == 0)
                        {
                            _captureWindow = window;
                        }
                        else
                        {
                            _mouseBlocked = true;
                        }
                    }

                    _mouseButtons.Add(mapped);
                }
                else
                {
                    _mouseButtons.Remove(mapped);
                }

                try
                {
                    if (!_mouseBlocked)
                    {
                        ulong capture = _source.BeginAcquisition();
                        _source.Mouse(window.Id, new MouseMoveData(new Vector2((int)data.Data3, (int)(data.Data3 >> 32))), capture);
                        _source.Mouse(window.Id, new MouseButtonData(mapped, down), capture);
                    }
                }
                finally
                {
                    if (_mouseButtons.Count == 0)
                    {
                        LinuxWindow? owner = _captureWindow;
                        _captureWindow = null;
                        _mouseBlocked = false;
                        if (owner is not null)
                        {
                            Xlib.XUngrabPointer(_display, 0);
                        }
                    }
                }
            }
        }
    }

    private void CancelMouse()
    {
        LinuxWindow? owner = _captureWindow;
        _captureWindow = null;
        _mouseBlocked = _mouseButtons.Count != 0;
        if (owner is not null)
        {
            foreach (MouseButton button in _mouseButtons)
            {
                _source?.Mouse(owner.Id, new MouseButtonCanceledData(button));
            }

            Xlib.XUngrabPointer(_display, 0);
        }
    }

    private void Execute(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _errors.Add(ex);
        }
    }
}
