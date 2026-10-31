using Xunit;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Tests;

/// <summary>Tests surface lifetime and sequential cross-thread initialization of fixed widget trees.</summary>
public sealed class UiSurfaceTests
{
    /// <summary>Checks null, parented and connected roots are rejected without changing their owner.</summary>
    [Fact]
    public void ConstructorValidatesRootOwnership()
    {
        Assert.Throws<ArgumentNullException>(() => new UiSurface(root: null!));
        Definitions.Panel child = Panel();
        Definitions.Panel root = Panel()[child];
        Assert.Throws<ArgumentException>(() => new UiSurface(child));
        using var surface = new UiSurface(root);
        Assert.Throws<ArgumentException>(() => new UiSurface(root));
        Assert.Same(root, surface.Root);
    }

    /// <summary>Checks a live surface root cannot become another tree's content or background.</summary>
    [Fact]
    public void SurfaceRootsCannotBeAdopted()
    {
        Definitions.Panel root = Panel();
        using var surface = new UiSurface(root);
        Definitions.Panel destination = Panel();
        Definitions.Button button = Button();
        Assert.Throws<ArgumentException>(() => destination.Children.Initialize(root));
        Assert.Throws<ArgumentException>(() => button[Button.Background()[root]]);
        Assert.Null(root.Parent);
    }

    /// <summary>Checks first initialization connects complete subtrees and allows sequential worker use.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task ConnectedTreeCanBeInitializedAcrossThreadsSequentiallyAsync()
    {
        Definitions.Panel root = Panel();
        using var surface = new UiSurface(root);
        Definitions.Panel leaf = Panel();
        Definitions.Button child = Button()[Button.Background()[Panel()[leaf]]];
        await Task.Run(() => root.Children.Initialize(child));
        Assert.Same(root, child.Parent);
        Assert.Throws<ArgumentException>(() => new UiSurface(child));
        await Task.Run(() => leaf.Children.Initialize(Panel()));
        Assert.Same(leaf, Assert.Single(leaf.Children).Parent);
        await Task.Run(() => root.Width = 20);
        Assert.Equal(20, root.Width);
        await Task.Run(surface.Dispose);
        using var replacement = new UiSurface(root);
        Assert.Same(root, replacement.Root);
        Assert.Throws<InvalidOperationException>(() => root.Children.Initialize());
    }

    /// <summary>Checks connected validation failure leaves the first assignment available for retry.</summary>
    [Fact]
    public void FailedInitializationPreservesConnectionsAndAllowsRetry()
    {
        Definitions.Panel root = Panel();
        using var surface = new UiSurface(root);
        Definitions.Panel foreign = Panel();
        using var other = new UiSurface(foreign);
        Definitions.Panel fresh = Panel();
        Assert.Throws<ArgumentException>(() => root.Children.Initialize(fresh, foreign));
        Assert.Null(fresh.Parent);
        Assert.Empty(root.Children);
        root.Children.Initialize(fresh);
        Assert.Same(root, fresh.Parent);
    }

    /// <summary>Checks disconnecting a surface preserves child assignments and externally owned resources.</summary>
    [Fact]
    public void DisposalPreservesTheFixedTreeAndExternalResources()
    {
        var resource = new DisposablePanel();
        Definitions.Panel root = Panel()[Button()[Button.Background()[resource]]];
        var surface = new UiSurface(root);
        surface.Dispose();
        surface.Dispose();
        Assert.Throws<ObjectDisposedException>(() => _ = surface.Root);
        Assert.False(resource.IsDisposed);
        Assert.NotNull(resource.Parent);
        resource.Children.Initialize(Panel());
        using var replacement = new UiSurface(root);
        Assert.Same(root, replacement.Root);
    }

    /// <summary>Checks iterative connection and disposal of a deeply nested fixed tree.</summary>
    [Fact]
    public void DeepTreesDoNotRequireRecursiveConnectionOrDisposal()
    {
        Definitions.Panel leaf = Panel();
        Widget root = leaf;
        for (int index = 0; index < 20000; index++)
        {
            root = Panel()[root];
        }

        var surface = new UiSurface(root);
        leaf.Children.Initialize();
        surface.Dispose();
        Assert.Throws<InvalidOperationException>(() => leaf.Children.Initialize());
        Assert.NotNull(leaf.Parent);
    }

    private sealed class DisposablePanel : Definitions.Panel, IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }
}
