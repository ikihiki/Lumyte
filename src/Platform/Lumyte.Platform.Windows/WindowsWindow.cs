using System.ComponentModel;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Lumyte.Windowing.Abstractions;

namespace Lumyte.Platform.Windows;

/// <summary>A native Windows window implementing the common contract directly.</summary>
[SupportedOSPlatform("windows10.0.17763")]
public sealed class WindowsWindow : IWindow
{
    private readonly WindowsPlatform _platform;
    private readonly uint _style;
    private readonly uint _extendedStyle;
    private readonly WindowDescriptor _descriptor;
    private readonly HashSet<WindowRequestId> _pending = [];
    private nint _handle;
    private WindowState _state;
    private ulong _nextRequest;
    private int _retained;
    private bool _disposed;
    private WindowRequestId? _close;

    internal WindowsWindow(WindowsPlatform platform, WindowId id, WindowDescriptor descriptor, string className)
    {
        _platform = platform;
        _descriptor = descriptor;
        Id = id;
        _style = descriptor.Kind == WindowKind.Popup ? 0x80000000 : 0x00C80000;
        if (descriptor.Kind != WindowKind.Popup)
        {
            _style |= 0x00020000;
            if (descriptor.IsResizable)
            {
                _style |= 0x00050000;
            }
        }

        _extendedStyle = descriptor.Kind == WindowKind.Tool ? 0x80U : 0U;
        if (descriptor.Activation == WindowActivation.PreserveFocus)
        {
            _extendedStyle |= 0x08000000;
        }

        var rect = new NativeMethods.Rect { Right = (int)Math.Ceiling(descriptor.ClientSize.X), Bottom = (int)Math.Ceiling(descriptor.ClientSize.Y) };
        CheckNative(NativeMethods.AdjustWindowRectExForDpi(ref rect, _style, false, _extendedStyle, 96));
        _handle = NativeMethods.CreateWindowExW(_extendedStyle, className, descriptor.Title, _style, int.MinValue, int.MinValue, rect.Right - rect.Left, rect.Bottom - rect.Top, (descriptor.Owner as WindowsWindow)?._handle ?? 0, 0, platform.Instance, 0);
        if (_handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        WindowFeatures features = WindowFeatures.OwnedWindow | WindowFeatures.NonActivating | WindowFeatures.AnchorPlacement;
        if (descriptor.Kind != WindowKind.Popup)
        {
            features |= WindowFeatures.Minimize;
            if (descriptor.IsResizable)
            {
                features |= WindowFeatures.Maximize;
            }
        }

        _state = new WindowState
        {
            Id = id,
            Owner = descriptor.Owner?.Id,
            Kind = descriptor.Kind,
            Activation = descriptor.Activation,
            Title = descriptor.Title,
            ClientSize = descriptor.ClientSize,
            PixelSize = default,
            Scale = Vector2.One,
            IsVisible = false,
            IsActive = false,
            DisplayMode = WindowDisplayMode.Normal,
            Lifetime = WindowLifetime.Open,
            Features = features,
        };
        Refresh();
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

    /// <summary>Gets the borrowed HWND; retain the native target while using it externally.</summary>
    public nint Hwnd
    {
        get
        {
            Check();
            if (_handle == 0)
            {
                throw new InvalidOperationException("The native window has closed.");
            }

            return _handle;
        }
    }

    /// <summary>Gets the borrowed module handle associated with this window.</summary>
    public nint HInstance
    {
        get
        {
            Check();
            return _platform.Instance;
        }
    }

    internal WindowsPlatform Platform => _platform;

    /// <inheritdoc/>
    public WindowRequestId Show() => Request(() =>
    {
        NativeMethods.ShowWindow(_handle, _descriptor.Activation == WindowActivation.PreserveFocus ? 4 : 5);
        Refresh();
        return _state.IsVisible ? WindowRequestStatus.Applied : WindowRequestStatus.Denied;
    });

    /// <inheritdoc/>
    public WindowRequestId Hide() => Request(() =>
    {
        NativeMethods.ShowWindow(_handle, 0);
        Refresh();
        return !_state.IsVisible ? WindowRequestStatus.Applied : WindowRequestStatus.Denied;
    });

    /// <inheritdoc/>
    public WindowRequestId SetTitle(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        if (title.Contains('\0'))
        {
            throw new ArgumentException("Title cannot contain NUL.", nameof(title));
        }

        return Request(() =>
        {
            CheckNative(NativeMethods.SetWindowTextW(_handle, title));
            SetState(_state with { Title = title });
            return WindowRequestStatus.Applied;
        });
    }

    /// <inheritdoc/>
    public WindowRequestId Resize(Vector2 clientSize)
    {
        ValidateSize(clientSize);
        return Request(() =>
        {
            float scale = _state.Scale.X;
            var rect = new NativeMethods.Rect { Right = checked((int)Math.Ceiling(clientSize.X * scale)), Bottom = checked((int)Math.Ceiling(clientSize.Y * scale)) };
            CheckNative(NativeMethods.AdjustWindowRectExForDpi(ref rect, _style, false, _extendedStyle, (uint)Math.Round(scale * 96)));
            CheckNative(NativeMethods.SetWindowPos(_handle, 0, 0, 0, rect.Right - rect.Left, rect.Bottom - rect.Top, 0x16));
            Refresh();
            return WindowRequestStatus.Applied;
        });
    }

    /// <inheritdoc/>
    public WindowRequestId Place(WindowPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        WindowRect a = placement.Anchor;
        if (!Enum.IsDefined(placement.PreferredSide) || !float.IsFinite(a.X) || !float.IsFinite(a.Y)
            || !float.IsFinite(a.Width) || !float.IsFinite(a.Height) || a.Width <= 0 || a.Height <= 0
            || !float.IsFinite(placement.Gap) || placement.Gap < 0)
        {
            throw new ArgumentException("Invalid anchor placement.", nameof(placement));
        }

        return Request(() =>
        {
            if (_descriptor.Owner is not WindowsWindow owner || owner.State.Lifetime != WindowLifetime.Open)
            {
                return WindowRequestStatus.Denied;
            }

            float scale = owner.State.Scale.X;
            var origin = new NativeMethods.Point { X = checked((int)Math.Round(a.X * scale)), Y = checked((int)Math.Round(a.Y * scale)) };
            CheckNative(NativeMethods.ClientToScreen(owner.Hwnd, ref origin));
            CheckNative(NativeMethods.GetWindowRect(_handle, out NativeMethods.Rect rect));
            var info = new NativeMethods.MonitorInfo { Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfo>() };
            CheckNative(NativeMethods.GetMonitorInfoW(NativeMethods.MonitorFromWindow(owner.Hwnd, 2), ref info));
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            int gap = checked((int)Math.Round(placement.Gap * scale));
            int anchorWidth = checked((int)Math.Round(a.Width * scale));
            int anchorHeight = checked((int)Math.Round(a.Height * scale));
            (int X, int Y) Position(WindowPlacementSide side) => side switch
            {
                WindowPlacementSide.Below => (origin.X, checked(origin.Y + anchorHeight + gap)),
                WindowPlacementSide.Above => (origin.X, checked(origin.Y - height - gap)),
                WindowPlacementSide.Right => (checked(origin.X + anchorWidth + gap), origin.Y),
                _ => (checked(origin.X - width - gap), origin.Y),
            };
            bool Fits((int X, int Y) p) => p.X >= info.Work.Left && p.Y >= info.Work.Top
                && (long)p.X + width <= info.Work.Right && (long)p.Y + height <= info.Work.Bottom;
            (int X, int Y) position = Position(placement.PreferredSide);
            if (!Fits(position))
            {
                WindowPlacementSide opposite = placement.PreferredSide switch
                {
                    WindowPlacementSide.Below => WindowPlacementSide.Above,
                    WindowPlacementSide.Above => WindowPlacementSide.Below,
                    WindowPlacementSide.Right => WindowPlacementSide.Left,
                    _ => WindowPlacementSide.Right,
                };
                position = Position(opposite);
            }

            int x = Math.Clamp(position.X, info.Work.Left, Math.Max(info.Work.Left, info.Work.Right - width));
            int y = Math.Clamp(position.Y, info.Work.Top, Math.Max(info.Work.Top, info.Work.Bottom - height));
            CheckNative(NativeMethods.SetWindowPos(_handle, 0, x, y, 0, 0, 0x15));
            Refresh();
            return WindowRequestStatus.Applied;
        });
    }

    /// <inheritdoc/>
    public WindowRequestId Activate() => Request(() => _descriptor.Activation == WindowActivation.PreserveFocus
        ? WindowRequestStatus.Denied : NativeMethods.SetForegroundWindow(_handle) ? ObserveActivation() : WindowRequestStatus.Denied);

    /// <inheritdoc/>
    public WindowRequestId BringToFront() => Request(() =>
    {
        CheckNative(NativeMethods.SetWindowPos(_handle, 0, 0, 0, 0, 0, 0x13));
        Refresh();
        return WindowRequestStatus.Applied;
    });

    /// <inheritdoc/>
    public WindowRequestId SetDisplayMode(WindowDisplayMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        return Request(() =>
        {
            WindowFeatures required = mode switch
            {
                WindowDisplayMode.Minimized => WindowFeatures.Minimize,
                WindowDisplayMode.Maximized => WindowFeatures.Maximize,
                WindowDisplayMode.Fullscreen => WindowFeatures.Fullscreen,
                _ => WindowFeatures.None,
            };
            if (required != WindowFeatures.None && !_state.Features.HasFlag(required))
            {
                return WindowRequestStatus.Unsupported;
            }

            int command = mode switch
            {
                WindowDisplayMode.Minimized => 6,
                WindowDisplayMode.Maximized => 3,
                _ => _descriptor.Activation == WindowActivation.PreserveFocus ? 4 : 9,
            };
            NativeMethods.ShowWindow(_handle, command);
            Refresh();
            return _state.DisplayMode == mode ? WindowRequestStatus.Applied : WindowRequestStatus.Denied;
        });
    }

    /// <inheritdoc/>
    public WindowRequestId Close()
    {
        CheckMutation();
        if (_close is not null)
        {
            throw new InvalidOperationException("Closure has already been requested.");
        }

        WindowRequestId id = NextRequest();
        _close = id;
        _platform.Enqueue(() =>
        {
            if (_disposed || _state.Lifetime == WindowLifetime.Closed)
            {
                Finish(id, WindowRequestStatus.Superseded);
            }
            else if (HasLiveChildren())
            {
                _close = null;
                Finish(id, WindowRequestStatus.Denied, "Close owned native children first.");
            }
            else
            {
                SetState(_state with { Lifetime = WindowLifetime.Closing });
                AdvanceClose();
            }
        });
        return id;
    }

    /// <inheritdoc/>
    public IDisposable RetainNativeTarget()
    {
        CheckMutation();
        if (_state.Lifetime != WindowLifetime.Open)
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
        if (_retained != 0 || HasLiveChildren())
        {
            throw new InvalidOperationException("Release retained targets and native children first.");
        }

        nint handle = _handle;
        if (handle != 0)
        {
            CheckNative(NativeMethods.DestroyWindow(handle));
        }

        _disposed = true;
        foreach (WindowRequestId request in _pending.ToArray())
        {
            Finish(request, WindowRequestStatus.Superseded);
        }

        _platform.Release(this, handle);
    }

    internal static void Validate(WindowDescriptor descriptor, WindowsPlatform platform)
    {
        ValidateSize(descriptor.ClientSize);
        if (descriptor.Title is null || descriptor.Title.Contains('\0') || !Enum.IsDefined(descriptor.Kind) || !Enum.IsDefined(descriptor.Activation))
        {
            throw new ArgumentException("Invalid window descriptor.", nameof(descriptor));
        }

        if (descriptor.Owner is not null && (descriptor.Owner is not WindowsWindow owner || owner._platform != platform
            || owner.State.Lifetime != WindowLifetime.Open || owner._close is not null))
        {
            throw new ArgumentException("Owner must be an open native window on this platform.", nameof(descriptor));
        }
    }

    internal void AdvanceClose()
    {
        if (_disposed || _close is not { } request || _state.Lifetime != WindowLifetime.Closing || _retained != 0)
        {
            return;
        }

        try
        {
            CheckNative(NativeMethods.DestroyWindow(_handle));
            Finish(request, WindowRequestStatus.Applied);
        }
        catch (Exception ex)
        {
            _close = null;
            SetState(_state with { Lifetime = WindowLifetime.Open });
            Finish(request, WindowRequestStatus.Failed, ex.Message);
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
                catch (Exception ex)
                {
                    args.Cancel = true;
                    _platform.Notify(() => throw new InvalidOperationException("Closing subscriber failed; closure was deferred.", ex));
                }
            }
        }

        if (!args.Cancel && _close is null && _state.Lifetime == WindowLifetime.Open)
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

    internal void Observe(uint message, nint lParam)
    {
        if (message == 0x82)
        {
            nint handle = _handle;
            _handle = 0;
            _platform.ForgetHandle(handle);
            SetState(_state with { Lifetime = WindowLifetime.Closed, IsVisible = false, IsActive = false });
        }
        else if (message == 0x2E0)
        {
            NativeMethods.Rect suggested = Marshal.PtrToStructure<NativeMethods.Rect>(lParam);
            CheckNative(NativeMethods.SetWindowPos(_handle, 0, suggested.Left, suggested.Top, suggested.Right - suggested.Left, suggested.Bottom - suggested.Top, 0x14));
            Refresh();
        }
        else if (message is 0x5 or 0x6 or 0x7 or 0x8 or 0x18 or 0x47)
        {
            Refresh();
        }
    }

    private static void ValidateSize(Vector2 size)
    {
        if (!float.IsFinite(size.X) || !float.IsFinite(size.Y) || size.X <= 0 || size.Y <= 0 || size.X > 100000 || size.Y > 100000)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }
    }

    private static void CheckNative(bool result)
    {
        if (!result)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    private WindowRequestStatus ObserveActivation()
    {
        Refresh();
        return _state.IsActive ? WindowRequestStatus.Applied : WindowRequestStatus.Denied;
    }

    private bool HasLiveChildren() => _platform.Windows.Any(w => w._state.Owner == Id && w._state.Lifetime != WindowLifetime.Closed);

    private void Refresh()
    {
        if (_handle == 0)
        {
            return;
        }

        CheckNative(NativeMethods.GetClientRect(_handle, out NativeMethods.Rect rect));
        float scale = Math.Max(1, NativeMethods.GetDpiForWindow(_handle)) / 96f;
        var pixels = new WindowPixelSize(Math.Max(0, rect.Right - rect.Left), Math.Max(0, rect.Bottom - rect.Top));
        SetState(_state with
        {
            PixelSize = pixels,
            Scale = new Vector2(scale),
            ClientSize = new Vector2(pixels.Width / scale, pixels.Height / scale),
            IsVisible = NativeMethods.IsWindowVisible(_handle),
            IsActive = NativeMethods.GetForegroundWindow() == _handle,
            DisplayMode = NativeMethods.IsIconic(_handle) ? WindowDisplayMode.Minimized : NativeMethods.IsZoomed(_handle) ? WindowDisplayMode.Maximized : WindowDisplayMode.Normal,
        });
    }

    private void SetState(WindowState state)
    {
        if (_state == state)
        {
            return;
        }

        _state = state;
        _platform.Notify(() => _platform.NotifyHandlers(StateChanged, state));
    }

    private WindowRequestId NextRequest()
    {
        var id = new WindowRequestId(checked(++_nextRequest));
        _pending.Add(id);
        return id;
    }

    private WindowRequestId Request(Func<WindowRequestStatus> operation)
    {
        CheckMutation();
        if (_state.Lifetime != WindowLifetime.Open || _close is not null)
        {
            throw new InvalidOperationException("The window is closing or closed.");
        }

        WindowRequestId id = NextRequest();
        _platform.Enqueue(() =>
        {
            if (_disposed || _state.Lifetime != WindowLifetime.Open)
            {
                Finish(id, WindowRequestStatus.Superseded);
                return;
            }

            try
            {
                Finish(id, operation());
            }
            catch (Exception ex)
            {
                Finish(id, WindowRequestStatus.Failed, ex.Message);
            }
        });
        return id;
    }

    private void Finish(WindowRequestId id, WindowRequestStatus status, string? error = null)
    {
        if (_pending.Remove(id))
        {
            var result = new WindowRequestResult(Id, id, status, error);
            _platform.Notify(() => _platform.NotifyHandlers(RequestCompleted, result));
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

    private sealed class Retention(WindowsWindow window) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            window.CheckMutation();
            window._retained--;
            _disposed = true;
        }
    }
}
