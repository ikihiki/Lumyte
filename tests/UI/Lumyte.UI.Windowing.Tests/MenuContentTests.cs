using Lumyte.Input.Abstractions;
using Lumyte.UI.Controls;
using Lumyte.UI.Controls.Windowing;
using Xunit;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Controls.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Windowing.Tests;

/// <summary>Exercises real manager admission, independent content and passive routing.</summary>
public sealed class MenuContentTests
{
    /// <summary>Right opens one actual submenu, a command closes the whole chain even when it throws.</summary>
    [Fact]
    public void RealSubmenuCommandClosesChainAfterThrow()
    {
        using var fixture = new Fixture();
        var host = new MenuPopupHost(fixture.Popups, fixture.Owner);
        int factories = 0;
        int calls = 0;
        UiPopupHandle root = fixture.Popups.RequestOpen(fixture.Owner, new(fixture.Anchor, new(100, 100), () =>
            Menu(popupHost: host)[MenuItem(key: "sub", createSubmenu: () =>
            {
                factories++;
                return Menu(popupHost: host)[MenuItem(key: "run", onInvoke: () =>
                {
                    calls++;
                    throw new InvalidOperationException("command");
                })];
            })]));
        fixture.Pump();
        fixture.Send(new UiKeyInput(Key.ArrowRight, true));
        fixture.Send(new UiKeyInput(Key.ArrowRight, true, IsRepeat: true));
        fixture.Send(new UiKeyInput(Key.ArrowRight, false));
        fixture.Pump();
        Assert.Equal(1, factories);
        Assert.Equal(2, fixture.Popups.CapturePopups().Count);
        fixture.Send(new UiKeyInput(Key.Enter, true));
        Assert.Throws<InvalidOperationException>(() => fixture.Send(new UiKeyInput(Key.Enter, false)));
        fixture.Pump();
        Assert.Equal(1, calls);
        Assert.Equal(UiPopupState.Closed, root.State);
        Assert.Empty(fixture.Popups.CapturePopups());
    }

    /// <summary>Controlled roots queue ItemInvoked without silently changing declared open state.</summary>
    [Fact]
    public void ControlledRootReceivesItemInvokedAndKeepsOpen()
    {
        using var fixture = new Fixture();
        var host = new MenuPopupHost(fixture.Popups, fixture.Owner);
        int calls = 0;
        var reasons = new List<PopupDismissReason>();
        UiPopupHandle root = fixture.Popups.RequestOpen(fixture.Owner, new(fixture.Anchor, new(100, 80), () => Menu(popupHost: host)[MenuItem(key: "run", onInvoke: () => calls++)], onDismissRequested: request => reasons.Add(request.Reason)));
        fixture.Pump();
        fixture.Send(new UiKeyInput(Key.Enter, true));
        fixture.Send(new UiKeyInput(Key.Enter, false));
        Assert.Empty(reasons);
        fixture.Pump();
        Assert.Equal(1, calls);
        Assert.Equal(new[] { PopupDismissReason.ItemInvoked }, reasons);
        Assert.Equal(UiPopupState.Open, root.State);
    }

    /// <summary>Freshness rejection includes descendants produced by actual function preparation.</summary>
    [Fact]
    public void GeneratedContentCannotBorrowMountedDescendants()
    {
        using var fixture = new Fixture();
        var host = new MenuPopupHost(fixture.Popups, fixture.Owner);
        using IDisposable request = host.Open(fixture.Anchor, new(80, 50), () => new ContentFunction(() => fixture.Anchor.Widget), false, () => { });
        fixture.Pump();
        Assert.Empty(fixture.Popups.CapturePopups());
        Assert.Same(fixture.Surface, fixture.Anchor.Surface);
    }

