using System.Collections.Concurrent;
using System.ComponentModel;
using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;

using Lumyte.Input;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Input.Pointer;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Lumyte.Platform.Windows;

public sealed class WindowsWindow : IWindow
{
    private const string WindowClassName = "Lumyte.Platform.Window";
    private static readonly WNDPROC s_windowProcedure = WindowProcedure;
    private static readonly object s_registrationLock = new();
    private static readonly ConcurrentDictionary<HWND, WindowsWindow> s_windows = new();
    private static bool s_classRegistered;

    private readonly Action<WindowsWindow> onClosed;
    private GCHandle selfHandle;
    private HWND handle;
    private string title;
    private bool closeRequested;
    private bool closed;
    private bool visible;
    private WindowState state;

    internal unsafe WindowsWindow(WindowOptions options, Action<WindowsWindow> onClosed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Title);
        if (options.ClientSize.Width <= 0 || options.ClientSize.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Client size must be positive.");
        }

        this.onClosed = onClosed;
        title = options.Title;
        state = options.State;
        WindowInput = new(this);
        EnsureWindowClassRegistered();

        WINDOW_STYLE style = WINDOW_STYLE.WS_OVERLAPPEDWINDOW;
        WINDOW_EX_STYLE extendedStyle = WINDOW_EX_STYLE.WS_EX_APPWINDOW;
        RECT bounds = new(0, 0, options.ClientSize.Width, options.ClientSize.Height);
        if (!PInvoke.AdjustWindowRectEx(ref bounds, style, false, extendedStyle))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        selfHandle = GCHandle.Alloc(this);
        nint instancePointer = GCHandle.ToIntPtr(selfHandle);
        handle = PInvoke.CreateWindowEx(
            extendedStyle,
            WindowClassName,
            title,
            style,
            options.Position?.X ?? PInvoke.CW_USEDEFAULT,
            options.Position?.Y ?? PInvoke.CW_USEDEFAULT,
            bounds.right - bounds.left,
            bounds.bottom - bounds.top,
            default,
            default,
            PInvoke.GetModuleHandle((string?)null),
            (void*)instancePointer);

        if (handle.IsNull)
        {
            selfHandle.Free();
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        Clipboard = new WindowsClipboard(GetOpenHandle);

        if (options.IsVisible)
        {
            Show();
        }
    }

    public event EventHandler? CloseRequested;

    public WindowsClipboard Clipboard { get; }

    IClipboard IWindow.Clipboard => Clipboard;

    internal WindowsWindowInput WindowInput { get; }

    public event EventHandler<WindowResizedEventArgs>? Resized;

    public event EventHandler<WindowMovedEventArgs>? Moved;

    public event EventHandler<WindowFocusChangedEventArgs>? FocusChanged;

    public event EventHandler<WindowStateChangedEventArgs>? StateChanged;

    public event EventHandler<WindowScaleFactorChangedEventArgs>? ScaleFactorChanged;

    public string Title
    {
        get => title;
        set
        {
            ObjectDisposedException.ThrowIf(closed, this);
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            if (!PInvoke.SetWindowText(handle, value))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            title = value;
        }
    }

