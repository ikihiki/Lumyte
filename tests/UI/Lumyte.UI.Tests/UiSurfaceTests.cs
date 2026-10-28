using Xunit;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Tests;

/// <summary>Tests surface ownership, sequential cross-thread access and resource lifetime.</summary>
public sealed class UiSurfaceTests
{
    /// <summary>Tests invalid roots without disconnecting their existing owner.</summary>
    [Fact]
    public void ConstructorRejectsNullParentedAndAlreadyConnectedRoots()
    {
        Assert.Throws<ArgumentNullException>(() => new UiSurface(null!));
        Definitions.Panel child = Panel();
        Definitions.Panel root = Panel()[child];
        Assert.Throws<ArgumentException>(() => new UiSurface(child));
        using var surface = new UiSurface(root);
        Assert.Throws<ArgumentException>(() => new UiSurface(root));
        Assert.Same(root, surface.Root);
        Assert.Same(root, child.Parent);
    }

    /// <summary>Tests that connected roots cannot become another tree's ordinary or background child.</summary>
    [Fact]
    public void SurfaceRootsCannotBeAdoptedByOtherTrees()
    {
        Definitions.Panel root = Panel();
        using var surface = new UiSurface(root);
        Definitions.Panel destination = Panel();
        Definitions.Button button = Button();
        Assert.Throws<ArgumentException>(() => destination.Children = [root]);
        Assert.Throws<ArgumentException>(() => button[Button.Background()[root]]);
        Assert.Null(root.Parent);
        Assert.Empty(destination.Children);
        Assert.Empty(button.BackgroundChildren);
    }

    /// <summary>Tests serialized reads and changes across threads without disconnecting the surface.</summary>
    [Fact]
    public void ConnectedTreeCanBeUsedSequentiallyAcrossThreads()
    {
        Definitions.Panel content = Panel();
        Definitions.Panel background = Panel();
        Definitions.Button button = Button()[content, Button.Background()[background]];
        Definitions.Panel root = Panel()[button];
        using var surface = new UiSurface(root);

        Assert.Null(OnOtherThread(() =>
        {
            Assert.Same(root, surface.Root);
            Assert.Same(button, content.Parent);
            Assert.Same(background, Assert.Single(button.BackgroundChildren));
            content.Children = [Panel()];
            background.Children = [Panel()];
        }));

        Assert.Same(content, Assert.Single(content.Children).Parent);
        Assert.Same(background, Assert.Single(background.Children).Parent);
        Assert.Null(OnOtherThread(() => root.Children = []));
        Assert.Empty(root.Children);
        Assert.Null(button.Parent);
        Assert.Same(root, surface.Root);
    }

    /// <summary>Tests an async caller using a worker sequentially without a dispatcher or a new surface.</summary>
    /// <returns>A task representing the sequential updates and disposal.</returns>
    [Fact]
    public async Task ConnectedTreeRemainsUsableAcrossAwaitAndWorkerUpdatesAsync()
    {
        Definitions.Panel root = Panel();
        using var surface = new UiSurface(root);
        await Task.Run(() => root.Children = [Button()[Button.Background()[Panel()]]]);
        Definitions.Button button = Assert.IsType<Definitions.Button>(Assert.Single(root.Children));
        Assert.Same(root, button.Parent);
        Assert.Same(button, Assert.Single(button.BackgroundChildren).Parent);

        await Task.Run(surface.Dispose);
        Assert.Throws<ObjectDisposedException>(() => _ = surface.Root);
        using var replacement = new UiSurface(root);
        Assert.Same(root, replacement.Root);
    }

    /// <summary>Tests recursive connection and disconnection after live tree replacement.</summary>
    [Fact]
    public void ReplacingConnectedChildrenUpdatesWholeSubtrees()
    {
        Definitions.Panel oldLeaf = Panel();
        Definitions.Panel old = Panel()[oldLeaf];
        Definitions.Panel root = Panel()[old];
        using var surface = new UiSurface(root);
        Definitions.Panel newLeaf = Panel();
        Definitions.Button replacement = Button()[Button.Background()[newLeaf]];

        root.Children = [replacement];
        Assert.Null(old.Parent);
        Assert.Same(old, oldLeaf.Parent);
        Assert.Null(OnOtherThread(() => oldLeaf.Children = [Panel()]));
        Assert.Null(OnOtherThread(() => newLeaf.Children = [Panel()]));
        Assert.Throws<ArgumentException>(() => new UiSurface(replacement));
        Assert.Same(root, replacement.Parent);
    }

    /// <summary>Tests reordering connected siblings without detaching the retained nodes.</summary>
    [Fact]
    public void ConnectedChildrenCanBeReorderedAndRetained()
    {
        Definitions.Panel first = Panel();
        Definitions.Panel second = Panel();
        Definitions.Panel root = Panel()[first, second];
        using var surface = new UiSurface(root);

        root.Children = [second, first];
        Assert.Same(second, root.Children[0]);
        Assert.Same(first, root.Children[1]);
        Assert.Same(root, first.Parent);
        Assert.Null(OnOtherThread(() => first.Children = []));
    }

