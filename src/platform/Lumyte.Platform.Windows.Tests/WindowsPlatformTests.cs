using System.Drawing;

using Xunit;

namespace Lumyte.Platform.Windows.Tests;

public sealed class WindowsPlatformTests
{
    [Fact]
    public void CreatesAndClosesAHiddenWindow()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var platform = new WindowsPlatform();
        Assert.Same(platform.Input, ((IPlatform)platform).Input);
        IReadOnlyList<WindowsDisplay> displays = platform.Displays;
        using WindowsWindow window = platform.CreateWindow(new()
        {
            Title = "Lumyte platform test",
            ClientSize = new(320, 240),
            IsVisible = false,
        });

        Assert.Equal("Lumyte platform test", window.Title);
        Assert.Equal(new Size(320, 240), window.ClientSize);
        Assert.Equal(window.ClientSize, window.FramebufferSize);
        Assert.True(window.ScaleFactor > 0);
        Assert.Equal(WindowState.Normal, window.State);
        Assert.False(window.IsClosed);
        Assert.NotEqual(0, window.Handle);
        Assert.Same(window.Clipboard, ((IWindow)window).Clipboard);
        WindowsWindowInput windowInput = platform.Input.GetWindow(window);
        Assert.Same(window, windowInput.Window);
        Assert.Same(windowInput.Keyboard, Assert.Single(windowInput.Keyboards));
        Assert.Same(windowInput.Mouse, Assert.Single(windowInput.Mice));
        Assert.Same(windowInput.Touchscreen, Assert.Single(windowInput.Touchscreens));
        Assert.Same(windowInput.TextInput, ((IWindowInput)windowInput).TextInput);
        Assert.Same(windowInput, Assert.Single(platform.Input.Windows));
        Assert.NotEmpty(displays);
        Assert.Contains(displays, display => display.IsPrimary);
        Assert.All(displays, display =>
        {
            Assert.StartsWith(@"\\.\DISPLAY", display.Name, StringComparison.OrdinalIgnoreCase);
            Assert.True(display.Bounds.Width > 0);
            Assert.True(display.Bounds.Height > 0);
            Assert.True(display.ScaleFactor > 0);
        });

        Point? movedPosition = null;
        window.Moved += (_, eventArgs) => movedPosition = eventArgs.Position;

        window.Position = new(120, 140);

        Assert.Equal(new Point(120, 140), window.Position);
        Assert.Equal(window.Position, movedPosition);

        window.State = WindowState.Maximized;

        Assert.Equal(WindowState.Maximized, window.State);

        window.Close();

        Assert.True(window.IsClosed);
        Assert.Empty(platform.Input.Windows);
        Assert.False(platform.PumpEvents());
    }
}
