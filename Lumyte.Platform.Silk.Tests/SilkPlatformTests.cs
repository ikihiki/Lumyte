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
}