    /// <summary>Invalid replacement content leaves an already-open sibling and its focus alive.</summary>
    [Fact]
    public void InvalidSiblingFactoryDoesNotCloseOldMenu()
    {
        using var fixture = new Fixture();
        UiPopupHandle old = fixture.Open();
        Assert.True(fixture.Popups.TryGetSurface(old.Id, out UiSurface? content));
        UiElement? focus = content!.FocusedElement;
        var host = new MenuPopupHost(fixture.Popups, fixture.Owner);
        int closed = 0;
        using IDisposable request = host.Open(fixture.Anchor, new(80, 50), () => fixture.Root, false, () => closed++);
        fixture.Pump();
        Assert.Equal(1, closed);
        Assert.Equal(UiPopupState.Open, old.State);
        Assert.Same(focus, content.FocusedElement);
        Assert.Single(fixture.Popups.CapturePopups());
    }

    /// <summary>A pending reservation can be cancelled before any resolver or factory callback.</summary>
    [Fact]
    public void CancelBeforeDrainRunsNoUserFactory()
    {
        using var fixture = new Fixture();
        int resolved = 0;
        int built = 0;
        var host = new MenuPopupHost(fixture.Popups, () =>
        {
            resolved++;
            return fixture.Owner;
        });
        Widget CreateContent()
        {
            built++;
            return Menu();
        }

        IDisposable request = host.Open(fixture.Anchor, new(80, 50), CreateContent, false, () => { });
        request.Dispose();
        fixture.Pump();
        Assert.Equal(0, resolved);
        Assert.Equal(0, built);
        Assert.Empty(fixture.Popups.CapturePopups());
    }

    /// <summary>Factory reentry can cancel the exact reservation without adopting a late surface.</summary>
    [Fact]
    public void FactoryCancellationRollsBackPreparedSurface()
    {
        using var fixture = new Fixture();
        var host = new MenuPopupHost(fixture.Popups, fixture.Owner);
        IDisposable? request = null;
        Widget? content = null;
        int closed = 0;
        Widget CreateContent()
        {
            request!.Dispose();
            return content = Menu()[MenuItem(key: "a")];
        }

        request = host.Open(fixture.Anchor, new(80, 50), CreateContent, false, () => closed++);
        fixture.Pump();
        Assert.Equal(1, closed);
        Assert.Empty(fixture.Popups.CapturePopups());
        using var other = new UiSurface(content!);
    }

    /// <summary>Tooltip uses Passive and cannot steal the ordinary target or menu chain input.</summary>
    [Fact]
    public void TooltipActualPassivePopupPreservesFocusAndPointerRouting()
    {
        using var fixture = new Fixture();
        var host = new MenuPopupHost(fixture.Popups, fixture.Owner);
        fixture.Surface.Rebuild(Tooltip(popupHost: host, isOpen: true, createPopup: () => Button(width: 100, height: 100))[
            Button(width: 80, height: 40, onClick: () => fixture.Clicks++)]);
        fixture.Surface.UpdateLayout();
        UiElement anchor = fixture.Surface.RootElement.Children[0];
        Assert.True(fixture.Surface.TryFocus(anchor));
        fixture.Pump();
        Assert.Equal(UiPopupInputMode.Passive, Assert.Single(fixture.Popups.CapturePopups()).InputMode);
        Assert.Same(anchor, fixture.Surface.FocusedElement);
        fixture.Send(new UiPointerInput(1, UiPointerPhase.Down, new(5)));
        fixture.Send(new UiPointerInput(1, UiPointerPhase.Up, new(5)));
        Assert.Equal(1, fixture.Clicks);
        Assert.Same(anchor, fixture.Surface.FocusedElement);
        fixture.Surface.Rebuild(SizedBox(width: 20, height: 20));
        fixture.Surface.UpdateLayout();
        fixture.Pump();
        Assert.Empty(fixture.Popups.CapturePopups());
    }

