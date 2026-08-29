using Lumyte.Platform;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace Lumyte.Platform.SilkNet;

public sealed class SilkPlatform : IPlatform
{
    private readonly HashSet<SilkWindow> windows = [];
    private bool disposed;

    public SilkPlatform()
    {
        Input = new();
    }

    public SilkPlatformInput Input { get; }

    IPlatformInput IPlatform.Input => Input;

    public IReadOnlyList<SilkDisplay> Displays
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return [.. Silk.NET.Windowing.Monitor.GetMonitors(null).Select((monitor, index) => new SilkDisplay(monitor, index == 0))];
        }
    }

    IReadOnlyList<IDisplay> IPlatform.Displays => Displays;

    public SilkWindow CreateWindow(Lumyte.Platform.WindowOptions options)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(options);
        Silk.NET.Windowing.WindowOptions nativeOptions = Silk.NET.Windowing.WindowOptions.Default with
        {
            API = GraphicsAPI.None,
            IsVisible = options.IsVisible,
            Size = new(options.ClientSize.Width, options.ClientSize.Height),
            WindowState = SilkConversions.ToSilk(options.State),
            Title = options.Title,
        };
        if (options.Position is { } position)
        {
            nativeOptions.Position = new Vector2D<int>(position.X, position.Y);
        }

        Silk.NET.Windowing.IWindow native = Window.Create(nativeOptions);
        native.Initialize();
        var window = new SilkWindow(native, Input, RemoveWindow);
        windows.Add(window);
        Input.Add(window.WindowInput);
        return window;
    }

    Lumyte.Platform.IWindow IPlatform.CreateWindow(Lumyte.Platform.WindowOptions options) =>
        CreateWindow(options);

    public bool PumpEvents()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        foreach (SilkWindow window in windows.ToArray())
        {
            window.DoEvents();
        }

        return windows.Count > 0;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        foreach (SilkWindow window in windows.ToArray())
        {
            window.Dispose();
        }

        windows.Clear();
        Input.Dispose();
    }

    private void RemoveWindow(SilkWindow window)
    {
        Input.Remove(window.WindowInput);
        windows.Remove(window);
    }
}
