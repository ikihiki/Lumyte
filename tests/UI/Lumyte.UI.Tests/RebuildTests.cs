using Xunit;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Tests;

/// <summary>Checks declaration matching and component state without layout or input dependencies.</summary>
public sealed class RebuildTests
{
    /// <summary>Checks repeated traversal reuses storage and late child initialization invalidates every ancestor.</summary>
    [Fact]
    public void CachedSubtreeTracksLateChildInitialization()
    {
        Definitions.Panel child = Panel();
        Definitions.Panel root = Panel()[child];
        List<Definitions.Widget> before = root.CollectSubtree();
        Assert.Same(before, root.CollectSubtree());
        ulong revision = root.StructureRevision;
        child.Children.Initialize(Button());
        Assert.True(root.StructureRevision > revision);
        Assert.NotSame(before, root.CollectSubtree());
        Assert.Equal(3, root.CollectSubtree().Count);
        Assert.Equal(2, before.Count);
    }

    /// <summary>Checks queueing and validation preserve the connected tree until the update boundary.</summary>
    [Fact]
    public void RebuildQueuesAndRejectsInvalidOwnershipOrDuplicateKeys()
    {
        Definitions.Panel original = Panel();
        using var surface = new UiSurface(original);
        Assert.Throws<ArgumentException>(() => surface.Rebuild(Panel()[Button(key: "same"), Button(key: "same")]));
        Assert.Throws<ArgumentNullException>(() => surface.Rebuild(root: null!));
        Definitions.Panel foreign = Panel();
        using var other = new UiSurface(foreign);
        Assert.Throws<ArgumentException>(() => surface.Rebuild(foreign));
        Definitions.Panel next = Panel()[Button(key: "new")];
        surface.Rebuild(next);
        Assert.Same(original, surface.Root);
        surface.UpdateTree();
        Assert.Same(next, surface.Root);
        using var detached = new UiSurface(original);
        Assert.Throws<ArgumentException>(() => new UiSurface(next));
    }

    /// <summary>Checks stable keys follow state across sibling reordering.</summary>
    [Fact]
    public void KeyedChildrenRetainStateWhenReordered()
    {
        var first = new StatefulPanel { Key = "first" };
        var second = new StatefulPanel { Key = "second" };
        using var surface = new UiSurface(Panel()[first, second]);
        var nextFirst = new StatefulPanel { Key = "first" };
        var nextSecond = new StatefulPanel { Key = "second" };
        surface.Rebuild(Panel()[nextSecond, nextFirst]);
        surface.UpdateTree();
        Assert.Same(first.State, nextFirst.State);
        Assert.Same(second.State, nextSecond.State);
    }

    /// <summary>Checks unkeyed position matching and explicit type or key changes start a new lifetime.</summary>
    [Fact]
    public void PositionTypeAndKeyDetermineStateLifetime()
    {
        var first = new StatefulPanel();
        var changedKey = new StatefulPanel { Key = "old" };
        var changedType = new StatefulPanel();
        using var surface = new UiSurface(Panel()[first, changedKey, changedType]);
        var next = new StatefulPanel();
        var newKey = new StatefulPanel { Key = "new" };
        var newType = new OtherStatefulPanel();
        object keyState = newKey.State;
        object typeState = newType.State;
        surface.Rebuild(Panel()[next, newKey, newType]);
        surface.UpdateTree();
        Assert.Same(first.State, next.State);
        Assert.Same(keyState, newKey.State);
        Assert.Same(typeState, newType.State);
        Assert.NotSame(changedKey.State, newKey.State);
        Assert.NotSame(changedType.State, newType.State);
    }

    /// <summary>Checks keys never transfer state between component-owned slots.</summary>
    [Fact]
    public void KeysAreScopedToEachOwnedChildSlot()
    {
        var content = new StatefulPanel { Key = "same" };
        var background = new StatefulPanel { Key = "same" };
        using var surface = new UiSurface(Button()[content, Button.Background()[background]]);
        var nextContent = new StatefulPanel { Key = "same" };
        var nextBackground = new StatefulPanel { Key = "same" };
        surface.Rebuild(Button()[nextContent, Button.Background()[nextBackground]]);
        surface.UpdateTree();
        Assert.Same(content.State, nextContent.State);
        Assert.Same(background.State, nextBackground.State);
        Assert.NotSame(nextContent.State, nextBackground.State);
    }

