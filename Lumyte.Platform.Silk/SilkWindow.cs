using System.Drawing;

using Lumyte.Platform;
using Silk.NET.Input;
using Silk.NET.Maths;
using NativeWindow = Silk.NET.Windowing.IWindow;

namespace Lumyte.Platform.SilkNet;

public sealed class SilkWindow : IWindow
{
    private readonly NativeWindow native;
    private readonly Action<SilkWindow> removed;
    private bool closeRequested;
    private bool disposed;

    internal SilkWindow(
        NativeWindow native,
        SilkPlatformInput platformInput,
        Action<SilkWindow> removed)
    {
        this.native = native;
        this.removed = removed;
        WindowInput = new(this, native.CreateInput());
        Clipboard = new SilkClipboard(WindowInput);
        native.Closing += OnClosing;
        native.Resize += size => Resized?.Invoke(this, new(new(size.X, size.Y)));
        native.Move += position => Moved?.Invoke(this, new(new(position.X, position.Y)));
        native.FocusChanged += focused =>
        {
            IsFocused = focused;
            FocusChanged?.Invoke(this, new(focused));
        };
        native.StateChanged += state => StateChanged?.Invoke(this, new(SilkConversions.FromSilk(state)));
        native.FramebufferResize += _ => ScaleFactorChanged?.Invoke(this, new(ScaleFactor));
    }

    public event EventHandler? CloseRequested;

    public event EventHandler<WindowResizedEventArgs>? Resized;

    public event EventHandler<WindowMovedEventArgs>? Moved;

    public event EventHandler<WindowFocusChangedEventArgs>? FocusChanged;

    public event EventHandler<WindowStateChangedEventArgs>? StateChanged;

    public event EventHandler<WindowScaleFactorChangedEventArgs>? ScaleFactorChanged;

    public SilkWindowInput WindowInput { get; }

    public NativeWindow Native => native;

    public SilkClipboard Clipboard { get; }

    IClipboard IWindow.Clipboard => Clipboard;

    public string Title
    {
        get => native.Title;
        set => native.Title = value;
    }

    public Size ClientSize => new(native.Size.X, native.Size.Y);

    public Size FramebufferSize => new(native.FramebufferSize.X, native.FramebufferSize.Y);

    public Point Position
    {
        get => new(native.Position.X, native.Position.Y);
        set => native.Position = new Vector2D<int>(value.X, value.Y);
    }

    public WindowState State
    {
        get => SilkConversions.FromSilk(native.WindowState);
        set => native.WindowState = SilkConversions.ToSilk(value);
    }

    public float ScaleFactor => ClientSize.Width > 0
        ? (float)FramebufferSize.Width / ClientSize.Width
        : 1;

    public bool IsFocused { get; private set; }

    public bool IsCloseRequested => closeRequested;

    public bool IsClosed => disposed;

    public void Show() => native.IsVisible = true;

    public void Hide() => native.IsVisible = false;

    public void Close() => native.Close();

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        WindowInput.Dispose();
        native.Dispose();
        removed(this);
    }

    internal void DoEvents()
    {
        if (!disposed)
        {
            native.DoEvents();
        }
    }

    private void OnClosing()
    {
        closeRequested = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
        Dispose();
    }
}