    /// <summary>A retained inactive anchor closes its own passive popup without consulting another surface index.</summary>
    [Fact]
    public void InactiveRetainedAnchorClosesTooltip()
    {
        using var fixture = new Fixture();
        var host = new MenuPopupHost(fixture.Popups, fixture.Owner);
        fixture.Surface.Rebuild(Expander(isExpanded: true)[
            Expander.Header()[Rectangle(width: 100, height: 20)],
            Expander.Content()[Tooltip(popupHost: host, isOpen: true, createPopup: () => Rectangle(width: 80, height: 20))[Button(width: 100, height: 20)]]]);
        fixture.Surface.UpdateLayout();
        fixture.Pump();
        Assert.Single(fixture.Popups.CapturePopups());
        fixture.Send(new UiPointerInput(3, UiPointerPhase.Down, new(5)));
        fixture.Send(new UiPointerInput(3, UiPointerPhase.Up, new(5)));
        fixture.Surface.UpdateLayout();
        fixture.Pump();
        Assert.Empty(fixture.Popups.CapturePopups());
    }

    /// <summary>Cancellation after preparation must not replace a still-live old menu.</summary>
    [Fact]
    public void CancelledPreparedReplacementKeepsOldMenu()
    {
        using var fixture = new Fixture();
        UiPopupHandle old = fixture.Open();
        var host = new MenuPopupHost(fixture.Popups, fixture.Owner);
        IDisposable request = host.Open(fixture.Anchor, new(80, 50), () => Menu()[MenuItem(key: "a")], false, () => { });
        fixture.Popups.Advance();
        request.Dispose();
        fixture.Pump();
        Assert.Equal(UiPopupState.Open, old.State);
        Assert.Single(fixture.Popups.CapturePopups());
    }

    /// <summary>Factory reentry is checked again before replacing unrelated live siblings.</summary>
    [Fact]
    public void FactoryDisablingItsOwnAnchorKeepsUnrelatedOldMenu()
    {
        using var fixture = new Fixture();
        fixture.Surface.Rebuild(Panel()[Button(width: 20, height: 20), Button(width: 20, height: 20)]);
        fixture.Surface.UpdateLayout();
        UiElement anchor = fixture.Surface.RootElement.Children[1];
        UiPopupHandle old = fixture.Open();
        var host = new MenuPopupHost(fixture.Popups, fixture.Owner);
        Widget CreateContent()
        {
            fixture.Surface.Rebuild(Panel()[Button(width: 20, height: 20), Button(width: 20, height: 20, isEnabled: false)]);
            fixture.Surface.UpdateLayout();
            return Menu()[MenuItem(key: "a")];
        }

        using IDisposable request = host.Open(anchor, new(80, 50), CreateContent, false, () => { });
        fixture.Pump();
        Assert.Equal(UiPopupState.Open, old.State);
        Assert.Single(fixture.Popups.CapturePopups());
    }

    /// <summary>Command cleanup cannot overwrite a newer explicit application focus request.</summary>
    [Fact]
    public void CommandFocusIntentWinsOverOldOwnerBookmark()
    {
        using var fixture = new Fixture();
        fixture.Surface.Rebuild(Panel()[Button(width: 20, height: 20), Button(width: 20, height: 20)]);
        fixture.Surface.UpdateLayout();
        UiElement destination = fixture.Surface.RootElement.Children[1];
        Assert.True(fixture.Surface.TryFocus(fixture.Anchor));
        var host = new MenuPopupHost(fixture.Popups, fixture.Owner);
        fixture.Popups.RequestOpen(fixture.Owner, new(fixture.Anchor, new(80, 50), () => Menu(popupHost: host)[MenuItem(key: "run", onInvoke: () => Assert.True(fixture.Surface.TryFocus(destination)))]));
        fixture.Pump();
        fixture.Send(new UiKeyInput(Key.Enter, true));
        fixture.Send(new UiKeyInput(Key.Enter, false));
        fixture.Pump();
        Assert.Empty(fixture.Popups.CapturePopups());
        Assert.Same(destination, fixture.Surface.FocusedElement);
    }

    private sealed class ContentFunction(Func<Widget> create) : UiFunctionWidget
    {
        protected override Widget Build() => create();
    }
}
