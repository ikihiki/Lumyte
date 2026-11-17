using System.Numerics;
using Lumyte.Input.Abstractions;
using Xunit;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Controls.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Controls.Tests;

/// <summary>Exercises typed menu state through ordinary surface input and explicit tooltip state.</summary>
public sealed class MenuTooltipTests
{
    /// <summary>Only actual direct item/separator types with unique inherited keys are admitted.</summary>
    [Fact]
    public void TopologyValidationPrecedesPublication()
    {
        Assert.Throws<InvalidOperationException>(() => new UiSurface(MenuItem(key: "a")));
        Assert.Throws<InvalidOperationException>(() => new UiSurface(Menu()[Button(role: UiRole.MenuItem)]));
        Assert.Throws<InvalidOperationException>(() => new UiSurface(Menu()[MenuItem()]));
        Assert.Throws<InvalidOperationException>(() => new UiSurface(Menu()[Separator(focusable: true)]));
        Assert.Throws<ArgumentException>(() => new UiSurface(Menu()[MenuItem(key: "a"), MenuItem(key: "a")]));
        using UiSurface surface = Surface(Menu()[MenuItem(key: "a")]);
        UiElement original = surface.RootElement.Children[0];
        surface.Rebuild(Menu()[Button(role: UiRole.MenuItem)]);
        Assert.Throws<InvalidOperationException>(surface.UpdateTree);
        Assert.Same(original, surface.RootElement.Children[0]);
    }

