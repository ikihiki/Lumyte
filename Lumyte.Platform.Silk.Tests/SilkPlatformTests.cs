using System.Drawing;

using Lumyte.Platform.SilkNet;

using Xunit;

namespace Lumyte.Platform.SilkNet.Tests;

public sealed class SilkPlatformTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public void HiddenWindowExposesPortableStateAndInput()
    {
        using var platform = new SilkPlatform();
        using SilkWindow window = platform.CreateWindow(new()
        {
            ClientSize = new Size(320, 200),
            IsVisible = false,
            Title = "Lumyte Silk test",
        });

        bool running = platform.PumpEvents();

        Assert.True(running);
        Assert.Equal("Lumyte Silk test", window.Title);
        Assert.Equal(new Size(320, 200), window.ClientSize);
        Assert.Same(window.WindowInput, platform.Input.GetWindow(window));
        Assert.NotEqual(nint.Zero, window.Native.Handle);
        Assert.NotEmpty(platform.Displays);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void ClosingWindowDisposesAfterEventPump()
    {
        using var platform = new SilkPlatform();
        using SilkWindow window = platform.CreateWindow(new()
        {
            ClientSize = new Size(320, 200),
            IsVisible = false,
            Title = "Lumyte Silk close test",
        });
        bool callbackObservedOpenWindow = false;
        window.CloseRequested += (_, _) =>
            callbackObservedOpenWindow = !window.IsClosed;

        window.Close();
        bool running = platform.PumpEvents();

        Assert.True(callbackObservedOpenWindow);
        Assert.True(window.IsCloseRequested);
        Assert.True(window.IsClosed);
        Assert.False(running);
    }
}
