using Xunit;
using static Lumyte.UI.Collections.Compose;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Collections.Tests;

/// <summary>Deterministic fixed-extent realization contracts, not performance budgets.</summary>
public sealed class VirtualizationTests
{
    /// <summary>Large models create only the viewport and bounded overscan.</summary>
    [Fact]
    public void LargeModelCreatesOnlyVisibleRangeAndRetainsOverlap()
    {
        int templates = 0;
        using var surface = new UiSurface(ListBox<int>(
            items: Enumerable.Range(0, 100000).ToArray(),
            keySelector: x => x.ToString(System.Globalization.CultureInfo.InvariantCulture),
            itemTemplate: _ =>
        {
            templates++;
            return SizedBox(height: 20);
        },
            height: 60,
            virtualization: new UiItemVirtualization(20, 1)));
        Assert.Equal(0, templates);
        surface.SetViewport(new(100, 60));
        surface.UpdateLayout();
        Assert.Equal(4, templates);
        UiElement scroll = surface.RootElement.Children[0];
        UiElement overlap = scroll.Children[0].Children[1];
        ScrollController controller = Assert.IsType<ScrollViewElement>(scroll).Controller;
        Assert.Equal(2000000, controller.Extent);
        controller.ScrollTo(20);
        surface.UpdateLayout();
        Assert.Equal(5, templates);
        Assert.Same(overlap, scroll.Children[0].Children[1]);
        controller.ScrollTo(1000);
        surface.UpdateLayout();
        Assert.InRange(scroll.Children[0].Children.Count, 0, 5);
        controller.ScrollTo(0);
        surface.UpdateLayout();
        Assert.NotEqual(overlap.AutomationId, scroll.Children[0].Children[1].AutomationId);
    }

    /// <summary>Invalid options reject at construction and extent overflow precedes templates.</summary>
    [Fact]
    public void InvalidConfigurationAndOverflowRejectBeforeTemplates()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new UiItemVirtualization(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UiItemVirtualization(float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UiItemVirtualization(20, 1025));
        int templates = 0;
        Assert.Throws<ArgumentOutOfRangeException>(() => new UiSurface(ListBox<int>(
            items: new[] { 1, 2 },
            keySelector: x => x.ToString(System.Globalization.CultureInfo.InvariantCulture),
            itemTemplate: _ =>
        {
            templates++;
            return SizedBox();
        },
            virtualization: new UiItemVirtualization(float.MaxValue))));
        Assert.Equal(0, templates);
    }

    /// <summary>Zero viewport, resizing and horizontal layouts remain bounded.</summary>
    /// <param name="orientation">The uniform layout axis.</param>
    [Theory]
    [InlineData(UiOrientation.Vertical)]
    [InlineData(UiOrientation.Horizontal)]
    public void ViewportResizeAndKeyReveal(UiOrientation orientation)
    {
        int templates = 0;
        using var surface = new UiSurface(ListBox<int>(
            items: Enumerable.Range(0, 1000).ToArray(),
            keySelector: x => x.ToString(System.Globalization.CultureInfo.InvariantCulture),
            itemTemplate: _ =>
        {
            templates++;
            return SizedBox();
        },
            height: 60,
            orientation: orientation,
            virtualization: new UiItemVirtualization(20, 0)));
        surface.SetViewport(new(0, 0));
        surface.UpdateLayout();
        Assert.Equal(0, templates);
        surface.SetViewport(new(60, 60));
        surface.UpdateLayout();
        Assert.Equal(3, templates);
        ListBoxElement<int> list = Assert.IsType<ListBoxElement<int>>(surface.RootElement);
        Assert.True(list.BringIntoView("999"));
        surface.UpdateLayout();
        Assert.Equal(6, templates);
        Assert.Equal("999", surface.RootElement.Children[0].Children[0].Children.Last().Widget.Key);
        Assert.False(list.BringIntoView("missing"));
    }

    /// <summary>Shared passive declarations realize independent bounded trees per surface.</summary>
    [Fact]
    public void SharedDeclarationHasIndependentRealization()
    {
        Compose.Definitions.ListBox<int> declaration = ListBox<int>(items: Enumerable.Range(0, 1000).ToArray(), keySelector: x => x.ToString(System.Globalization.CultureInfo.InvariantCulture), itemTemplate: _ => SizedBox(height: 20), height: 60, virtualization: new UiItemVirtualization(20, 0));
        using var first = new UiSurface(declaration);
        using var second = new UiSurface(declaration);
        first.SetViewport(new(100, 60));
        second.SetViewport(new(100, 60));
        first.UpdateLayout();
        second.UpdateLayout();
        ListBoxElement<int> firstList = Assert.IsType<ListBoxElement<int>>(first.RootElement);
        Assert.True(firstList.BringIntoView("999"));
        first.UpdateLayout();
        Assert.Equal("999", first.RootElement.Children[0].Children[0].Children.Last().Widget.Key);
        Assert.Equal("0", second.RootElement.Children[0].Children[0].Children[0].Widget.Key);
    }

