using Xunit;

namespace Lumyte.Platform.Windows.Tests;

public sealed class WindowsPlatformInputTests
{
    [Fact]
    public void WindowsAreRegisteredAndRemovedExactlyOnce()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var platform = new WindowsPlatform();
        List<WindowInputChangedEventArgs> changes = [];
        platform.Input.WindowChanged += (_, eventArgs) => changes.Add(eventArgs);
        using WindowsWindow first = platform.CreateWindow(new() { IsVisible = false });
        using WindowsWindow second = platform.CreateWindow(new() { IsVisible = false });

        first.Close();
        second.Close();

        Assert.Collection(
            changes,
            change => AssertWindowChange(change, first, true),
            change => AssertWindowChange(change, second, true),
            change => AssertWindowChange(change, first, false),
            change => AssertWindowChange(change, second, false));
        Assert.Empty(platform.Input.Windows);
    }

    private static void AssertWindowChange(
        WindowInputChangedEventArgs change,
        WindowsWindow window,
        bool isAdded)
    {
        Assert.Same(window, change.Input.Window);
        Assert.Equal(isAdded, change.IsAdded);
    }
}
