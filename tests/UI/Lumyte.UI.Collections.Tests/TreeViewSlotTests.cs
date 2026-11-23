using Lumyte.Composition;
using Lumyte.Signals;
using Xunit;
using static Lumyte.UI.Collections.Compose;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Collections.Tests;

/// <summary>Checks concrete TreeView template ownership across hierarchy and recipe preparation.</summary>
public sealed class TreeViewSlotTests
{
    /// <summary>Slot replay binds the new declaration and expansion never evaluates a collapsed child.</summary>
    [Fact]
    public void TypedSlotReplayAndLazyExpansionUseReceivingOwner()
    {
        var height = Signal.Create<float?>(60);
        var owners = new List<Compose.Definitions.TreeView<string>>();
        int discoveries = 0;
        Lumyte.UI.Compose.Definitions.Widget Render(string value, Compose.Definitions.TreeView<string> owner)
        {
            owners.Add(owner);
            return SizedBox(height: 20);
        }

        Compose.Definitions.TreeView<string> declaration = TreeView<string>(
            roots: new[] { "root" },
            keySelector: value => value,
            childrenSelector: _ =>
            {
                discoveries++;
                return new[] { "child" };
            },
            hasChildren: value => value == "root",
            height: height,
            itemTemplate: _ => throw new InvalidOperationException("legacy"))[
            TreeViewFactory<string>().Item()[Render]];
        Assert.Empty(owners);
        using var surface = new UiSurface(declaration);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        TreeViewElement<string> tree = Assert.IsType<TreeViewElement<string>>(surface.RootElement);
        Assert.Same(declaration, Assert.Single(owners));
        Assert.Equal(0, discoveries);
        Assert.True(tree.SetExpanded("root", true));
        surface.UpdateLayout();
        Assert.Equal(new[] { "root", "child" }, tree.VisibleKeys);
        Assert.Equal(1, discoveries);
        Assert.All(owners, owner => Assert.Same(declaration, owner));
        height.Value = 80;
        surface.UpdateLayout();
        Compose.Definitions.TreeView<string> replacement = Assert.IsType<Compose.Definitions.TreeView<string>>(tree.Widget);
        Assert.NotSame(declaration, replacement);
        Assert.Same(replacement, owners.Last());
        Assert.Empty(declaration.ExpandedKeys);
        Assert.Equal(new[] { "root" }, tree.VisibleKeys);
    }

    /// <summary>Signals inside slot content update their mounted children without resetting accepted lazy state.</summary>
    [Fact]
    public void SlotChildSignalRefreshPreservesExpansionAndLazyCache()
    {
        var height = Signal.Create<float?>(20);
        int discoveries = 0;
        int templates = 0;
        Lumyte.UI.Compose.Definitions.Widget Render(string value, Compose.Definitions.TreeView<string> owner)
        {
            templates++;
            return SizedBox(height: height);
        }

        Compose.Definitions.TreeView<string> declaration = TreeView<string>(
            roots: new[] { "root" },
            keySelector: value => value,
            childrenSelector: _ =>
            {
                discoveries++;
                return new[] { "child" };
            },
            hasChildren: value => value == "root")[
            TreeViewFactory<string>().Item()[Render]];
        using var surface = new UiSurface(declaration);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        TreeViewElement<string> tree = Assert.IsType<TreeViewElement<string>>(surface.RootElement);
        Assert.True(tree.SetExpanded("root", true));
        surface.UpdateLayout();
        int previousTemplates = templates;
        height.Value = 30;
        surface.UpdateLayout();
        Assert.Same(declaration, tree.Widget);
        Assert.Equal(new[] { "root", "child" }, tree.VisibleKeys);
        Assert.Equal(new[] { "root" }, tree.ExpandedKeys);
        Assert.Equal(1, discoveries);
        Assert.Equal(previousTemplates, templates);
        Assert.True(tree.SetExpanded("root", false));
        surface.UpdateLayout();
        Assert.True(tree.SetExpanded("root", true));
        surface.UpdateLayout();
        Assert.Equal(1, discoveries);
        Assert.Empty(declaration.ExpandedKeys);
    }

    /// <summary>Fresh roots are required across receiving surfaces and failed templates publish nothing.</summary>
    [Fact]
    public void TypedSlotFailureAndFixedRootPreserveOwnership()
    {
        CompositionSlotAssignment<Compose.Definitions.TreeView<string>> slot = TreeViewFactory<string>().Item()[
            (_, _) => SizedBox(height: 20)];
        Compose.Definitions.TreeView<string> declaration = TreeView<string>(
            roots: new[] { "a" },
            keySelector: value => value,
            childrenSelector: _ => Array.Empty<string>(),
            hasChildren: _ => false,
            selectedKey: "a")[slot];
        using var first = new UiSurface(declaration);
        using var second = new UiSurface(declaration);
        first.SetViewport(new(100, 100));
        first.UpdateLayout();
        second.SetViewport(new(100, 100));
        second.UpdateLayout();
        Assert.NotSame(first.RootElement.Children[0], second.RootElement.Children[0]);
        UiElement old = first.RootElement.Children[0];
        first.Rebuild(TreeView<string>(
            roots: new[] { "b" },
            keySelector: value => value,
            childrenSelector: _ => Array.Empty<string>(),
            hasChildren: _ => false)[
            TreeViewFactory<string>().Item()[
            (_, _) => throw new InvalidOperationException("slot")]]);
        Assert.Throws<InvalidOperationException>(first.UpdateLayout);
        Assert.Same(old, first.RootElement.Children[0]);
        Assert.Equal("a", Assert.IsType<TreeViewElement<string>>(first.RootElement).SelectedKey);
        Lumyte.UI.Compose.Definitions.SizedBox shared = SizedBox();
        Assert.Throws<InvalidOperationException>(() => new UiSurface(TreeView<string>(
            roots: new[] { "a", "b" },
            keySelector: value => value,
            childrenSelector: _ => Array.Empty<string>(),
            hasChildren: _ => false)[TreeViewFactory<string>().Item()[shared]]));
    }
}