    /// <summary>A focused item remains one bounded clipped pin during range eviction.</summary>
    [Fact]
    public void FocusedItemRemainsBoundedAndClipped()
    {
        using var surface = new UiSurface(ListBox<int>(items: Enumerable.Range(0, 1000).ToArray(), keySelector: x => x.ToString(System.Globalization.CultureInfo.InvariantCulture), itemTemplate: _ => SizedBox(height: 20), height: 60, virtualization: new UiItemVirtualization(20, 0)));
        surface.SetViewport(new(100, 60));
        surface.UpdateLayout();
        UiElement focused = surface.RootElement.Children[0].Children[0].Children[0];
        Assert.True(surface.TryFocus(focused));
        surface.UpdateLayout();
        ScrollController controller = Assert.IsType<ScrollViewElement>(surface.RootElement.Children[0]).Controller;
        controller.ScrollTo(1000);
        surface.UpdateLayout();
        Assert.Same(focused, surface.FocusedElement);
        Assert.Equal(4, surface.RootElement.Children[0].Children[0].Children.Count);
        UiAutomationNode node = surface.CaptureAutomationSnapshot().Nodes.Single(item => item.Id == focused.AutomationId);
        Assert.Equal(0, node.ClippedBounds.Height);
    }

    /// <summary>Only an actual group can handle the bounded owner operation.</summary>
    [Fact]
    public void TypedKeyInputValidatesOwnerAndPoint()
    {
        using var surface = new UiSurface(ListBox<int>(items: Enumerable.Range(0, 1000).ToArray(), keySelector: x => x.ToString(System.Globalization.CultureInfo.InvariantCulture), itemTemplate: _ => SizedBox(height: 20), height: 60, virtualization: new UiItemVirtualization(20, 0)));
        surface.SetViewport(new(100, 60));
        surface.UpdateLayout();
        UiAutomationId group = surface.RootElement.AutomationId;
        Assert.False(surface.Dispatch(new UiBringItemInput(group, "999", new(200, 200))));
        Assert.True(surface.Dispatch(new UiBringItemInput(group, "999", new(10, 10))));
        surface.UpdateLayout();
        Assert.Equal("999", surface.RootElement.Children[0].Children[0].Children.Last().Widget.Key);
        Assert.Throws<ArgumentOutOfRangeException>(() => surface.Dispatch(new UiBringItemInput(group, new string('a', 1025), new(10, 10))));
    }

