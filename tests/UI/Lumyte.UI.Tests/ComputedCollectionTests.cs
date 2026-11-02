using Lumyte.Signals;
using Xunit;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Tests;

/// <summary>Checks stable keys, computed arrays and instance-local topology.</summary>
public sealed class ComputedCollectionTests
{
    /// <summary>Checks map insertion, removal and reordering retain keyed elements and callback state.</summary>
    [Fact]
    public void ComputeMapsArraysWithoutReconstructingUnrelatedSiblings()
    {
        var items = Signal.Create(new[] { "a", "b" });
        Widget stable = Button(key: "unrelated");
        using var surface = new UiSurface(Panel()[stable, Compute(() => Stack()[items.Value.Select(key => Button(key: key)).ToArray()])]);
        UiElement sibling = surface.RootElement.Children[0];
        UiElement group = surface.RootElement.Children[1];
        UiElement first = group.Children[0];
        UiElement removed = group.Children[1];
        items.Value = ["c", "a"];
        surface.UpdateTree();
        Assert.Same(sibling, surface.RootElement.Children[0]);
        Assert.Same(group, surface.RootElement.Children[1]);
        Assert.Equal(["c", "a"], group.Children.Select(child => child.Widget.Key));
        Assert.Same(first, group.Children[1]);
        Assert.False(removed.IsMounted);
        items.Value = [];
        surface.UpdateTree();
        Assert.Empty(group.Children);
        Assert.False(first.IsMounted);
    }

    /// <summary>Checks deliberate identity replacement rebuilds the declared content rather than borrowing old child slots.</summary>
    [Fact]
    public void ExplicitComputeIdentityReplacementRecreatesItsChildren()
    {
        var identity = Signal.Create("before");
        using var surface = new UiSurface(Compute(() => Panel(key: identity.Value)[Button(key: "content")]));
        UiElement previous = surface.RootElement;
        identity.Value = "after";
        surface.UpdateTree();
        Assert.NotSame(previous, surface.RootElement);
        Assert.False(previous.IsMounted);
        Assert.Equal("content", Assert.Single(surface.RootElement.Children).Widget.Key);
    }

    /// <summary>Checks a memoized map can be reused when an unrelated source reconstructs the parent.</summary>
    [Fact]
    public void ComputedWidgetArraysCanBeReusedAcrossParentsAndSurfaces()
    {
        var items = Signal.Create(new[] { "a", "b" });
        var version = Signal.Create(0);
        int maps = 0;
        Computed<Widget[]> rows = Signal.Computed(() =>
        {
            maps++;
            return items.Value.Select(key => (Widget)Button(key: key)).ToArray();
        });
        using var surface = new UiSurface(() =>
        {
            _ = version.Value;
            return Stack()[rows.Value];
        });
        UiElement retained = surface.RootElement.Children[0];
        version.Value++;
        surface.UpdateTree();
        Assert.Equal(1, maps);
        Assert.Same(retained, surface.RootElement.Children[0]);
        using var independent = new UiSurface(Stack()[rows.Value]);
        Assert.NotSame(retained, independent.RootElement.Children[0]);
        Assert.Same(retained.Widget, independent.RootElement.Children[0].Widget);
        items.Value = ["b", "a"];
        surface.UpdateTree();
        Assert.Equal(2, maps);
        Assert.Same(retained, surface.RootElement.Children[1]);
    }

    /// <summary>Checks declaration recipes preserve computed reads and shared cached child declarations.</summary>
    [Fact]
    public void RecipesPreserveComputedSubtreesAndSharedDeclarations()
    {
        var items = Signal.Create(new[] { "a" });
        Widget shared = Button(key: "cached");
        Widget root = Panel()[Panel()[shared], Panel()[shared], Compute(() => Stack()[items.Value.Select(key => Button(key: key)).ToArray()])];
        UiDeclarationRecipe recipe = Assert.IsType<UiDeclarationRecipe>(UiDeclarationRecipe.Capture(root));
        using var surface = new UiSurface(recipe.Build());
        items.Value = ["a", "b"];
        surface.UpdateTree();
        Assert.Equal(["a", "b"], surface.RootElement.Children[2].Children.Select(child => child.Widget.Key));
        recipe = Assert.IsType<UiDeclarationRecipe>(UiDeclarationRecipe.Capture(surface.RootElement));
        using var rebuilt = new UiSurface(recipe.Build());
        items.Value = ["b"];
        rebuilt.UpdateTree();
        Assert.Equal("b", Assert.Single(rebuilt.RootElement.Children[2].Children).Widget.Key);
        Assert.Single(rebuilt.RootElement.Children[0].Children);
        Assert.Single(rebuilt.RootElement.Children[1].Children);
    }
}