    /// <summary>Tests that failed mixed replacements preserve every original connection.</summary>
    [Fact]
    public void FailedConnectedReplacementDoesNotDetachValidOldChildren()
    {
        Definitions.Panel old = Panel();
        Definitions.Panel root = Panel()[old];
        using var surface = new UiSurface(root);
        Definitions.Panel foreign = Panel();
        using var other = new UiSurface(foreign);
        Definitions.Panel fresh = Panel();

        Assert.Throws<ArgumentException>(() => root.Children = [fresh, foreign]);
        Assert.Same(old, Assert.Single(root.Children));
        Assert.Same(root, old.Parent);
        Assert.Null(fresh.Parent);
        Assert.Null(OnOtherThread(() => old.Children = []));
        Assert.Null(OnOtherThread(() => fresh.Children = []));
        Assert.Same(foreign, other.Root);
    }

    /// <summary>Tests background connection changes independently of ordinary content.</summary>
    [Fact]
    public void BackgroundReplacementRecursivelyReleasesTheOldBackground()
    {
        Definitions.Panel oldLeaf = Panel();
        Definitions.Panel oldBackground = Panel()[oldLeaf];
        Definitions.Button button = Button()[Button.Background()[oldBackground]];
        using var surface = new UiSurface(button);
        Definitions.Panel newLeaf = Panel();
        Definitions.Panel newBackground = Panel()[newLeaf];

        _ = button[Button.Background()[newBackground]];
        Assert.Null(oldBackground.Parent);
        Assert.Same(oldBackground, oldLeaf.Parent);
        Assert.Null(OnOtherThread(() => oldLeaf.Children = []));
        Assert.Null(OnOtherThread(() => newLeaf.Children = []));
    }

    /// <summary>Tests idempotent disposal and preservation of internal parent links and external ownership.</summary>
    [Fact]
    public void DisposalReleasesEveryConnectionWithoutDisposingWidgets()
    {
        var resource = new DisposablePanel();
        Definitions.Panel background = Panel()[resource];
        Definitions.Button button = Button()[Button.Background()[background]];
        Definitions.Panel root = Panel()[button];
        var surface = new UiSurface(root);

        Assert.Null(OnOtherThread(surface.Dispose));
        surface.Dispose();
        Assert.Throws<ObjectDisposedException>(() => _ = surface.Root);
        Assert.False(resource.IsDisposed);
        Assert.Same(background, resource.Parent);
        Assert.Same(button, background.Parent);
        Assert.Same(root, button.Parent);
        Assert.Null(OnOtherThread(() => resource.Children = [Panel()]));
        Assert.Null(OnOtherThread(surface.Dispose));
    }

    /// <summary>Tests transferring an entire disposed surface tree on another thread.</summary>
    [Fact]
    public void DisconnectedRootCanBeReattachedOnAnotherThread()
    {
        Definitions.Panel child = Panel();
        Definitions.Panel root = Panel()[child];
        var surface = new UiSurface(root);
        surface.Dispose();

        Exception? failure = OnOtherThread(() =>
        {
            using var replacement = new UiSurface(root);
            Assert.Same(root, replacement.Root);
            child.Children = [Panel()];
        });

        Assert.Null(failure);
        Assert.Same(root, child.Parent);
        using var finalSurface = new UiSurface(root);
        Assert.Same(root, finalSurface.Root);
    }

    /// <summary>Tests moving a detached subtree between two live surfaces.</summary>
    [Fact]
    public void DetachedSubtreeCanMoveToAnotherSurface()
    {
        Definitions.Panel leaf = Panel();
        Definitions.Panel subtree = Panel()[leaf];
        Definitions.Panel first = Panel()[subtree];
        Definitions.Button second = Button();
        using var firstSurface = new UiSurface(first);
        using var secondSurface = new UiSurface(second);

        first.Children = [];
        _ = second[Button.Background()[subtree]];
        Assert.Same(second, subtree.Parent);
        Assert.Same(subtree, leaf.Parent);
        Assert.Null(OnOtherThread(() => leaf.Children = []));
        Assert.Empty(first.Children);
    }

    /// <summary>Tests iterative connection and disconnection on a deeply nested tree.</summary>
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
        Assert.Null(OnOtherThread(() => leaf.Children = []));
        surface.Dispose();
        Assert.Null(OnOtherThread(() => leaf.Children = []));
        Assert.NotNull(leaf.Parent);
    }

    private static Exception? OnOtherThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.Start();
        thread.Join();
        return failure;
    }

    private sealed class DisposablePanel : Definitions.Panel, IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }
}
