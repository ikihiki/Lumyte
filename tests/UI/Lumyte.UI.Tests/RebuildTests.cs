using Xunit;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Tests;

/// <summary>Checks state identity, preparation and lifecycle independently of rendering services.</summary>
public sealed class RebuildTests
{
    /// <summary>Checks runtime slot views remain stable while the current configuration changes.</summary>
    [Fact]
    public void SlotViewsAreIndependentOfConstructionTimeChildren()
    {
        Definitions.Panel original = Panel()[new StatefulPanel { Key = "first" }];
        using var surface = new UiSurface(original);
        UiElement root = surface.RootElement;
        UiElement child = Assert.Single(root.Children);
        IReadOnlyList<UiElement> children = root.Children;
        var nextChild = new StatefulPanel { Key = "first" };
        surface.Rebuild(Panel()[nextChild]);
        surface.UpdateTree();
        Assert.Same(root, surface.RootElement);
        Assert.Same(children, root.Children);
        Assert.Same(child, Assert.Single(children));
        Assert.Same(nextChild, child.Widget);
        Assert.NotSame(nextChild, Assert.Single(original.Children));
    }

    /// <summary>Checks declarations remain queued and duplicate keys fail before publication.</summary>
    [Fact]
    public void RebuildQueuesSharedDeclarationsAndRejectsDuplicateKeys()
    {
        Widget original = Panel();
        using var surface = new UiSurface(original);
        Assert.Throws<ArgumentException>(() => surface.Rebuild(Panel()[Button(key: "same"), Button(key: "same")]));
        Assert.Throws<ArgumentNullException>(() => surface.Rebuild((Definitions.Widget)null!));
        Definitions.Panel child = Panel();
        _ = Panel()[child];
        surface.Rebuild(child);
        surface.UpdateTree();
        Assert.Same(child, surface.Root);
        surface.Rebuild(original);
        surface.UpdateTree();
        Widget next = Panel()[Button()];
        surface.Rebuild(next);
        Assert.Same(original, surface.Root);
        surface.UpdateTree();
        Assert.Same(next, surface.Root);
    }

    /// <summary>Checks keyed children keep ordinary fields without any state-transfer callback.</summary>
    [Fact]
    public void KeyedChildrenRetainStateWhenReordered()
    {
        using var surface = new UiSurface(Panel()[new StatefulPanel { Key = "first" }, new StatefulPanel { Key = "second" }]);
        StateElement first = Assert.IsType<StateElement>(surface.RootElement.Children[0]);
        StateElement second = Assert.IsType<StateElement>(surface.RootElement.Children[1]);
        first.Count = 42;
        surface.Rebuild(Panel()[new StatefulPanel { Key = "second" }, new StatefulPanel { Key = "first" }]);
        surface.UpdateTree();
        Assert.Same(second, surface.RootElement.Children[0]);
        Assert.Same(first, surface.RootElement.Children[1]);
        Assert.Equal(42, first.Count);
        Assert.Equal(1, first.Mounts);
        Assert.Equal(0, first.Unmounts);
    }

    /// <summary>Checks position, type, key and parent identity define the runtime lifetime.</summary>
    [Fact]
    public void PositionTypeAndKeyDetermineStateLifetime()
    {
        using var surface = new UiSurface(Panel()[new StatefulPanel(), new StatefulPanel { Key = "old" }, new StatefulPanel()]);
        UiElement[] before = surface.RootElement.Children.ToArray();
        surface.Rebuild(Panel()[new StatefulPanel(), new StatefulPanel { Key = "new" }, new OtherStatefulPanel()]);
        surface.UpdateTree();
        Assert.Same(before[0], surface.RootElement.Children[0]);
        Assert.NotSame(before[1], surface.RootElement.Children[1]);
        Assert.NotSame(before[2], surface.RootElement.Children[2]);
        Assert.False(before[1].IsMounted);
        Assert.False(before[2].IsMounted);
        UiElement child = surface.RootElement.Children[0];
        surface.Rebuild(Panel(key: "new-parent")[new StatefulPanel()]);
        surface.UpdateTree();
        Assert.NotSame(child, surface.RootElement.Children[0]);
        Assert.False(child.IsMounted);
    }

