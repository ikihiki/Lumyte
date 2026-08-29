using System.Drawing;

using Lumyte.Platform;

namespace Lumyte.Interaction.Tests;

internal sealed class VirtualWindow : IWindow
{
    public IClipboard Clipboard { get; } = new VirtualClipboard();

    public event EventHandler? CloseRequested
    {
        add { }
        remove { }
    }

    public event EventHandler<WindowResizedEventArgs>? Resized
    {
        add { }
        remove { }
    }

    public event EventHandler<WindowMovedEventArgs>? Moved
    {
        add { }
        remove { }
    }

    public event EventHandler<WindowFocusChangedEventArgs>? FocusChanged;

    public event EventHandler<WindowStateChangedEventArgs>? StateChanged
    {
        add { }
        remove { }
    }

    public event EventHandler<WindowScaleFactorChangedEventArgs>? ScaleFactorChanged
    {
        add { }
        remove { }
    }

    public string Title { get; set; } = "Virtual window";

    public Size ClientSize { get; } = new(1280, 720);

    public Size FramebufferSize => ClientSize;

    public Point Position { get; set; }

    public WindowState State { get; set; }

    public float ScaleFactor => 1;

    public bool IsFocused { get; private set; } = true;

    public bool IsCloseRequested => false;

    public bool IsClosed => false;

    public void SetFocus(bool isFocused)
    {
        if (IsFocused == isFocused)
        {
            return;
        }

        IsFocused = isFocused;
        FocusChanged?.Invoke(this, new(isFocused));
    }

    public void Show()
    {
    }

    public void Hide()
    {
    }

    public void Close()
    {
    }

    public void Dispose()
    {
    }

    private sealed class VirtualClipboard : IClipboard
    {
        private string? text;

        public string? GetText() => text;

        public void SetText(string text) => this.text = text;
    }
}
