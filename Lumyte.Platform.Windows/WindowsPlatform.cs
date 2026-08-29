using Windows.Win32;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Lumyte.Platform.Windows;

public sealed class WindowsPlatform : IPlatform
{
    private readonly HashSet<WindowsWindow> windows = [];
    private bool disposed;

    public WindowsPlatform()
    {
        _ = PInvoke.SetProcessDpiAwareness(PROCESS_DPI_AWARENESS.PROCESS_PER_MONITOR_DPI_AWARE);
        Input = new();
    }

    public WindowsPlatformInput Input { get; }

    IPlatformInput IPlatform.Input => Input;

    public IReadOnlyList<WindowsDisplay> Displays
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return WindowsDisplay.Enumerate();
        }
    }

    IReadOnlyList<IDisplay> IPlatform.Displays => Displays;

    public WindowsWindow CreateWindow(WindowOptions options)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(options);

        var window = new WindowsWindow(options, RemoveWindow);
        windows.Add(window);
        Input.Add(window.WindowInput);
        return window;
    }

    IWindow IPlatform.CreateWindow(WindowOptions options) => CreateWindow(options);

    public bool PumpEvents()
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        bool quitRequested = false;
        while (PInvoke.PeekMessage(out MSG message, default, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_REMOVE))
        {
            if (message.message == PInvoke.WM_QUIT)
            {
                quitRequested = true;
                continue;
            }

            if (!WindowsWindow.PreFilterTextInput(message))
            {
                PInvoke.TranslateMessage(message);
            }

            PInvoke.DispatchMessage(message);
        }

        Input.Update();

        return !quitRequested && windows.Count > 0;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        foreach (WindowsWindow window in windows.ToArray())
        {
            window.Dispose();
        }

        windows.Clear();
    }

    private void RemoveWindow(WindowsWindow window)
    {
        Input.Remove(window.WindowInput);
        windows.Remove(window);
    }
}
