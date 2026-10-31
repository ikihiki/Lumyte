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

    /// <summary>Checks a held key and focus continue on the matched button with its latest callback.</summary>
    [Fact]
    public void KeyboardPressRetainsFocusAndUsesTheNewCallback()
    {
        int clicks = 0;
        Definitions.Button first = Button(key: "button");
        using UiSurface surface = Surface(Panel()[first]);
        Assert.True(surface.TryFocus(first));
        Assert.True(surface.Dispatch(new UiKeyInput(Lumyte.Input.Abstractions.Key.Space, true)));
        Definitions.Button next = Button(key: "button", onClick: () => clicks++);
        surface.Rebuild(Panel()[next]);
        surface.UpdateLayout();
        Assert.Same(next, surface.FocusedWidget);
        Assert.True(next.IsPressed);
        Assert.True(surface.Dispatch(new UiKeyInput(Lumyte.Input.Abstractions.Key.Space, false)));
        Assert.False(next.IsPressed);
        Assert.Equal(1, clicks);
    }

    /// <summary>Checks changed keys cancel the removed button's capture and drop its focus.</summary>
    [Fact]
    public void ChangedKeyDropsOldFocusAndCapture()
    {
        Definitions.Button first = Button(key: "old", width: 20, height: 20);
        using UiSurface surface = Surface(Panel()[first]);
        Assert.True(surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(1, 1))));
        Definitions.Button next = Button(key: "new", width: 20, height: 20);
        surface.Rebuild(Panel()[next]);
        surface.UpdateLayout();
        Assert.False(first.IsPressed);
        Assert.Null(surface.FocusedWidget);
        Assert.False(surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(1, 1))));
    }

    /// <summary>Checks logical automation identity survives keyed reordering while old declarations detach.</summary>
    [Fact]
    public void AutomationIdentityFollowsTheMatchedDeclaration()
    {
        Definitions.Button first = Button(key: "button", testId: "button");
        using UiSurface surface = Surface(Panel()[first]);
        UiAutomationId identity = surface.CaptureAutomationSnapshot().Nodes.Single(node => node.TestId == "button").Id;
        Definitions.Button next = Button(key: "button", testId: "button");
        surface.Rebuild(Panel()[Panel(), next]);
        surface.UpdateLayout();
        Assert.Equal(identity, surface.CaptureAutomationSnapshot().Nodes.Single(node => node.TestId == "button").Id);
    }

    /// <summary>Checks a saved focus bookmark resolves the logical identity after declarations are replaced.</summary>
    [Fact]
    public void FocusBookmarkFollowsTheMatchedDeclaration()
    {
        Definitions.Button first = Button(key: "button");
        using UiSurface surface = Surface(Panel()[first]);
        Assert.True(surface.TryFocus(first));
        UiFocusBookmark bookmark = surface.CaptureFocus();
        Definitions.Button next = Button(key: "button");
        surface.Rebuild(Panel()[Panel(), next]);
        surface.UpdateLayout();
        Assert.True(surface.TryRestoreFocus(bookmark));
        Assert.Same(next, surface.FocusedWidget);
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
