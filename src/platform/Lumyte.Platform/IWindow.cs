using System.Drawing;

namespace Lumyte.Platform;

public interface IWindow : IDisposable
{
    IClipboard Clipboard { get; }

    event EventHandler? CloseRequested;

    event EventHandler<WindowResizedEventArgs>? Resized;

    event EventHandler<WindowMovedEventArgs>? Moved;

    event EventHandler<WindowFocusChangedEventArgs>? FocusChanged;

    event EventHandler<WindowStateChangedEventArgs>? StateChanged;

    event EventHandler<WindowScaleFactorChangedEventArgs>? ScaleFactorChanged;

    string Title { get; set; }

    Size ClientSize { get; }

    Size FramebufferSize { get; }

    Point Position { get; set; }

    WindowState State { get; set; }

    float ScaleFactor { get; }

    bool IsFocused { get; }

    bool IsCloseRequested { get; }

    bool IsClosed { get; }

    void Show();

    void Hide();

    void Close();
}
