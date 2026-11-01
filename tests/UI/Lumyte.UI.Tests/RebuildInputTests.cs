using Xunit;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Tests;

/// <summary>Checks input and logical identity integrate with the foundational tree reconciliation.</summary>
public sealed class RebuildInputTests : UiElementTestBase
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
        Assert.True(Pressed(first));
        Definitions.Button next = Button(key: "button", width: 20, height: 20, onClick: () => newClicks++);
        surface.Rebuild(Panel()[next]);
        surface.UpdateLayout();
        Assert.True(Pressed(next));
        Assert.True(surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(1, 1))));
        Assert.False(Pressed(next));
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
        Assert.False(Pressed(original));
        Assert.Throws<ObjectDisposedException>(() => surface.Root);
    }

    /// <summary>Checks a local callback update preserves capture and runtime content slots.</summary>
    [Fact]
    public void LocalUpdateKeepsCaptureAndUsesTheLatestCallback()
    {
        int clicks = 0;
        Definitions.Button button = Button(width: 20, height: 20)[Panel()];
        using UiSurface surface = Surface(button);
        UiElement element = surface.RootElement;
        UiElement content = element.Children[0];
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(1, 1)));
        surface.RebuildSubtree(element, Button(width: 20, height: 20, onClick: () => clicks++));
        Assert.Same(element, surface.RootElement);
        Assert.Same(content, element.Children[0]);
        Assert.True(((ButtonElement)element).IsPressed);
        Assert.True(surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(1, 1))));
        Assert.Equal(1, clicks);
    }

    /// <summary>Checks disabling a retained ancestor cancels a child's ownership at publication.</summary>
    [Fact]
    public void DisablingAncestorCancelsRetainedChildCapture()
    {
        Definitions.Button button = Button(width: 20, height: 20);
        using UiSurface surface = Surface(Panel()[button]);
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(1, 1)));
        Assert.True(Pressed(button));
        surface.RebuildSubtree(surface.RootElement, Panel(isEnabled: false));
        Assert.False(Pressed(button));
        surface.UpdateLayout();
        Assert.False(surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(1, 1))));
    }

    /// <summary>Checks a custom component can request cancellation without replacing its persistent instance.</summary>
    [Fact]
    public void CustomPreparationCanCancelItsInputOwnership()
    {
        var button = new CancelButton { Width = 20, Height = 20 };
        using UiSurface surface = Surface(button);
        ButtonElement element = Assert.IsAssignableFrom<ButtonElement>(surface.RootElement);
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(1, 1)));
        Assert.True(element.IsPressed);
        surface.Rebuild(new CancelButton { Width = 20, Height = 20, Cancel = true });
        surface.UpdateTree();
        Assert.Same(element, surface.RootElement);
        Assert.False(element.IsPressed);
        Assert.False(surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(1, 1))));
    }

    /// <summary>Checks a held key and focus continue on the matched button with its latest callback.</summary>
    [Fact]
    public void KeyboardPressRetainsFocusAndUsesTheNewCallback()
    {
        int clicks = 0;
        Definitions.Button first = Button(key: "button");
        using UiSurface surface = Surface(Panel()[first]);
        UiElement element = Instance(first);
        Assert.True(surface.TryFocus(element));
        Assert.True(surface.Dispatch(new UiKeyInput(Lumyte.Input.Abstractions.Key.Space, true)));
        Definitions.Button next = Button(key: "button", onClick: () => clicks++);
        surface.Rebuild(Panel()[next]);
        surface.UpdateLayout();
        Assert.Same(element, surface.FocusedElement);
        Assert.Same(next, element.Widget);
        Assert.True(Pressed(next));
        Assert.True(surface.Dispatch(new UiKeyInput(Lumyte.Input.Abstractions.Key.Space, false)));
        Assert.False(Pressed(next));
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
        Assert.False(Pressed(first));
        Assert.Null(surface.FocusedElement?.Widget);
        Assert.False(surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(1, 1))));
    }

    /// <summary>Checks an explicit input reset ends a held key but consumes its physical terminal event.</summary>
    [Fact]
    public void CustomCancellationEndsFocusAndKeyOwnershipWithoutReplacingTheElement()
    {
        var button = new CancelButton { Width = 20, Height = 20 };
        using UiSurface surface = Surface(button);
        ButtonElement element = Assert.IsAssignableFrom<ButtonElement>(surface.RootElement);
        Assert.True(surface.TryFocus(element));
        Assert.True(surface.Dispatch(new UiKeyInput(Lumyte.Input.Abstractions.Key.Space, true)));
        Assert.True(element.IsPressed);
        surface.Rebuild(new CancelButton { Width = 20, Height = 20, Cancel = true });
        surface.UpdateTree();
        Assert.Same(element, surface.RootElement);
        Assert.False(element.IsPressed);
        Assert.Null(surface.FocusedElement);
        Assert.True(surface.Dispatch(new UiKeyInput(Lumyte.Input.Abstractions.Key.Space, false)));
        Assert.False(surface.Dispatch(new UiKeyInput(Lumyte.Input.Abstractions.Key.Space, false)));
    }

    private UiSurface Surface(Definitions.Widget root)
    {
        UiSurface surface = Mount(root);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        return surface;
    }

    private sealed class ClosingButton : Definitions.Button
    {
        internal Action? Closed { get; set; }

        protected override UiElement CreateElement() => new ClosingElement(this);
    }

    private sealed class ClosingElement(ClosingButton widget) : ButtonElement(widget)
    {
        protected override void OnPointerCaptureChanged(UiPointerCaptureState state)
        {
            base.OnPointerCaptureChanged(state);
            if (!state.IsCaptured)
            {
                ((ClosingButton)Widget).Closed?.Invoke();
            }
        }
    }

    private sealed class CancelButton : Definitions.Button
    {
        internal bool Cancel { get; init; }

        protected override UiElement CreateElement() => new CancelElement(this);
    }

    private sealed class CancelElement(CancelButton widget) : ButtonElement(widget)
    {
        protected override Action? PrepareUpdate(Definitions.Button next, UiElementUpdateContext context)
        {
            if (((CancelButton)next).Cancel)
            {
                context.CancelInput();
            }

            return null;
        }
    }
}
