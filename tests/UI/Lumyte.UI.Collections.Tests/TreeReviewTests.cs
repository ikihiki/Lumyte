using Xunit;
using static Lumyte.UI.Collections.Compose;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Collections.Tests;

/// <summary>Regression coverage for TreeView review findings.</summary>
public sealed class TreeReviewTests
{
    /// <summary>Other collections own their same-key focus, including interactive descendants.</summary>
    [Fact]
    public void OtherCollectionFocusIsNotStolen()
    {
        using var surface = new UiSurface(Stack()[
            TreeView<string>(roots: new[] { "root" }, keySelector: x => x, childrenSelector: _ => new[] { "child" }, hasChildren: x => x == "root", itemTemplate: _ => SizedBox(height: 20), height: 60),
            ListBox<string>(items: new[] { "root" }, keySelector: x => x, itemTemplate: _ => Button(height: 20), height: 60)]);
        surface.SetViewport(new(100, 120));
        surface.UpdateLayout();
        TreeViewElement<string> tree = Assert.IsType<TreeViewElement<string>>(surface.RootElement.Children[0]);
        UiElement other = surface.RootElement.Children[1].Children[0].Children[0].Children[0];
        Assert.True(surface.TryFocus(other));
        Assert.True(tree.SetExpanded("root", true));
        surface.UpdateLayout();
        Assert.Same(other, surface.FocusedElement);
        Assert.True(surface.TryFocus(other.Children[0]));
        UiElement nested = surface.FocusedElement!;
        Assert.True(tree.SetExpanded("root", false));
        surface.UpdateLayout();
        Assert.Same(nested, surface.FocusedElement);
    }

    /// <summary>Retained expansion keys do not disable leaf parent navigation.</summary>
    [Fact]
    public void RetainedLeafExpansionNavigatesToParent()
    {
        using UiSurface surface = Create(false, new[] { "root", "leaf99" });
        surface.SetViewport(new(100, 2060));
        surface.UpdateLayout();
        Assert.True(surface.TryFocus(Row(surface, "leaf99")));
        surface.Dispatch(new UiKeyInput(Lumyte.Input.Abstractions.Key.ArrowLeft, true));
        surface.UpdateLayout();
        Assert.Equal("root", surface.FocusedElement?.Widget.Key);
    }

    /// <summary>Explicit hierarchy focus wins over later list restoration across virtual windows.</summary>
    [Fact]
    public void VirtualLeafNavigatesToUnrealizedParent()
    {
        using UiSurface surface = Create(true);
        surface.SetViewport(new(100, 60));
        surface.UpdateLayout();
        TreeViewElement<string> tree = Assert.IsType<TreeViewElement<string>>(surface.RootElement);
        Assert.True(tree.BringPathIntoView(new[] { "root", "leaf99" }));
        surface.UpdateLayout();
        Assert.True(surface.TryFocus(Row(surface, "leaf99")));
        surface.Dispatch(new UiKeyInput(Lumyte.Input.Abstractions.Key.ArrowLeft, true));
        surface.UpdateLayout();
        Assert.Equal("root", surface.FocusedElement?.Widget.Key);
        Assert.Null(tree.SelectedKey);
    }

    /// <summary>Nonvirtual reveal publishes matching offset and actionable clipped geometry.</summary>
    [Fact]
    public void NonvirtualRevealConverges()
    {
        using UiSurface surface = Create(false);
        surface.SetViewport(new(100, 60));
        surface.UpdateLayout();
        TreeViewElement<string> tree = Assert.IsType<TreeViewElement<string>>(surface.RootElement);
        Assert.True(tree.BringPathIntoView(new[] { "root", "leaf99" }));
        surface.UpdateLayout();
        UiAutomationNode leaf = surface.CaptureAutomationSnapshot().Nodes.Single(n => n.OptionKey == "leaf99");
        Assert.True(leaf.ClippedBounds.Height > 0);
        Assert.True(leaf.ActionableBounds.Height > 0);
        Assert.InRange(leaf.ActionableBounds.Y, 0, 59);
    }

    /// <summary>Selection centers exclude expansion glyphs at depth three in a 100-pixel row.</summary>
    [Fact]
    public void DeepRowSelectionExcludesGlyph()
    {
        using var surface = new UiSurface(TreeView<int>(roots: new[] { 0 }, keySelector: x => x.ToString(System.Globalization.CultureInfo.InvariantCulture), childrenSelector: x => new[] { x + 1 }, hasChildren: x => x < 4, expandedKeys: new[] { "0", "1", "2" }, itemTemplate: _ => SizedBox(height: 20), height: 100));
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        TreeViewElement<int> tree = Assert.IsType<TreeViewElement<int>>(surface.RootElement);
        UiAutomationNode row = surface.CaptureAutomationSnapshot().Nodes.Single(n => n.OptionKey == "3");
        var point = new System.Numerics.Vector2(row.ActionableBounds.X + (row.ActionableBounds.Width / 2), row.ActionableBounds.Y + (row.ActionableBounds.Height / 2));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, point));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, point));
        surface.UpdateLayout();
        Assert.Equal("3", tree.SelectedKey);
        Assert.DoesNotContain("3", tree.ExpandedKeys);
    }

    /// <summary>A newer application focus request supersedes pending hierarchy navigation.</summary>
    [Fact]
    public void NewerApplicationFocusWinsHierarchyNavigation()
    {
        using UiSurface surface = Create(true);
        surface.SetViewport(new(100, 60));
        surface.UpdateLayout();
        TreeViewElement<string> tree = Assert.IsType<TreeViewElement<string>>(surface.RootElement);
        Assert.True(tree.BringPathIntoView(new[] { "root", "leaf99" }));
        surface.UpdateLayout();
        Assert.True(surface.TryFocus(Row(surface, "leaf99")));
        surface.Dispatch(new UiKeyInput(Lumyte.Input.Abstractions.Key.ArrowLeft, true));
        Assert.True(surface.TryFocus(null));
        surface.UpdateLayout();
        Assert.Null(surface.FocusedElement);
    }

    private static UiElement Row(UiSurface surface, string key) => surface.FindElement(surface.CaptureAutomationSnapshot().Nodes.Single(n => n.OptionKey == key).Id)!;

    private static UiSurface Create(bool virtualized, string[]? expanded = null) => new(TreeView<string>(roots: new[] { "root" }, keySelector: x => x, childrenSelector: _ => Enumerable.Range(0, 100).Select(i => "leaf" + i).ToArray(), hasChildren: x => x == "root", expandedKeys: expanded ?? Array.Empty<string>(), itemTemplate: _ => SizedBox(height: 20), height: expanded is null ? 60 : 2060, virtualization: virtualized ? new UiItemVirtualization(20, 0) : null));
}
