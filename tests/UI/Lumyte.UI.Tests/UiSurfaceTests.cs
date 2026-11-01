using Xunit;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Tests;

/// <summary>Checks passive declarations and independent mounted lifetimes.</summary>
public sealed class UiSurfaceTests
{
    /// <summary>Checks null is rejected and shared declarations can describe independent surface roots.</summary>
    [Fact]
    public void ConstructorRejectsNullAndAcceptsSharedRoots()
    {
        Assert.Throws<ArgumentNullException>(() => new UiSurface(root: null!));
        Definitions.Panel child = Panel();
        Definitions.Panel root = Panel()[child];
        using var standalone = new UiSurface(child);
        using var surface = new UiSurface(root);
        Assert.Same(root, surface.Root);
        Assert.Same(surface.RootElement, Assert.Single(surface.RootElement.Children).Parent);
    }

    /// <summary>Checks one passive declaration can describe independent mounted instances.</summary>
    [Fact]
    public void SharedDeclarationsDoNotShareInstances()
    {
        Widget declaration = Panel()[Button()];
        using var first = new UiSurface(declaration);
        using var second = new UiSurface(declaration);
        Assert.Same(first.Root, second.Root);
        Assert.NotSame(first.RootElement, second.RootElement);
        UiElement child = Assert.Single(first.RootElement.Children);
        UiElement other = Assert.Single(second.RootElement.Children);
        Assert.NotSame(child, other);
        Assert.Same(first, child.Surface);
        Assert.Same(second, other.Surface);
        first.Dispose();
        Assert.False(child.IsMounted);
        Assert.True(other.IsMounted);
    }

    /// <summary>Checks sequential operations on another thread need no explicit runtime or dispatcher.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task TreesCanBeUpdatedAcrossThreadsSequentiallyAsync()
    {
        using var surface = new UiSurface(Panel());
        UiElement element = surface.RootElement;
        await Task.Run(() =>
        {
            surface.Rebuild(Panel()[Button()]);
            surface.UpdateTree();
        });
        Assert.Same(element, surface.RootElement);
        Assert.Single(element.Children);
        await Task.Run(surface.Dispose);
        Assert.False(element.IsMounted);
    }

    /// <summary>Checks removing a surface ends runtime instances but preserves the declaration.</summary>
    [Fact]
    public void ReconnectionCreatesNewInstancesWithoutConsumingTheDeclaration()
    {
        Widget declaration = Panel()[Button()];
        var surface = new UiSurface(declaration);
        UiElement previous = surface.RootElement;
        UiElement child = Assert.Single(previous.Children);
        surface.Dispose();
        surface.Dispose();
        Assert.False(previous.IsMounted);
        Assert.Null(child.Surface);
        Assert.Null(child.Parent);
        Assert.Empty(previous.Children);
        Assert.Throws<ObjectDisposedException>(() => _ = surface.Root);
        using var replacement = new UiSurface(declaration);
        Assert.Same(declaration, replacement.Root);
        Assert.NotSame(previous, replacement.RootElement);
        Assert.Throws<InvalidOperationException>(() => ((Definitions.Panel)declaration).Children.Initialize());
    }

    /// <summary>Checks connection and release use iterative runtime traversal on deeply nested declarations.</summary>
    [Fact]
    public void DeepTreesDoNotRequireRecursiveConnectionOrDisposal()
    {
        Widget root = Panel();
        for (int index = 0; index < 20000; index++)
        {
            root = Panel()[root];
        }

        using var surface = new UiSurface(root);
        Assert.Equal(20001, UiSurface.CollectElements(surface.RootElement).Count);
        surface.Dispose();
    }
}
