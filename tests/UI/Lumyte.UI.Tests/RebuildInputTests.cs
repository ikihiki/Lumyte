using Xunit;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Tests;

/// <summary>Checks input and logical identity integrate with the foundational tree reconciliation.</summary>
public sealed class RebuildInputTests
{
    /// <summary>Checks a continuing pointer press retains state and invokes only the latest callback.</summary>
    [Fact]
    public void PointerPressContinuesWithTheNewDeclaration()
    {
        int oldClicks = 0;
        int newClicks = 0;
        Definitions.Button first = Button(key: "button", width: 20, height: 20, onClick: () => oldClicks++);
        using UiSurface surface = Surface(Panel()[first]);
        Assert.True(surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(1, 1))));
        Assert.True(first.IsPressed);
        Definitions.Button next = Button(key: "button", width: 20, height: 20, onClick: () => newClicks++);
        surface.Rebuild(Panel()[next]);
        surface.UpdateLayout();
        Assert.True(next.IsPressed);
        Assert.True(surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(1, 1))));
        Assert.False(next.IsPressed);
        Assert.Equal(0, oldClicks);
        Assert.Equal(1, newClicks);
    }

    /// <summary>Checks removal cleanup can dispose the surface without reconnecting the new tree.</summary>
    [Fact]
    public void DisposalDuringCaptureCleanupStopsLayout()
    {
        var original = new ClosingButton { Width = 20, Height = 20 };
        using UiSurface surface = Surface(original);
        Assert.True(surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(1, 1))));
        original.Closed = surface.Dispose;
        surface.Rebuild(Panel());
        surface.UpdateLayout();
        Assert.False(original.IsPressed);
        Assert.Throws<ObjectDisposedException>(() => surface.Root);
    }

    private static UiSurface Surface(Definitions.Widget root)
    {
        var surface = new UiSurface(root);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        return surface;
    }

    private sealed class ClosingButton : Definitions.Button
    {
        internal Action? Closed { get; set; }

        protected override void OnPointerCaptureChanged(UiPointerCaptureState state)
        {
            base.OnPointerCaptureChanged(state);
            if (!state.IsCaptured)
            {
                Closed?.Invoke();
            }
        }
    }
}
