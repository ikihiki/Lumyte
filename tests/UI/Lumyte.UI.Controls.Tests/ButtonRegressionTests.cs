using Lumyte.Input.Abstractions;
using Xunit;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Controls.Tests;

/// <summary>Proves shared recognition retains the original Button callback and cancellation contracts.</summary>
public sealed class ButtonRegressionTests
{
    /// <summary>Button obtains the latest callback at Up after pointer owner release.</summary>
    [Fact]
    public void LatestCallbackAndReentrantExceptionDoNotCancelNewPress()
    {
        int oldCalls = 0;
        int newCalls = 0;
        Lumyte.UI.Compose.Definitions.Button button = Button(onClick: () => oldCalls++);
        using var surface = new UiSurface(button);
        surface.SetViewport(new(100));
        surface.UpdateLayout();
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(2)));
        button.OnClick = () =>
        {
            newCalls++;
            Assert.False(Assert.IsType<ButtonElement>(surface.RootElement).IsPressed);
            surface.Dispatch(new UiKeyInput(Key.Space, true));
            throw new InvalidOperationException("up callback");
        };
        Assert.Throws<InvalidOperationException>(() => surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(2))));
        Assert.Equal(0, oldCalls);
        Assert.Equal(1, newCalls);
        Assert.True(Assert.IsType<ButtonElement>(surface.RootElement).IsPressed);
        button.OnClick = () => newCalls++;
        surface.Dispatch(new UiKeyInput(Key.Space, false));
        Assert.Equal(2, newCalls);
        Assert.False(Assert.IsType<ButtonElement>(surface.RootElement).IsPressed);
    }
}