    /// <summary>Checks identical keys in independent slots do not share state.</summary>
    [Fact]
    public void KeysAreScopedToEachOwnedChildSlot()
    {
        using var surface = new UiSurface(Button()[new StatefulPanel { Key = "same" }, Button.Background()[new StatefulPanel { Key = "same" }]]);
        UiElement[] before = surface.RootElement.Children.ToArray();
        surface.Rebuild(Button()[new StatefulPanel { Key = "same" }, Button.Background()[new StatefulPanel { Key = "same" }]]);
        surface.UpdateTree();
        Assert.Same(before[0], surface.RootElement.Children[0]);
        Assert.Same(before[1], surface.RootElement.Children[1]);
        Assert.NotSame(before[0], before[1]);
    }

    /// <summary>Checks preparation failure leaves the old configuration, topology and fields unchanged.</summary>
    [Fact]
    public void FailedPreparationKeepsTheOldTree()
    {
        var original = new StatefulPanel();
        using var surface = new UiSurface(original);
        StateElement element = Assert.IsType<StateElement>(surface.RootElement);
        element.Count = 5;
        surface.Rebuild(new StatefulPanel { Prepared = () => throw new ArithmeticException() });
        Assert.Throws<ArithmeticException>(surface.UpdateTree);
        Assert.Same(original, element.Widget);
        Assert.Equal(5, element.Count);
        surface.Rebuild(new StatefulPanel());
        surface.UpdateTree();
        Assert.Same(element, surface.RootElement);
        Assert.Equal(5, element.Count);
    }

    /// <summary>Checks a request made during prepare survives publication and waits for the next boundary.</summary>
    [Fact]
    public void RequestsQueuedDuringPreparationWaitForTheNextUpdate()
    {
        using var surface = new UiSurface(new StatefulPanel());
        UiElement element = surface.RootElement;
        var last = new StatefulPanel();
        var next = new StatefulPanel { Prepared = () => surface.Rebuild(last) };
        surface.Rebuild(next);
        surface.UpdateTree();
        Assert.Same(next, surface.Root);
        surface.UpdateTree();
        Assert.Same(last, surface.Root);
        Assert.Same(element, surface.RootElement);
    }

    /// <summary>Checks reentrant update and requeueing do not duplicate the applying candidate.</summary>
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
                surface.UpdateTree();
                surface.Rebuild(next!);
                surface.Rebuild(last);
            },
        };
        surface.Rebuild(next);
        surface.UpdateTree();
        Assert.Same(next, surface.Root);
        surface.UpdateTree();
        Assert.Same(last, surface.Root);
    }

    /// <summary>Checks disposal during prepare cannot remount the persistent instance.</summary>
    [Fact]
    public void DisposalDuringPreparationStopsAttachment()
    {
        var surface = new UiSurface(new StatefulPanel());
        UiElement element = surface.RootElement;
        surface.Rebuild(new StatefulPanel { Prepared = surface.Dispose });
        surface.UpdateTree();
        Assert.False(element.IsMounted);
        Assert.Throws<ObjectDisposedException>(surface.UpdateTree);
    }

    /// <summary>Checks deep matching preserves the actual leaf instance without recursive traversal.</summary>
    [Fact]
    public void DeepTreesRetainStateWithoutRecursiveMatching()
    {
        Widget before = new StatefulPanel();
        Widget after = new StatefulPanel();
        for (int depth = 0; depth < 20000; depth++)
        {
            before = Panel()[before];
            after = Panel()[after];
        }

        using var surface = new UiSurface(before);
        UiElement leaf = UiSurface.CollectElements(surface.RootElement)[^1];
        surface.Rebuild(after);
        surface.UpdateTree();
        Assert.Same(leaf, UiSurface.CollectElements(surface.RootElement)[^1]);
    }

    private class StatefulPanel : Definitions.Panel
    {
        internal Action? Prepared { get; init; }

        protected override UiElement CreateElement() => new StateElement(this);
    }

    private sealed class StateElement(StatefulPanel widget) : UiElement<StatefulPanel>(widget)
    {
        internal int Count { get; set; }

        internal int Mounts { get; private set; }

        internal int Unmounts { get; private set; }

        protected override Action? PrepareUpdate(StatefulPanel next, UiElementUpdateContext context)
        {
            next.Prepared?.Invoke();
            return null;
        }

        protected override void OnMounted() => Mounts++;

        protected override void OnUnmounted() => Unmounts++;
    }

    private sealed class OtherStatefulPanel : StatefulPanel
    {
    }
}
