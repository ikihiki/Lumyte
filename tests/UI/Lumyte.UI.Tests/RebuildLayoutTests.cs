using Xunit;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Tests;

/// <summary>Checks layout consumes declaration changes through the common tree update boundary.</summary>
public sealed class RebuildLayoutTests
{
    /// <summary>Checks new declarations are applied before geometry and explicit tree updates leave layout dirty.</summary>
    [Fact]
    public void LayoutAppliesTheQueuedTreeAndComputesNewGeometry()
    {
        Definitions.Panel child = Panel(width: 20, height: 20);
        using var surface = new UiSurface(Panel()[child]);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        Assert.Equal(20, surface.RootElement.Children[0].Size.X);
        Definitions.Panel next = Panel(width: 40, height: 30);
        surface.Rebuild(Panel()[next]);
        surface.UpdateTree();
        surface.UpdateLayout();
        Assert.Equal(40, surface.RootElement.Children[0].Size.X);
        Assert.Equal(30, surface.RootElement.Children[0].Size.Y);
        Definitions.Panel last = Panel(width: 60, height: 50);
        surface.Rebuild(Panel()[last]);
        surface.UpdateLayout();
        Assert.Equal(60, surface.RootElement.Children[0].Size.X);
    }

    /// <summary>Checks configuration publication keeps geometry until the next layout on the same instance.</summary>
    [Fact]
    public void PublicationRetainsTheRuntimeInstanceAndInvalidatesOnlyItsGeometry()
    {
        using var surface = new UiSurface(Panel()[Panel(width: 20, height: 10)]);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        UiElement child = surface.RootElement.Children[0];
        surface.Rebuild(Panel()[Panel(width: 40, height: 10)]);
        surface.UpdateTree();
        Assert.Same(child, surface.RootElement.Children[0]);
        Assert.Equal(20, child.Size.X);
        Assert.True(child.IsLayoutDirty);
        surface.UpdateLayout();
        Assert.Equal(40, child.Size.X);
    }

    /// <summary>Checks equivalent built-in configuration does not invalidate a clean mounted tree.</summary>
    [Fact]
    public void EquivalentDeclarationsReuseComputedGeometry()
    {
        using var surface = new UiSurface(Panel()[Panel(width: 20, height: 10)]);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        UiElement root = surface.RootElement;
        surface.Rebuild(Panel()[Panel(width: 20, height: 10)]);
        surface.UpdateTree();
        Assert.Same(root, surface.RootElement);
        Assert.False(root.IsLayoutDirty);
        Assert.False(root.Children[0].IsLayoutDirty);
    }
}