    /// <summary>Checks a matching child key under a replaced parent cannot retain state.</summary>
    [Fact]
    public void ReplacedParentStartsANewChildLifetime()
    {
        var child = new StatefulPanel { Key = "editor" };
        using var surface = new UiSurface(Panel(key: "old")[child]);
        var next = new StatefulPanel { Key = "editor" };
        surface.Rebuild(Panel(key: "new")[next]);
        surface.UpdateTree();
        Assert.NotSame(child.State, next.State);
    }

    /// <summary>Checks preparation failure leaves old declarations connected and permits a replacement request.</summary>
    [Fact]
    public void FailedPreparationKeepsTheOldTree()
    {
        var original = new StatefulPanel();
        using var surface = new UiSurface(original);
        var failed = new StatefulPanel { Prepared = () => throw new ArithmeticException() };
        surface.Rebuild(failed);
        Assert.Throws<ArithmeticException>(surface.UpdateTree);
        Assert.Same(original, surface.Root);
        Assert.Throws<ArgumentException>(() => new UiSurface(original));
        var next = new StatefulPanel();
        surface.Rebuild(next);
        surface.UpdateTree();
        Assert.Same(original.State, next.State);
    }

    /// <summary>Checks latest request wins and a new request queued during preparation survives the current commit.</summary>
    [Fact]
    public void RequestsQueuedDuringPreparationWaitForTheNextUpdate()
    {
        var original = new StatefulPanel();
        using var surface = new UiSurface(original);
        var last = new StatefulPanel();
        var next = new StatefulPanel { Prepared = () => surface.Rebuild(last) };
        surface.Rebuild(new StatefulPanel());
        surface.Rebuild(next);
        surface.UpdateTree();
        Assert.Same(next, surface.Root);
        surface.UpdateTree();
        Assert.Same(last, surface.Root);
        Assert.Same(original.State, last.State);
    }

    /// <summary>Checks requeueing the same declaration preserves a later request made during reconciliation.</summary>
    [Fact]
    public void RequeueingTheApplyingDeclarationDoesNotReplaceANewerRequest()
    {
        using var surface = new UiSurface(new StatefulPanel());
        var last = new StatefulPanel();
        StatefulPanel? next = null;
        next = new StatefulPanel
        {
            Prepared = () =>
            {
                surface.Rebuild(next!);
                surface.Rebuild(last);
            },
        };
        surface.Rebuild(next);
        surface.Rebuild(next);
        surface.UpdateTree();
        Assert.Same(next, surface.Root);
        surface.UpdateTree();
        Assert.Same(last, surface.Root);
        Assert.Same(next.State, last.State);
    }

    /// <summary>Checks disposal during preparation does not reconnect either declaration.</summary>
    [Fact]
    public void DisposalDuringPreparationStopsAttachment()
    {
        var original = new StatefulPanel();
        var surface = new UiSurface(original);
        var next = new StatefulPanel { Prepared = surface.Dispose };
        surface.Rebuild(next);
        surface.UpdateTree();
        Assert.Throws<ObjectDisposedException>(surface.UpdateTree);
        using var oldSurface = new UiSurface(original);
        using var newSurface = new UiSurface(next);
    }

    /// <summary>Checks reconciliation supports deep trees without recursive matching.</summary>
    [Fact]
    public void DeepTreesRetainStateWithoutRecursiveMatching()
    {
        var originalLeaf = new StatefulPanel();
        var nextLeaf = new StatefulPanel();
        Definitions.Widget before = originalLeaf;
        Definitions.Widget after = nextLeaf;
        for (int depth = 0; depth < 20000; depth++)
        {
            before = Panel()[before];
            after = Panel()[after];
        }

        using var surface = new UiSurface(before);
        surface.Rebuild(after);
        surface.UpdateTree();
        Assert.Same(originalLeaf.State, nextLeaf.State);
    }

    private class StatefulPanel : Definitions.Panel
    {
        internal object State { get; private set; } = new();

        internal Action? Prepared { get; init; }

        protected override void OnReconcile(Definitions.Widget previous)
        {
            Prepared?.Invoke();
            State = ((StatefulPanel)previous).State;
        }
    }

    private sealed class OtherStatefulPanel : StatefulPanel
    {
    }
}