    public Size ClientSize
    {
        get
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (!PInvoke.GetClientRect(handle, out RECT bounds))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            return new(bounds.right - bounds.left, bounds.bottom - bounds.top);
        }
    }

    public Size FramebufferSize => ClientSize;

    public Point Position
    {
        get
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (!PInvoke.GetWindowRect(handle, out RECT bounds))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            return new(bounds.left, bounds.top);
        }
        set
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (!PInvoke.SetWindowPos(
                handle,
                default,
                value.X,
                value.Y,
                0,
                0,
                SET_WINDOW_POS_FLAGS.SWP_NOSIZE
                    | SET_WINDOW_POS_FLAGS.SWP_NOZORDER
                    | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }
    }

    public WindowState State
    {
        get
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (!visible)
            {
                return state;
            }

            if (PInvoke.IsIconic(handle))
            {
                return WindowState.Minimized;
            }

            return PInvoke.IsZoomed(handle) ? WindowState.Maximized : WindowState.Normal;
        }
        set
        {
            ObjectDisposedException.ThrowIf(closed, this);
            state = value;
            if (visible)
            {
                PInvoke.ShowWindow(handle, ToShowCommand(value));
            }
        }
    }

    public float ScaleFactor
    {
        get
        {
            ObjectDisposedException.ThrowIf(closed, this);
            return PInvoke.GetDpiForWindow(handle) / 96f;
        }
    }

    public bool IsFocused
    {
        get
        {
            ObjectDisposedException.ThrowIf(closed, this);
            return PInvoke.GetForegroundWindow() == handle;
        }
    }

    public bool IsCloseRequested => closeRequested;

    public bool IsClosed => closed;

    public nint Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(closed, this);
            return GetHandleValue(handle);
        }
    }

    public void Show()
    {
        ObjectDisposedException.ThrowIf(closed, this);
        visible = true;
        PInvoke.ShowWindow(handle, ToShowCommand(state));
    }

    public void Hide()
    {
        ObjectDisposedException.ThrowIf(closed, this);
        PInvoke.ShowWindow(handle, SHOW_WINDOW_CMD.SW_HIDE);
        visible = false;
    }

    public void Close()
    {
        if (!closed && !PInvoke.DestroyWindow(handle))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    public void Dispose() => Close();

    private static unsafe nint GetHandleValue(HWND window) => (nint)window.Value;

    internal HWND GetOpenHandle()
    {
        ObjectDisposedException.ThrowIf(closed, this);
        return handle;
    }

    private static SHOW_WINDOW_CMD ToShowCommand(WindowState windowState) => windowState switch
    {
        WindowState.Normal => SHOW_WINDOW_CMD.SW_SHOWNORMAL,
        WindowState.Minimized => SHOW_WINDOW_CMD.SW_SHOWMINIMIZED,
        WindowState.Maximized => SHOW_WINDOW_CMD.SW_SHOWMAXIMIZED,
        _ => throw new ArgumentOutOfRangeException(nameof(windowState)),
    };

    private static unsafe void EnsureWindowClassRegistered()
    {
        lock (s_registrationLock)
        {
            if (s_classRegistered)
            {
                return;
            }

            HMODULE module = PInvoke.GetModuleHandle((PCWSTR)null);
            HINSTANCE moduleInstance = new(module.Value);
            fixed (char* className = WindowClassName)
            {
                WNDCLASSEXW windowClass = new()
                {
                    cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                    style = WNDCLASS_STYLES.CS_HREDRAW | WNDCLASS_STYLES.CS_VREDRAW,
                    lpfnWndProc = s_windowProcedure,
                    hInstance = moduleInstance,
                    hCursor = PInvoke.LoadCursor(default, PInvoke.IDC_ARROW),
                    lpszClassName = className,
                };

                if (PInvoke.RegisterClassEx(windowClass) == 0)
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                }
            }

            s_classRegistered = true;
        }
    }

    private static LRESULT WindowProcedure(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        if (message == PInvoke.WM_NCCREATE)
        {
            unsafe
            {
                var create = (CREATESTRUCTW*)lParam.Value;
                var instanceHandle = GCHandle.FromIntPtr((nint)create->lpCreateParams);
                if (instanceHandle.Target is WindowsWindow createdWindow)
                {
                    s_windows[window] = createdWindow;
                }
            }
        }

        if (s_windows.TryGetValue(window, out WindowsWindow? instance))
        {
            return instance.ProcessMessage(window, message, wParam, lParam);
        }

        return PInvoke.DefWindowProc(window, message, wParam, lParam);
    }

    internal static bool PreFilterTextInput(MSG message)
    {
        if (!s_windows.TryGetValue(message.hwnd, out WindowsWindow? window))
        {
            return false;
        }

        return message.message switch
        {
            PInvoke.WM_KEYDOWN or PInvoke.WM_SYSKEYDOWN =>
                window.WindowInput.TextInput.HandleKeyDown(
                    unchecked((ushort)message.wParam.Value),
                    message.lParam.Value),
            PInvoke.WM_KEYUP or PInvoke.WM_SYSKEYUP =>
                window.WindowInput.TextInput.HandleKeyUp(
                    unchecked((ushort)message.wParam.Value),
                    message.lParam.Value),
            _ => false,
        };
    }

    private LRESULT ProcessMessage(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        switch (message)
        {
            case PInvoke.WM_POINTERDOWN:
                if (TryGetTouch(window, wParam, out long startedId, out Vector2 startedPosition, out float? startedPressure))
                {
                    WindowInput.Touchscreen.ChangeTouch(
                        startedId,
                        startedPosition,
                        TouchPhase.Began,
                        startedPressure);
                    return default;
                }

                break;
            case PInvoke.WM_POINTERUPDATE:
                if (TryGetTouch(window, wParam, out long movedId, out Vector2 movedPosition, out float? movedPressure))
                {
                    WindowInput.Touchscreen.ChangeTouch(
                        movedId,
                        movedPosition,
                        TouchPhase.Moved,
                        movedPressure);
                    return default;
                }

                break;
            case PInvoke.WM_POINTERUP:
                if (TryGetTouch(window, wParam, out long endedId, out Vector2 endedPosition, out float? endedPressure))
                {
                    WindowInput.Touchscreen.ChangeTouch(
                        endedId,
                        endedPosition,
                        TouchPhase.Ended,
                        endedPressure);
                    return default;
                }

                break;
            case PInvoke.WM_POINTERCAPTURECHANGED:
                WindowInput.Touchscreen.Cancel(GetPointerId(wParam));
                return default;
            case PInvoke.WM_KEYDOWN:
            case PInvoke.WM_SYSKEYDOWN:
                WindowInput.Keyboard.ChangeKey(GetKey(wParam, lParam), true, IsRepeatedKey(lParam));
                return default;
            case PInvoke.WM_KEYUP:
            case PInvoke.WM_SYSKEYUP:
                WindowInput.Keyboard.ChangeKey(GetKey(wParam, lParam), false, false);
                return default;
            case PInvoke.WM_CHAR:
                WindowInput.TextInput.DispatchCharacter(unchecked((char)wParam.Value));
                return default;
            case PInvoke.WM_MOUSEMOVE:
                WindowInput.Mouse.Move(GetMousePosition(lParam));
                return default;
            case 0x00ff:
                if (WindowsCursor.TryReadDelta(lParam.Value, out int rawX, out int rawY))
                {
                    WindowInput.Mouse.MoveRaw(new(rawX, rawY));
                }

                return default;
            case PInvoke.WM_LBUTTONDOWN:
                WindowInput.Mouse.ChangeButton(MouseButton.Left, true, GetMousePosition(lParam));
                return default;
            case PInvoke.WM_LBUTTONUP:
                WindowInput.Mouse.ChangeButton(MouseButton.Left, false, GetMousePosition(lParam));
                return default;
            case PInvoke.WM_MBUTTONDOWN:
                WindowInput.Mouse.ChangeButton(MouseButton.Middle, true, GetMousePosition(lParam));
                return default;
            case PInvoke.WM_MBUTTONUP:
                WindowInput.Mouse.ChangeButton(MouseButton.Middle, false, GetMousePosition(lParam));
                return default;
            case PInvoke.WM_RBUTTONDOWN:
                WindowInput.Mouse.ChangeButton(MouseButton.Right, true, GetMousePosition(lParam));
                return default;
            case PInvoke.WM_RBUTTONUP:
                WindowInput.Mouse.ChangeButton(MouseButton.Right, false, GetMousePosition(lParam));
                return default;
            case PInvoke.WM_XBUTTONDOWN:
                WindowInput.Mouse.ChangeButton(GetXButton(wParam), true, GetMousePosition(lParam));
                return default;
            case PInvoke.WM_XBUTTONUP:
                WindowInput.Mouse.ChangeButton(GetXButton(wParam), false, GetMousePosition(lParam));
                return default;
            case PInvoke.WM_MOUSEWHEEL:
                WindowInput.Mouse.ChangeWheel(new(0, GetWheelDelta(wParam)));
                return default;
            case PInvoke.WM_MOUSEHWHEEL:
                WindowInput.Mouse.ChangeWheel(new(GetWheelDelta(wParam), 0));
                return default;
            case PInvoke.WM_CLOSE:
                closeRequested = true;
                CloseRequested?.Invoke(this, EventArgs.Empty);
                return default;
            case PInvoke.WM_SIZE:
                int width = unchecked((ushort)(lParam.Value & 0xffff));
                int height = unchecked((ushort)((lParam.Value >> 16) & 0xffff));
                WindowState nextState = wParam.Value switch
                {
                    1 => WindowState.Minimized,
                    2 => WindowState.Maximized,
                    _ => WindowState.Normal,
                };
                if (state != nextState)
                {
                    state = nextState;
                    StateChanged?.Invoke(this, new(nextState));
                }

                Resized?.Invoke(this, new WindowResizedEventArgs(new(width, height)));
                return default;
            case PInvoke.WM_MOVE:
                WindowInput.Mouse.UpdateCursorState();
                Moved?.Invoke(this, new(Position));
                return default;
            case PInvoke.WM_SETFOCUS:
                WindowInput.Mouse.UpdateCursorState();
                FocusChanged?.Invoke(this, new(true));
                return default;
            case PInvoke.WM_KILLFOCUS:
                WindowInput.Mouse.UpdateCursorState();
                WindowInput.Touchscreen.CancelAll();
                FocusChanged?.Invoke(this, new(false));
                return default;
            case PInvoke.WM_DPICHANGED:
                uint dpi = unchecked((ushort)(wParam.Value & 0xffff));
                ScaleFactorChanged?.Invoke(this, new(dpi / 96f));
                return default;
            case PInvoke.WM_NCDESTROY:
                LRESULT result = PInvoke.DefWindowProc(window, message, wParam, lParam);
                closed = true;
                s_windows.TryRemove(window, out _);
                handle = default;
                if (selfHandle.IsAllocated)
                {
                    selfHandle.Free();
                }

                onClosed(this);
                return result;
            default:
                return PInvoke.DefWindowProc(window, message, wParam, lParam);
        }

        return PInvoke.DefWindowProc(window, message, wParam, lParam);
    }

    private static Vector2 GetMousePosition(LPARAM parameter)
    {
        int x = unchecked((short)(parameter.Value & 0xffff));
        int y = unchecked((short)((parameter.Value >> 16) & 0xffff));
        return new(x, y);
    }

    private static float GetWheelDelta(WPARAM parameter)
    {
        short delta = unchecked((short)((parameter.Value >> 16) & 0xffff));
        return delta / 120f;
    }

    private static MouseButton GetXButton(WPARAM parameter) => ((parameter.Value >> 16) & 0xffff) switch
    {
        1 => MouseButton.Button4,
        2 => MouseButton.Button5,
        _ => throw new ArgumentOutOfRangeException(nameof(parameter)),
    };

    private static bool IsRepeatedKey(LPARAM parameter) => (parameter.Value & (1 << 30)) != 0;

    private static uint GetPointerId(WPARAM parameter) => unchecked((ushort)(parameter.Value & 0xffff));

    private static bool TryGetTouch(
        HWND window,
        WPARAM parameter,
        out long id,
        out Vector2 position,
        out float? pressure)
    {
        uint pointerId = GetPointerId(parameter);
        id = pointerId;
        position = default;
        pressure = null;

        if (!PInvoke.GetPointerType(pointerId, out POINTER_INPUT_TYPE pointerType)
            || pointerType != POINTER_INPUT_TYPE.PT_TOUCH
            || !PInvoke.GetPointerTouchInfo(pointerId, out POINTER_TOUCH_INFO touchInfo))
        {
            return false;
        }

        var clientPosition = touchInfo.pointerInfo.ptPixelLocation;
        if (!PInvoke.ScreenToClient(window, ref clientPosition))
        {
            return false;
        }

        position = new(clientPosition.X, clientPosition.Y);
        if ((touchInfo.touchMask & PInvoke.TOUCH_MASK_PRESSURE) != 0)
        {
            pressure = Math.Clamp(touchInfo.pressure / 1024f, 0, 1);
        }

        return true;
    }

    private static Key GetKey(WPARAM virtualKeyParameter, LPARAM keyData)
    {
        uint virtualKey = unchecked((uint)virtualKeyParameter.Value);
        bool extended = (keyData.Value & (1 << 24)) != 0;
        int scanCode = (int)((keyData.Value >> 16) & 0xff);

        if (virtualKey is >= 0x30 and <= 0x39)
        {
            return Key.D0 + (int)(virtualKey - 0x30);
        }

        if (virtualKey is >= 0x41 and <= 0x5a)
        {
            return Key.A + (int)(virtualKey - 0x41);
        }

        if (virtualKey is >= 0x60 and <= 0x69)
        {
            return Key.NumPad0 + (int)(virtualKey - 0x60);
        }

        if (virtualKey is >= 0x70 and <= 0x87)
        {
            return Key.F1 + (int)(virtualKey - 0x70);
        }

        return virtualKey switch
        {
            0x08 => Key.Backspace,
            0x09 => Key.Tab,
            0x0d => Key.Enter,
            0x13 => Key.Pause,
            0x14 => Key.CapsLock,
            0x1b => Key.Escape,
            0x20 => Key.Space,
            0x21 => Key.PageUp,
            0x22 => Key.PageDown,
            0x23 => Key.End,
            0x24 => Key.Home,
            0x25 => Key.Left,
            0x26 => Key.Up,
            0x27 => Key.Right,
            0x28 => Key.Down,
            0x2c => Key.PrintScreen,
            0x2d => Key.Insert,
            0x2e => Key.Delete,
            0x5b => Key.LeftSuper,
            0x5c => Key.RightSuper,
            0x5d => Key.Menu,
            0x6a => Key.NumPadMultiply,
            0x6b => Key.NumPadAdd,
            0x6d => Key.NumPadSubtract,
            0x6e => Key.NumPadDecimal,
            0x6f => Key.NumPadDivide,
            0x90 => Key.NumLock,
            0x91 => Key.ScrollLock,
            0x10 => scanCode == 0x36 ? Key.RightShift : Key.LeftShift,
            0x11 => extended ? Key.RightControl : Key.LeftControl,
            0x12 => extended ? Key.RightAlt : Key.LeftAlt,
            0xba => Key.Semicolon,
            0xbb => Key.Equal,
            0xbc => Key.Comma,
            0xbd => Key.Minus,
            0xbe => Key.Period,
            0xbf => Key.Slash,
            0xc0 => Key.GraveAccent,
            0xdb => Key.LeftBracket,
            0xdc => Key.Backslash,
            0xdd => Key.RightBracket,
            0xde => Key.Apostrophe,
            _ => Key.Unknown,
        };
    }
}