    /// <summary>Command and submenu declarations cannot be combined, including checked submenus.</summary>
    [Fact]
    public void ConflictingItemBehaviorsAreRejected()
    {
        var host = new Host();
        Assert.Throws<InvalidOperationException>(() => new UiSurface(Menu(popupHost: host)[MenuItem(key: "a", onInvoke: () => { }, createSubmenu: () => Menu())]));
        Assert.Throws<InvalidOperationException>(() => new UiSurface(Menu(popupHost: host)[MenuItem(key: "a", isCheckable: true, createSubmenu: () => Menu())]));
        Assert.Throws<InvalidOperationException>(() => new UiSurface(Menu()[MenuItem(key: "a", createSubmenu: () => Menu())]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UiSurface(Menu()[MenuItem(key: "a", popupSize: new Vector2(float.NaN, 1))]));
    }

    /// <summary>Roving navigation skips disabled and hidden rows, with repeat/Up consumed once.</summary>
    [Fact]
    public void RovingFocusOwnsNavigationSeries()
    {
        using UiSurface surface = Surface(Menu()[
            MenuItem(key: "a"),
            Separator(),
            MenuItem(key: "disabled", isEnabled: false),
            MenuItem(key: "hidden", isVisible: false),
            MenuItem(key: "b"),
            MenuItem(key: "c")]);
        Assert.True(surface.MoveFocus(UiFocusNavigation.First));
        Assert.Equal("a", surface.FocusedElement!.Widget.Key);
        Assert.True(surface.Dispatch(new UiKeyInput(Key.ArrowDown, true)));
        Assert.Equal("b", surface.FocusedElement!.Widget.Key);
        Assert.True(surface.Dispatch(new UiKeyInput(Key.ArrowDown, true, IsRepeat: true)));
        Assert.Equal("b", surface.FocusedElement!.Widget.Key);
        Assert.True(surface.Dispatch(new UiKeyInput(Key.ArrowDown, false)));
        Press(surface, Key.End);
        Assert.Equal("c", surface.FocusedElement!.Widget.Key);
        Press(surface, Key.Home);
        Assert.Equal("a", surface.FocusedElement!.Widget.Key);
        Press(surface, Key.ArrowUp);
        Assert.Equal("c", surface.FocusedElement!.Widget.Key);
    }

    /// <summary>An inline menu without focus enters from the end on ArrowUp.</summary>
    [Fact]
    public void InlineMenuCanEnterFromEitherDirection()
    {
        using UiSurface surface = Surface(Menu()[MenuItem(key: "a"), MenuItem(key: "b"), MenuItem(key: "c")]);
        Press(surface, Key.ArrowUp);
        Assert.Equal("c", surface.FocusedElement!.Widget.Key);
    }

    /// <summary>One passive declaration has independent accepted check state on each surface.</summary>
    [Fact]
    public void AcceptedCheckStateIsLocalAndLatestCallbackRunsOnce()
    {
        int calls = 0;
        Compose.Definitions.MenuItem item = MenuItem(key: "a", isCheckable: true);
        Compose.Definitions.Menu declaration = Menu()[item];
        using UiSurface first = Surface(declaration);
        using UiSurface second = Surface(declaration);
        MenuItemElement accepted = Assert.IsType<MenuItemElement>(first.RootElement.Children[0]);
        item.OnChanged = value =>
        {
            Assert.Equal(value, accepted.IsChecked);
            calls++;
        };
        Assert.True(first.TryFocus(accepted));
        first.Dispatch(new UiKeyInput(Key.Space, true));
        first.Dispatch(new UiKeyInput(Key.Space, true, IsRepeat: true));
        first.Dispatch(new UiKeyInput(Key.Space, false));
        Assert.True(accepted.IsChecked);
        Assert.False(Assert.IsType<MenuItemElement>(second.RootElement.Children[0]).IsChecked);
        Assert.False(item.IsChecked);
        Assert.Equal(1, calls);
        first.Dispatch(new UiKeyInput(Key.Space, false));
        Assert.Equal(1, calls);
        UiAutomationNode node = first.CaptureAutomationSnapshot().Nodes.Single(node => node.Role == UiRole.MenuItem);
        Assert.True(node.IsChecked);
        Assert.True(node.Actions.HasFlag(UiAutomationActions.Check));
        Assert.Null(node.IsExpanded);
    }

    /// <summary>Check requests use actual capabilities rather than the declaration's semantic role.</summary>
    [Fact]
    public void CheckInputAndCapabilityAreIdempotent()
    {
        int changes = 0;
        using UiSurface surface = Surface(Menu()[MenuItem(key: "a", isCheckable: true, role: UiRole.MenuItem, onChanged: _ => changes++)]);
        UiElement item = surface.RootElement.Children[0];
        Assert.True(surface.Dispatch(new UiCheckInput(item.AutomationId, true, new(2))));
        Assert.True(surface.Dispatch(new UiCheckInput(item.AutomationId, true, new(2))));
        Assert.Equal(1, changes);
        Assert.True(Assert.IsType<MenuItemElement>(item).IsChecked);
        using UiSurface ordinary = Surface(Menu()[MenuItem(key: "a")]);
        Assert.False(ordinary.CaptureAutomationSnapshot().Nodes.Last().Actions.HasFlag(UiAutomationActions.Check));
    }

    /// <summary>Cancelled presses and fresh configuration adoption do not invoke the old callback.</summary>
    [Fact]
    public void CancellationAndAdoptionAreSilent()
    {
        int calls = 0;
        using UiSurface surface = Surface(Menu()[MenuItem(key: "a", onInvoke: () => calls++)]);
        surface.TryFocus(surface.RootElement.Children[0]);
        surface.Dispatch(new UiKeyInput(Key.Enter, true));
        surface.Dispatch(new UiFocusLostInput());
        surface.Dispatch(new UiKeyInput(Key.Enter, false));
        Assert.Equal(0, calls);
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(2)));
        surface.Rebuild(Menu()[MenuItem(key: "a", isCheckable: true, isChecked: true, onChanged: _ => calls++)]);
        surface.UpdateLayout();
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(2)));
        Assert.Equal(0, calls);
        Assert.True(Assert.IsType<MenuItemElement>(surface.RootElement.Children[0]).IsChecked);
    }

    /// <summary>Cleanup is queued before callback failure and the terminal Up cannot invoke twice.</summary>
    [Fact]
    public void ThrowingCommandStillQueuesDismissal()
    {
        var host = new Host();
        int calls = 0;
        using UiSurface surface = Surface(Menu(popupHost: host)[MenuItem(key: "a", onInvoke: () =>
        {
            calls++;
            Assert.Equal(1, host.Invocations);
            throw new InvalidOperationException("command");
        })]);
        Assert.Throws<InvalidOperationException>(() => Click(surface));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(2)));
        Assert.Equal(1, calls);
        Assert.Equal(1, host.Invocations);
    }

    /// <summary>Desired submenu expansion focuses its own semantic anchor and stays idempotent.</summary>
    [Fact]
    public void DesiredSubmenuExpansionUsesTheActualAnchor()
    {
        var host = new Host();
        using UiSurface surface = Surface(Menu(popupHost: host)[MenuItem(key: "a"), MenuItem(key: "sub", createSubmenu: () => Menu())]);
        UiElement item = surface.RootElement.Children[1];
        Assert.True(surface.Dispatch(new UiExpandInput(item.AutomationId, true, new(2, 26))));
        Assert.Same(item, surface.FocusedElement);
        Assert.True(Assert.IsType<MenuItemElement>(item).IsExpanded);
        Assert.True(surface.Dispatch(new UiExpandInput(item.AutomationId, true, new(2, 26))));
        Assert.Equal(1, host.Opens);
        Assert.True(surface.Dispatch(new UiExpandInput(item.AutomationId, false, new(2, 26))));
        Assert.Equal(1, host.Disposals);
    }

    /// <summary>Reentry cannot transfer a cancelled generation's late token into a replacement item.</summary>
    [Fact]
    public void SubmenuReservationSurvivesReentryAndLateClosedCallbacks()
    {
        var host = new Host();
        using UiSurface surface = Surface(Menu(popupHost: host)[MenuItem(key: "a", createSubmenu: () => Menu())]);
        MenuItemElement item = Assert.IsType<MenuItemElement>(surface.RootElement.Children[0]);
        host.DuringOpen = () =>
        {
            surface.Rebuild(Menu(popupHost: host)[MenuItem(key: "a", createSubmenu: () => Menu())]);
            surface.UpdateTree();
        };
        Click(surface);
        Assert.False(item.IsExpanded);
        Assert.Equal(1, host.Disposals);
        host.DuringOpen = null;
        surface.UpdateLayout();
        Click(surface);
        Assert.True(item.IsExpanded);
        host.Closed[0]();
        Assert.True(item.IsExpanded);
        surface.Dispose();
        Assert.Equal(2, host.Disposals);
    }

    /// <summary>Tooltip content is passive, one-per-instance, explicit and cleaned when the anchor is removed.</summary>
    [Fact]
    public void TooltipDelegatesAnchorInputAndCleansOnlyItsToken()
    {
        var host = new Host();
        int clicks = 0;
        Compose.Definitions.Tooltip declaration = Tooltip(popupHost: host, isOpen: true, createPopup: () => Rectangle(width: 20, height: 10))[
            Button(width: 24, height: 24, onClick: () => clicks++)];
        using UiSurface first = Surface(declaration);
        using UiSurface second = Surface(declaration);
        Assert.Equal(2, host.Opens);
        Assert.True(host.Passive);
        Click(first);
        Assert.Equal(1, clicks);
        Assert.Same(first.RootElement.Children[0], first.FocusedElement);
        Assert.Equal(UiAutomationActions.None, first.CaptureAutomationSnapshot().Nodes[0].Actions);
        first.Dispose();
        Assert.Equal(1, host.Disposals);
        Assert.True(Assert.IsType<TooltipElement>(second.RootElement).IsOpen);
        Assert.Throws<ArgumentException>(() => Tooltip()[Button(), Button()]);
        Assert.Throws<InvalidOperationException>(() => new UiSurface(Tooltip(isOpen: true)));
    }

    /// <summary>Configuration changes reconcile an explicitly controlled tooltip without mutating its model.</summary>
    [Fact]
    public void TooltipOpensAndClosesThroughAdoption()
    {
        var host = new Host();
        using UiSurface surface = Surface(Tooltip(popupHost: host, width: 20, height: 20));
        Assert.Equal(0, host.Opens);
        surface.Rebuild(Tooltip(popupHost: host, isOpen: true, createPopup: () => Rectangle(), width: 20, height: 20));
        surface.UpdateLayout();
        Assert.True(Assert.IsType<TooltipElement>(surface.RootElement).IsOpen);
        surface.Rebuild(Tooltip(popupHost: host, isOpen: false, width: 20, height: 20));
        surface.UpdateLayout();
        Assert.False(Assert.IsType<TooltipElement>(surface.RootElement).IsOpen);
        Assert.Equal(1, host.Disposals);
    }

    private static UiSurface Surface(Widget root)
    {
        var surface = new UiSurface(root);
        surface.SetViewport(new(240, 240));
        surface.UpdateLayout();
        return surface;
    }

    private static void Press(UiSurface surface, Key key)
    {
        surface.Dispatch(new UiKeyInput(key, true));
        surface.Dispatch(new UiKeyInput(key, false));
    }

    private static void Click(UiSurface surface)
    {
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(2)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(2)));
    }

    private sealed class Host : IMenuPopupHost
    {
        internal int Opens { get; private set; }

        internal int Disposals { get; private set; }

        internal int Invocations { get; private set; }

        internal bool Passive { get; private set; }

        internal Action? DuringOpen { get; set; }

        internal List<Action> Closed { get; } = [];

        public IDisposable Open(UiElement anchor, Vector2 size, Func<Widget> createContent, bool passive, Action closed)
        {
            Opens++;
            Passive = passive;
            Closed.Add(closed);
            DuringOpen?.Invoke();
            return new Token(() => Disposals++);
        }

        public void ItemInvoked(UiElement anchor) => Invocations++;
    }

    private sealed class Token(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
