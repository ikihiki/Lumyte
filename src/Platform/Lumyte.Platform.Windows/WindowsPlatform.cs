using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Lumyte.Input.Abstractions;
using Lumyte.Platform.Abstractions;
using Lumyte.Windowing.Abstractions;

namespace Lumyte.Platform.Windows;

/// <summary>Creates native Windows services and drains one UI thread's OS queue.</summary>
/// <remarks>Create and use on an STA thread. Dispose services and windows before the platform.</remarks>
[SupportedOSPlatform("windows10.0.17763")]
public sealed class WindowsPlatform : IPlatform
{
    [ThreadStatic]
    private static bool _threadClaimed;

    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly NativeMethods.WindowProcedure _procedure;
    private readonly string _className = "Lumyte." + Guid.NewGuid().ToString("N");
    private readonly Dictionary<nint, WindowsWindow> _handles = [];
    private readonly HashSet<WindowsWindow> _windows = [];
    private readonly Dictionary<nint, Action<uint>> _hosts = [];
    private readonly Queue<Action> _operations = new();
    private readonly Queue<Action> _notifications = new();
    private readonly List<Exception> _errors = [];
    private readonly nint _oldDpiContext;
    private ulong _nextWindow;
    private bool _disposed;
    private bool _pumping;
    private bool _notifying;
    private WindowsInputSource? _source;

