using System.Numerics;
using Lumyte.Input.Abstractions;
using Lumyte.Signals;
using Xunit;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Controls.Compose;

namespace Lumyte.UI.Controls.Tests;

/// <summary>Exercises retained panel controls through their generated public API.</summary>
public sealed class TabExpanderTests
{
    /// <summary>Content remains mounted but cannot be focused, hit or laid out while collapsed.</summary>
    [Fact]
    public void ExpanderRetainsContentAndIndependentAcceptedState()
    {
        Compose.Definitions.Expander declaration = Expander()[Expander.Header()[Rectangle(width: 60, height: 20)], Expander.Content()[Button(width: 60, height: 40)]];
        using UiSurface first = Surface(declaration);
        using UiSurface second = Surface(declaration);
        UiElement content = first.RootElement.Children[1];
        Assert.Equal(Vector2.Zero, content.Size);
        Assert.False(first.TryFocus(content));
        Click(first, new(2));
        first.UpdateLayout();
        Assert.True(Assert.IsType<ExpanderElement>(first.RootElement).IsExpanded);
        Assert.False(Assert.IsType<ExpanderElement>(second.RootElement).IsExpanded);
        Assert.False(declaration.IsExpanded);
        Assert.Same(content, first.RootElement.Children[1]);
        Assert.True(first.TryFocus(content));
        Click(first, new(2));
        first.UpdateLayout();
        Assert.Same(content, first.RootElement.Children[1]);
        Assert.Equal(Vector2.Zero, content.Size);
        Assert.False(first.TryFocus(content));
        Assert.False(first.CaptureAutomationSnapshot().Nodes.Single(node => node.Id == content.AutomationId).IsVisible);
    }

    /// <summary>Selection navigates eligible headers and preserves inactive element identity.</summary>
    [Fact]
    public void TabsNavigateAndRetainPanels()
    {
        var changes = new List<string?>();
        using UiSurface surface = Surface(TabView(selectedKey: "a", onSelectionChanged: changes.Add)[
            TabItem(key: "a")[TabItem.Header()[Rectangle(width: 60, height: 20)], TabItem.Content()[Button(width: 60, height: 30)]],
            TabItem(key: "b")[TabItem.Header()[Rectangle(width: 60, height: 20)], TabItem.Content()[Button(width: 60, height: 30)]]]);
        TabViewElement group = Assert.IsType<TabViewElement>(surface.RootElement);
        UiElement panel = group.Children[0].Children[1];
        Assert.True(surface.TryFocus(group.Children[0]));
        surface.Dispatch(new UiKeyInput(Key.ArrowRight, true));
        surface.Dispatch(new UiKeyInput(Key.ArrowRight, false));
        surface.UpdateLayout();
        Assert.Equal("b", group.SelectedKey);
        Assert.Same(group.Children[1], surface.FocusedElement);
        Assert.Equal(Vector2.Zero, panel.Size);
        Assert.False(surface.TryFocus(panel));
        surface.Dispatch(new UiKeyInput(Key.Home, true));
        surface.Dispatch(new UiKeyInput(Key.Home, false));
        surface.UpdateLayout();
        Assert.Same(panel, group.Children[0].Children[1]);
        Assert.Equal("a", group.SelectedKey);
        Assert.Equal(new[] { "b", "a" }, changes);
    }

    /// <summary>Invalid item topology fails before changing the current accepted tree.</summary>
    [Fact]
    public void TabTopologyIsValidatedBeforePublication()
    {
        Assert.Throws<InvalidOperationException>(() => new UiSurface(TabItem(key: "a")));
        Assert.Throws<InvalidOperationException>(() => new UiSurface(TabView()[Button()]));
        Assert.Throws<InvalidOperationException>(() => new UiSurface(TabView()[TabItem()]));
        using UiSurface surface = Surface(TabView(selectedKey: "a")[TabItem(key: "a")]);
        UiElement original = surface.RootElement.Children[0];
        Assert.Throws<ArgumentException>(() => surface.Rebuild(TabView()[TabItem(key: "a"), TabItem(key: "a")]));
        Assert.Same(original, surface.RootElement.Children[0]);
        Assert.Equal("a", Assert.IsType<TabViewElement>(surface.RootElement).SelectedKey);
    }