    /// <summary>A captured offscreen item cannot activate through stale visible coordinates.</summary>
    [Fact]
    public void CapturedOffscreenItemHasNoGhostActivation()
    {
        int changes = 0;
        using var surface = new UiSurface(ListBox<int>(items: Enumerable.Range(0, 1000).ToArray(), keySelector: item => item.ToString(System.Globalization.CultureInfo.InvariantCulture), itemTemplate: _ => SizedBox(height: 20), height: 60, virtualization: new UiItemVirtualization(20, 0), onSelectionChanged: _ => changes++));
        surface.SetViewport(new(100, 60));
        surface.UpdateLayout();
        UiElement old = surface.RootElement.Children[0].Children[0].Children[0];
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(10, 10)));
        surface.UpdateLayout();
        ScrollController controller = Assert.IsType<ScrollViewElement>(surface.RootElement.Children[0]).Controller;
        controller.ScrollTo(1000);
        surface.UpdateLayout();
        Assert.NotEqual(old.AutomationId, surface.ProbePointer(new(10, 10)).ActivationTarget);
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(10, 10)));
        Assert.Equal(0, changes);
        surface.Dispatch(new UiPointerInput(2, UiPointerPhase.Down, new(10, 10)));
        surface.Dispatch(new UiPointerInput(2, UiPointerPhase.Cancel, new(10, 10)));
        surface.UpdateLayout();
        Assert.False(old.IsMounted);
        controller.ScrollTo(0);
        surface.UpdateLayout();
        Assert.NotEqual(old.AutomationId, surface.RootElement.Children[0].Children[0].Children[0].AutomationId);
        Assert.Equal(0, changes);
    }

    /// <summary>A caller-held old declaration cannot reenter a new realization through a fresh function wrapper.</summary>
    /// <param name="generated">Whether the stale declaration is returned by a generated function.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EvictedTemplateDeclarationCannotBeResurrected(bool generated)
    {
        Lumyte.UI.Compose.Definitions.Widget first = SizedBox(height: 20);
        Lumyte.UI.Compose.Definitions.Widget Template(int item) => generated
            ? OwnershipFunctions.Generated(() => item == 0 ? first : SizedBox(height: 20))
            : item == 0 ? first : SizedBox(height: 20);
        using var surface = new UiSurface(ListBox<int>(items: Enumerable.Range(0, 1000).ToArray(), keySelector: item => item.ToString(System.Globalization.CultureInfo.InvariantCulture), itemTemplate: Template, height: 60, virtualization: new UiItemVirtualization(20, 0)));
        surface.SetViewport(new(100, 60));
        surface.UpdateLayout();
        ScrollViewElement scroll = Assert.IsType<ScrollViewElement>(surface.RootElement.Children[0]);
        scroll.Controller.ScrollTo(1000);
        surface.UpdateLayout();
        UiAutomationId current = scroll.Children[0].Children[0].AutomationId;
        scroll.Controller.ScrollTo(0);
        Assert.Throws<InvalidOperationException>(surface.UpdateLayout);
        Assert.Equal(1000, scroll.Controller.Offset);
        Assert.Equal(current, scroll.Children[0].Children[0].AutomationId);
    }

    /// <summary>Representable extreme extents keep a nonempty range at the finite scroll endpoint.</summary>
    /// <param name="horizontal">Whether to use the horizontal axis.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MaximumFiniteExtentKeepsEndpointItem(bool horizontal)
    {
        using var surface = new UiSurface(ListBox<int>(items: new[] { 0 }, keySelector: _ => "a", itemTemplate: _ => SizedBox(), orientation: horizontal ? UiOrientation.Horizontal : UiOrientation.Vertical, virtualization: new UiItemVirtualization(float.MaxValue, 0)));
        surface.SetViewport(new(100, 60));
        surface.UpdateLayout();
        ScrollViewElement scroll = Assert.IsType<ScrollViewElement>(surface.RootElement.Children[0]);
        scroll.Controller.ScrollTo(float.MaxValue);
        surface.UpdateLayout();
        Assert.Single(scroll.Children[0].Children);
        Assert.True(scroll.Controller.Offset < scroll.Controller.Extent);
        Assert.NotNull(surface.ProbePointer(new(10, 10)).ActivationTarget);
    }

    /// <summary>Minimum positive lengths and shrink preserve finite geometry without extra notifications.</summary>
    [Fact]
    public void MinimumPositiveExtentAndShrinkRemainFinite()
    {
        using var tiny = new UiSurface(ListBox<int>(items: Enumerable.Range(0, 10).ToArray(), keySelector: item => item.ToString(System.Globalization.CultureInfo.InvariantCulture), itemTemplate: _ => SizedBox(), virtualization: new UiItemVirtualization(float.Epsilon, 0)));
        tiny.SetViewport(new(100, 60));
        tiny.UpdateLayout();
        ScrollViewElement tinyScroll = Assert.IsType<ScrollViewElement>(tiny.RootElement.Children[0]);
        Assert.Equal(10, tinyScroll.Children[0].Children.Count);
        Assert.True(float.IsFinite(tinyScroll.Controller.Extent));
        Assert.Equal(0, tinyScroll.Controller.Maximum);
        using var surface = new UiSurface(ListBox<int>(items: Enumerable.Range(0, 100).ToArray(), keySelector: item => item.ToString(System.Globalization.CultureInfo.InvariantCulture), itemTemplate: _ => SizedBox(), virtualization: new UiItemVirtualization(20, 0)));
        surface.SetViewport(new(100, 60));
        surface.UpdateLayout();
        ScrollViewElement scroll = Assert.IsType<ScrollViewElement>(surface.RootElement.Children[0]);
        scroll.Controller.ScrollTo(1900);
        surface.UpdateLayout();
        int changes = 0;
        scroll.Controller.OnChanged = _ => changes++;
        surface.Rebuild(ListBox<int>(items: new[] { 0 }, keySelector: _ => "a", itemTemplate: _ => SizedBox(), virtualization: new UiItemVirtualization(20, 0)));
        surface.UpdateLayout();
        Assert.Equal(0, scroll.Controller.Offset);
        Assert.Equal(0, scroll.Controller.Maximum);
        Assert.Equal(0, changes);
        Assert.Single(scroll.Children[0].Children);
    }
}