    /// <summary>Initializes a new instance of the <see cref="WindowsPlatform"/> class.</summary>
    public WindowsPlatform()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            throw new PlatformNotSupportedException("Windows 10 version 1809 or later is required.");
        }

        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            throw new InvalidOperationException("WindowsPlatform requires an STA thread.");
        }

        if (_threadClaimed)
        {
            throw new InvalidOperationException("Only one WindowsPlatform is allowed per UI thread.");
        }

        Instance = NativeMethods.GetModuleHandleW(null);
        _procedure = Dispatch;
        _oldDpiContext = NativeMethods.SetThreadDpiAwarenessContext(-4);
        var windowClass = new NativeMethods.WindowClass
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.WindowClass>(),
            Procedure = _procedure,
            Instance = Instance,
            Cursor = NativeMethods.LoadCursorW(0, 32512),
            ClassName = _className,
        };
        if (NativeMethods.RegisterClassExW(ref windowClass) == 0)
        {
            NativeMethods.SetThreadDpiAwarenessContext(_oldDpiContext);
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        _threadClaimed = true;
    }

    /// <summary>Gets the unique platform scope.</summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>Gets supported platform services.</summary>
    public PlatformFeatures Features => PlatformFeatures.NativeWindows | PlatformFeatures.DeviceInput | PlatformFeatures.Clipboard;

    /// <summary>Gets a value indicating whether the thread received WM_QUIT.</summary>
    public bool IsQuitRequested { get; private set; }

    internal nint Instance { get; }

    internal IEnumerable<WindowsWindow> Windows => _windows;

    /// <inheritdoc/>
    public IWindow CreateWindow(WindowDescriptor descriptor)
    {
        CheckMutation();
        ArgumentNullException.ThrowIfNull(descriptor);
        WindowsWindow.Validate(descriptor, this);
        var window = new WindowsWindow(this, new WindowId(Id, checked(++_nextWindow)), descriptor, _className);
        _handles.Add(window.Hwnd, window);
        _windows.Add(window);
        return window;
    }

    /// <inheritdoc/>
    public IInputSource CreateInputSource()
    {
        CheckMutation();
        if (_source is not null)
        {
            throw new InvalidOperationException("Only one input source can acquire this thread's input.");
        }

        _source = new WindowsInputSource(this);
        return _source;
    }

    /// <inheritdoc/>
    public IClipboard CreateClipboard()
    {
        CheckMutation();
        return new WindowsClipboard(this);
    }

    /// <inheritdoc/>
    public ITextInputService CreateTextInputService()
    {
        CheckMutation();
        throw new NotSupportedException("Windows text input is not included in this release.");
    }

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
            int operations = _operations.Count;
            for (int i = 0; i < operations; i++)
            {
                Execute(_operations.Dequeue());
            }

            while (NativeMethods.PeekMessageW(out NativeMethods.Message message, 0, 0, 0, 1))
            {
                if (message.Id == 0x12)
                {
                    IsQuitRequested = true;
                    continue;
                }

                if (message.Id is 0x100 or 0x101 or 0x104 or 0x105 && _handles.TryGetValue(message.Window, out WindowsWindow? target))
                {
                    _source?.Receive(target, message.Id, message.WParam, message.LParam);
                }

                NativeMethods.TranslateMessage(ref message);
                NativeMethods.DispatchMessageW(ref message);
            }

            foreach (WindowsWindow window in _windows.ToArray())
            {
                Execute(window.AdvanceClose);
            }

            while (_notifications.TryDequeue(out Action? notification))
            {
                _notifying = true;
                try
                {
                    Execute(notification);
                }
                finally
                {
                    _notifying = false;
                }
            }
        }
        finally
        {
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
        if (_pumping || _windows.Count > 0 || _hosts.Count > 0 || _source is not null)
        {
            throw new InvalidOperationException("Dispose all created services and windows before the platform.");
        }

        if (!NativeMethods.UnregisterClassW(_className, Instance))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        NativeMethods.SetThreadDpiAwarenessContext(_oldDpiContext);
        _threadClaimed = false;
        _disposed = true;
    }

    internal void Check()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Environment.CurrentManagedThreadId != _thread)
        {
            throw new InvalidOperationException("Use the creating STA thread.");
        }
    }

    internal void CheckMutation()
    {
        Check();
        if (_notifying)
        {
            throw new InvalidOperationException("Mutation during native notifications is forbidden.");
        }
    }

    internal T InNativeCallback<T>(Func<T> action)
    {
        bool previous = _notifying;
        _notifying = true;
        try
        {
            return action();
        }
        finally
        {
            _notifying = previous;
        }
    }

    internal void Enqueue(Action operation) => _operations.Enqueue(operation);

    internal void Notify(Action notification) => _notifications.Enqueue(notification);

    internal void NotifyHandlers(Action? handlers)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (Action handler in handlers.GetInvocationList().Cast<Action>())
        {
            Execute(handler);
        }
    }

    internal void NotifyHandlers<T>(Action<T>? handlers, T value)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (Action<T> handler in handlers.GetInvocationList().Cast<Action<T>>())
        {
            Execute(() => handler(value));
        }
    }

    internal void Release(WindowsWindow window, nint handle)
    {
        _handles.Remove(handle);
        _windows.Remove(window);
    }

    internal void ForgetHandle(nint handle) => _handles.Remove(handle);

    internal void StopInput(WindowsWindow window)
    {
        if (window.State.IsActive)
        {
            _source?.Deactivate();
        }
    }

    internal void Release(WindowsInputSource source)
    {
        if (ReferenceEquals(_source, source))
        {
            _source = null;
        }
    }

    internal nint CreateHost(Action<uint> callback)
    {
        nint handle = NativeMethods.CreateWindowExW(0, _className, string.Empty, 0, 0, 0, 0, 0, -3, 0, Instance, 0);
        if (handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        _hosts.Add(handle, callback);
        return handle;
    }

    internal void DestroyHost(nint handle)
    {
        if (!NativeMethods.DestroyWindow(handle))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        _hosts.Remove(handle);
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

    private nint Dispatch(nint handle, uint message, nuint wParam, nint lParam)
    {
        // Managed exceptions must never unwind through user32.
        try
        {
            if (_hosts.TryGetValue(handle, out Action<uint>? callback))
            {
                callback(message);
            }

            if (_handles.TryGetValue(handle, out WindowsWindow? window))
            {
                if (message == 0x10)
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

                    return 0;
                }

                if (message == 0x21 && window.State.Activation == WindowActivation.PreserveFocus)
                {
                    return 3;
                }

                window.Observe(message, lParam);
                if (message is not (0x100 or 0x101 or 0x104 or 0x105))
                {
                    _source?.Receive(window, message, wParam, lParam);
                }
            }
        }
        catch (Exception ex)
        {
            _errors.Add(ex);
        }

        return NativeMethods.DefWindowProcW(handle, message, wParam, lParam);
    }
}