    /// <summary>Cancelled keyboard and pointer gestures never toggle the accepted value.</summary>
    [Fact]
    public void ExpanderCancellationAndCallbackExceptionAreStable()
    {
        int changes = 0;
        using UiSurface surface = Surface(Expander(onChanged: _ =>
        {
            changes++;
            throw new InvalidOperationException("callback");
        }));
        surface.TryFocus(surface.RootElement);
        surface.Dispatch(new UiKeyInput(Key.Space, true));
        surface.Dispatch(new UiFocusLostInput());
        surface.Dispatch(new UiKeyInput(Key.Space, false));
        Assert.False(Assert.IsType<ExpanderElement>(surface.RootElement).IsExpanded);
        Assert.Throws<InvalidOperationException>(() => Click(surface, new(2)));
        Assert.True(Assert.IsType<ExpanderElement>(surface.RootElement).IsExpanded);
        Assert.Equal(1, changes);
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(2)));
        Assert.Equal(1, changes);
    }

    /// <summary>Both named slots enforce cardinality and reject ordinary content.</summary>
    [Fact]
    public void NamedSlotsValidateBeforeMount()
    {
        Assert.Throws<ArgumentException>(() => Expander()[Button()]);
        Assert.Throws<ArgumentException>(() => TabItem(key: "a")[TabItem.Header()[Button(), Button()]]);
        Assert.Throws<ArgumentException>(() => Expander()[Expander.Content()[Button(), Button()]]);
    }

    /// <summary>Desired expansion is idempotent and never pretends to be checked.</summary>
    [Fact]
    public void DesiredExpansionUsesActualHeaderProbeAndLatestCallback()
    {
        int calls = 0;
        Compose.Definitions.Expander declaration = Expander();
        using UiSurface surface = Surface(declaration);
        declaration.OnChanged = value =>
        {
            Assert.Equal(value, Assert.IsType<ExpanderElement>(surface.RootElement).IsExpanded);
            calls++;
        };
        Assert.True(surface.Dispatch(new UiExpandInput(surface.RootElement.AutomationId, true, new(2))));
        Assert.True(surface.Dispatch(new UiExpandInput(surface.RootElement.AutomationId, true, new(2))));
        Assert.Equal(1, calls);
        UiAutomationNode node = surface.CaptureAutomationSnapshot().Nodes[0];
        Assert.True(node.IsExpanded);
        Assert.Null(node.IsChecked);
        Assert.Equal(UiRole.Expander, node.Role);
        Assert.False(surface.Dispatch(new UiCheckInput(node.Id, false, new(2))));
        Assert.False(surface.Dispatch(new UiExpandInput(node.Id, false, new(100))));
    }

    /// <summary>New declarations adopt silently, while same declaration layout preserves accepted state.</summary>
    [Fact]
    public void ExpansionRebuildAdoptsSilentlyAndPreservesIdentity()
    {
        int calls = 0;
        using UiSurface surface = Surface(Expander(onChanged: _ => calls++)[Expander.Content()[Button()]]);
        UiElement content = surface.RootElement.Children[0];
        Click(surface, new(2));
        surface.UpdateLayout();
        surface.UpdateLayout();
        Assert.True(Assert.IsType<ExpanderElement>(surface.RootElement).IsExpanded);
        surface.Rebuild(Expander(isExpanded: false, onChanged: _ => calls++)[Expander.Content()[Button()]]);
        surface.UpdateLayout();
        Assert.Same(content, surface.RootElement.Children[0]);
        Assert.False(Assert.IsType<ExpanderElement>(surface.RootElement).IsExpanded);
        Assert.Equal(1, calls);
    }

    /// <summary>Unknown selected keys normalize to null and keyed reorder retains panels.</summary>
    [Fact]
    public void SelectionReorderRemoveAndUnknownAreSilent()
    {
        using UiSurface surface = Surface(TabView(selectedKey: "a")[TabItem(key: "a"), TabItem(key: "b")]);
        UiElement a = surface.RootElement.Children[0];
        surface.Rebuild(TabView(selectedKey: "b")[TabItem(key: "b"), TabItem(key: "a")]);
        surface.UpdateLayout();
        Assert.Same(a, surface.RootElement.Children[1]);
        surface.Rebuild(TabView(selectedKey: "missing")[TabItem(key: "a")]);
        surface.UpdateLayout();
        Assert.Null(Assert.IsType<TabViewElement>(surface.RootElement).SelectedKey);
        Assert.Same(a, surface.RootElement.Children[0]);
    }

    /// <summary>Arrow navigation skips disabled headers and explicit focus does not select.</summary>
    [Fact]
    public void RovingFocusSkipsDisabledAndHomeEndActivate()
    {
        using UiSurface surface = Surface(TabView(selectedKey: "a")[TabItem(key: "a"), TabItem(key: "b", isEnabled: false), TabItem(key: "c")]);
        TabViewElement group = Assert.IsType<TabViewElement>(surface.RootElement);
        surface.TryFocus(group.Children[2]);
        Assert.Equal("a", group.SelectedKey);
        surface.Dispatch(new UiKeyInput(Key.Home, true));
        surface.Dispatch(new UiKeyInput(Key.Home, false));
        Assert.Same(group.Children[0], surface.FocusedElement);
        surface.Dispatch(new UiKeyInput(Key.ArrowRight, true));
        surface.Dispatch(new UiKeyInput(Key.ArrowRight, false));
        Assert.Equal("c", group.SelectedKey);
    }

    /// <summary>Signals adopt silently and shared tab declarations retain separate accepted selections.</summary>
    [Fact]
    public void SharedTabsAndSignalAdoptionAreIndependent()
    {
        var selected = Signal.Create<string?>("a");
        int changes = 0;
        Compose.Definitions.TabView declaration = TabView(selectedKey: selected, onSelectionChanged: _ => changes++)[TabItem(key: "a"), TabItem(key: "b")];
        using UiSurface first = Surface(declaration);
        using UiSurface second = Surface(declaration);
        first.TryFocus(first.RootElement.Children[1]);
        first.Dispatch(new UiKeyInput(Key.Space, true));
        first.Dispatch(new UiKeyInput(Key.Space, false));
        Assert.Equal("b", Assert.IsType<TabViewElement>(first.RootElement).SelectedKey);
        Assert.Equal("a", Assert.IsType<TabViewElement>(second.RootElement).SelectedKey);
        Assert.Equal("a", selected.Value);
        selected.Value = "missing";
        first.UpdateLayout();
        second.UpdateLayout();
        Assert.Null(Assert.IsType<TabViewElement>(first.RootElement).SelectedKey);
        Assert.Null(Assert.IsType<TabViewElement>(second.RootElement).SelectedKey);
        Assert.Equal(1, changes);
    }

    /// <summary>Interactive header content owns activation and does not duplicate the parent callback.</summary>
    [Fact]
    public void InteractiveHeaderDoesNotDoubleActivate()
    {
        int inner = 0;
        int outer = 0;
        using UiSurface surface = Surface(Expander(onChanged: _ => outer++)[Expander.Header()[Button(width: 80, height: 24, onClick: () => inner++)]]);
        Click(surface, new(2));
        Assert.Equal(1, inner);
        Assert.Equal(0, outer);
        Assert.False(Assert.IsType<ExpanderElement>(surface.RootElement).IsExpanded);
    }

    /// <summary>Normal pointer and key captures in collapsed content are cancelled before later Up.</summary>
    [Fact]
    public void CollapsedContentCancelsHeldKeysWithoutFalseButtonCommit()
    {
        int calls = 0;
        using UiSurface surface = Surface(Expander(isExpanded: true)[Expander.Content()[Button(width: 80, height: 24, onClick: () => calls++)]]);
        UiElement button = surface.RootElement.Children[0];
        Assert.True(surface.TryFocus(button));
        surface.Dispatch(new UiKeyInput(Key.Space, true));
        surface.Rebuild(Expander(isExpanded: false)[Expander.Content()[Button(width: 80, height: 24, onClick: () => calls++)]]);
        surface.UpdateLayout();
        surface.Dispatch(new UiKeyInput(Key.Space, false));
        Assert.Equal(0, calls);
        Assert.Same(button, surface.RootElement.Children[0]);
        Assert.Same(surface.RootElement, surface.FocusedElement);
    }

    /// <summary>A newer focus request from blur supersedes the internal header restoration.</summary>
    [Fact]
    public void NewerApplicationFocusWinsCollapseRestoration()
    {
        UiSurface? current = null;
        var probe = new BlurButton(() => current!.TryFocus(current.RootElement.Children[1]));
        using UiSurface surface = Surface(Stack()[Expander(isExpanded: true)[Expander.Content()[probe]], Button(width: 40, height: 24)]);
        current = surface;
        UiElement editor = surface.RootElement.Children[0].Children[0];
        surface.TryFocus(editor);
        surface.Rebuild(Stack()[Expander(isExpanded: false)[Expander.Content()[probe]], Button(width: 40, height: 24)]);
        surface.UpdateLayout();
        Assert.Same(surface.RootElement.Children[1], surface.FocusedElement);
        Assert.False(surface.TryFocus(editor));
    }

    /// <summary>Releasing a captured header press over content does not activate the header.</summary>
    [Fact]
    public void DraggingHeaderPressIntoContentDoesNotCollapse()
    {
        int calls = 0;
        using UiSurface surface = Surface(Expander(isExpanded: true, onChanged: _ => calls++)[Expander.Header()[Rectangle(width: 80, height: 24)], Expander.Content()[Rectangle(width: 80, height: 80)]]);
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(2)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Move, new(2, 40)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(2, 40)));
        Assert.True(Assert.IsType<ExpanderElement>(surface.RootElement).IsExpanded);
        Assert.Equal(0, calls);
    }

    /// <summary>An unrelated sibling group cannot replace the owning group's focus restoration.</summary>
    [Fact]
    public void SiblingTabGroupDoesNotStealFocusRestoration()
    {
        using UiSurface surface = Surface(Stack()[
            TabView(key: "a", selectedKey: "one")[TabItem(key: "one")[TabItem.Content()[Button(width: 40, height: 24)]], TabItem(key: "two")],
            TabView(key: "b", selectedKey: "one")[TabItem(key: "one"), TabItem(key: "two")]]);
        UiElement owner = surface.RootElement.Children[0];
        Assert.True(surface.TryFocus(owner.Children[0].Children[0]));
        surface.Rebuild(Stack()[
            TabView(key: "a", selectedKey: "two")[TabItem(key: "one")[TabItem.Content()[Button(width: 40, height: 24)]], TabItem(key: "two")],
            TabView(key: "b", selectedKey: "one")[TabItem(key: "one"), TabItem(key: "two")]]);
        surface.UpdateLayout();
        Assert.Same(owner.Children[1], surface.FocusedElement);
    }

    /// <summary>Reactive sibling visibility invalidates every dependent header position.</summary>
    [Fact]
    public void ReactiveVisibilityRepositionsFollowingHeader()
    {
        var visible = Signal.Create(true);
        using UiSurface surface = Surface(TabView()[TabItem(key: "a"), TabItem(key: "b", isVisible: visible), TabItem(key: "c", testId: "third")]);
        Assert.Equal(48, surface.CaptureAutomationSnapshot().Nodes.Single(node => node.TestId == "third").ActionableBounds.Y);
        visible.Value = false;
        surface.UpdateLayout();
        Assert.Equal(24, surface.CaptureAutomationSnapshot().Nodes.Single(node => node.TestId == "third").ActionableBounds.Y);
        visible.Value = true;
        surface.UpdateLayout();
        Assert.Equal(48, surface.CaptureAutomationSnapshot().Nodes.Single(node => node.TestId == "third").ActionableBounds.Y);
    }

    /// <summary>Programmatic header focus reveals its actual header through nested viewports.</summary>
    [Fact]
    public void NestedScrollFocusRevealsHeaderRatherThanWholePanel()
    {
        using var outer = new ScrollController();
        using var inner = new ScrollController();
        using UiSurface surface = Surface(Panel()[ScrollView(controller: outer, height: 48)[ScrollView(controller: inner, height: 96)[TabView()[TabItem(key: "a"), TabItem(key: "b"), TabItem(key: "c"), TabItem(key: "d"), TabItem(key: "e", testId: "last")]]]]);
        UiElement last = surface.FindElement(surface.CaptureAutomationSnapshot().Nodes.Single(node => node.TestId == "last").Id)!;
        Assert.True(surface.TryFocus(last));
        surface.UpdateLayout();
        Assert.True(outer.Offset > 0);
        Assert.True(inner.Offset > 0);
        Assert.Same(last, surface.FocusedElement);
        UiAutomationNode node = surface.CaptureAutomationSnapshot().Nodes.Single(item => item.TestId == "last");
        Assert.True(node.ActionableBounds.Height > 0);
    }

    /// <summary>A nested closing panel cannot overwrite its enclosing live header fallback.</summary>
    [Fact]
    public void NestedInactivePanelDoesNotOverrideOuterFocusRestoration()
    {
        using UiSurface surface = Surface(TabView(selectedKey: "a")[TabItem(key: "a")[TabItem.Content()[Expander(isExpanded: true)[Expander.Content()[Button(width: 40, height: 24)]]]], TabItem(key: "b")]);
        UiElement editor = surface.RootElement.Children[0].Children[0].Children[0];
        Assert.True(surface.TryFocus(editor));
        surface.Rebuild(TabView(selectedKey: "b")[TabItem(key: "a")[TabItem.Content()[Expander(isExpanded: false)[Expander.Content()[Button(width: 40, height: 24)]]]], TabItem(key: "b")]);
        surface.UpdateLayout();
        Assert.Same(surface.RootElement.Children[1], surface.FocusedElement);
        Assert.False(surface.TryFocus(editor));
    }

    private static UiSurface Surface(Lumyte.UI.Compose.Definitions.Widget widget)
    {
        var surface = new UiSurface(widget);
        surface.SetViewport(new(200, 200));
        surface.UpdateLayout();
        return surface;
    }

    private static void Click(UiSurface surface, Vector2 point)
    {
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, point));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, point));
    }

    private sealed class BlurButton : Lumyte.UI.Compose.Definitions.Button
    {
        internal BlurButton(Action blur)
        {
            Blur = blur;
            Width = 40;
            Height = 24;
        }

        internal Action Blur { get; }

        protected override UiElement CreateElement() => new BlurButtonElement(this);
    }

    private sealed class BlurButtonElement(BlurButton widget) : ButtonElement(widget)
    {
        protected override UiInputResult OnInput(in UiInputContext context)
        {
            UiInputResult result = base.OnInput(in context);
            if (context.Input is UiFocusChangedInput { IsFocused: false })
            {
                widget.Blur();
            }

            return result;
        }
    }
}
