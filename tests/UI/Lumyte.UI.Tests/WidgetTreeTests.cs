using System.Collections;
using Lumyte.Composition;
using Xunit;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Tests;

/// <summary>Exercises generated construction and transactional ownership through the public API.</summary>
public sealed class WidgetTreeTests
{
    /// <summary>Tests the generated factories, mixed content and background slots across an assembly boundary.</summary>
    [Fact]
    public void GeneratedCompositionOwnsNormalAndBackgroundChildren()
    {
        Definitions.Panel content = Panel();
        Definitions.Panel background = Panel();
        Definitions.Button button = Button()[content, Button.Background()[background]];
        Definitions.Panel root = Panel()[button];
        using var surface = new UiSurface(root);

        Assert.Same(root, surface.Root);
        Assert.Null(root.Parent);
        Assert.Same(root, button.Parent);
        Assert.Same(button, content.Parent);
        Assert.Same(button, background.Parent);
        Assert.Same(content, Assert.Single(button.Children));
        Assert.Same(background, Assert.Single(button.BackgroundChildren));
        Assert.NotSame(Panel(), Panel());
        Assert.NotSame(Button(), Button());
    }

    /// <summary>Tests generated with actions and inherited container ownership.</summary>
    [Fact]
    public void WithActionsAndCustomPanelsUseTheSameChildStorage()
    {
        Definitions.Panel child = Panel();
        Definitions.Panel root = Panel(with: [panel => panel.Children = [child]]);
        Assert.Same(root, child.Parent);

        var custom = new CustomPanel();
        root.Children = [];
        custom.Children = [child];
        Assert.Same(custom, child.Parent);
    }

    /// <summary>Tests input copying and immutable snapshots that remain unchanged after replacement.</summary>
    [Fact]
    public void ChildrenAreCopiedAndExposedAsReadOnlySnapshots()
    {
        Definitions.Panel first = Panel();
        Definitions.Panel second = Panel();
        Widget[] input = [first];
        Definitions.Panel root = Panel();
        root.Children = input;
        IReadOnlyList<Widget> snapshot = root.Children;
        input[0] = second;
        Assert.Same(first, Assert.Single(root.Children));
        Assert.Throws<NotSupportedException>(() => ((IList<Widget>)snapshot)[0] = second);

        root.Children = [second];
        Assert.Same(first, Assert.Single(snapshot));
        Assert.Null(first.Parent);
        Assert.Same(root, second.Parent);
    }

    /// <summary>Tests reference identity even when custom widgets define value equality.</summary>
    [Fact]
    public void OwnershipUsesReferenceIdentity()
    {
        var first = new EqualPanel();
        var second = new EqualPanel();
        Definitions.Panel root = Panel()[first, second];
        Assert.Equal(2, root.Children.Count);
        root.Children = [second, first];
        Assert.Same(second, root.Children[0]);
        Assert.Same(root, first.Parent);
        Assert.Same(root, second.Parent);
    }

    /// <summary>Tests null and duplicate input without disturbing a previously valid collection.</summary>
    [Fact]
    public void InvalidElementsLeaveThePreviousCollectionIntact()
    {
        Definitions.Panel old = Panel();
        Definitions.Panel replacement = Panel();
        Definitions.Panel root = Panel()[old];
        IReadOnlyList<Widget> snapshot = root.Children;

        Assert.Throws<ArgumentNullException>(() => root.Children = null!);
        Assert.Throws<ArgumentException>(() => root.Children = [replacement, null!]);
        Assert.Throws<ArgumentException>(() => root.Children = [replacement, replacement]);
        Assert.Same(snapshot, root.Children);
        Assert.Same(root, old.Parent);
        Assert.Null(replacement.Parent);
    }

    /// <summary>Tests self cycles and ancestor cycles, including a background ancestor.</summary>
    [Fact]
    public void SelfAndAncestorCyclesAreRejected()
    {
        Definitions.Panel leaf = Panel();
        Definitions.Button button = Button()[Button.Background()[leaf]];
        Definitions.Panel root = Panel()[button];

        Assert.Throws<ArgumentException>(() => root.Children = [root]);
        Assert.Throws<ArgumentException>(() => leaf.Children = [root]);
        Assert.Throws<ArgumentException>(() => button[Button.Background()[root]]);
        Assert.Same(root, button.Parent);
        Assert.Same(button, leaf.Parent);
        Assert.Empty(leaf.Children);
    }

    /// <summary>Tests exclusive ownership across ordinary parents and background roles.</summary>
    [Fact]
    public void MultipleParentsAndSlotsAreRejected()
    {
        Definitions.Panel child = Panel();
        Definitions.Button first = Button()[child];
        Definitions.Button second = Button();

        Assert.Throws<ArgumentException>(() => second.Children = [child]);
        Assert.Throws<ArgumentException>(() => first[Button.Background()[child]]);
        Assert.Empty(first.BackgroundChildren);
        Assert.Empty(second.Children);

        first.Children = [];
        _ = second[Button.Background()[child]];
        Assert.Same(second, child.Parent);
        Assert.Throws<ArgumentException>(() => second.Children = [child]);
    }

    /// <summary>Tests the single-child content rule independently of the multi-child background rule.</summary>
    [Fact]
    public void ButtonAllowsOneContentAndMultipleBackgroundChildren()
    {
        Definitions.Panel content = Panel();
        Definitions.Panel first = Panel();
        Definitions.Panel second = Panel();
        Definitions.Button button = Button()[content, Button.Background()[first, second]];

        Assert.Throws<ArgumentException>(() => button.Children = [Panel(), Panel()]);
        Assert.Same(content, Assert.Single(button.Children));
        Assert.Equal(2, button.BackgroundChildren.Count);
        _ = button[Button.Background()[Array.Empty<Widget>()]];
        Assert.Empty(button.BackgroundChildren);
        Assert.Null(first.Parent);
        Assert.Null(second.Parent);
        Assert.Same(button, content.Parent);
    }

    /// <summary>Tests that a later failed slot does not roll back earlier successful content.</summary>
    [Fact]
    public void LaterSlotFailurePreservesSuccessfulContentAndTheOldBackground()
    {
        Definitions.Panel oldContent = Panel();
        Definitions.Panel oldBackground = Panel();
        Definitions.Panel replacement = Panel();
        Definitions.Panel foreignChild = Panel();
        Definitions.Panel foreignParent = Panel()[foreignChild];
        Definitions.Button button = Button()[oldContent, Button.Background()[oldBackground]];

        Assert.Throws<ArgumentException>(() => button[replacement, Button.Background()[foreignChild]]);
        Assert.Same(replacement, Assert.Single(button.Children));
        Assert.Null(oldContent.Parent);
        Assert.Same(oldBackground, Assert.Single(button.BackgroundChildren));
        Assert.Same(foreignParent, foreignChild.Parent);
    }

    /// <summary>Tests successive slot assignments and per-slot failure guarantees.</summary>
    [Fact]
    public void LastSuccessfulBackgroundAssignmentWins()
    {
        Definitions.Panel first = Panel();
        Definitions.Panel second = Panel();
        Definitions.Panel foreign = Panel();
        Definitions.Panel foreignParent = Panel()[foreign];
        Definitions.Button button = Button()[Button.Background()[first], Button.Background()[second]];
        Assert.Null(first.Parent);
        Assert.Same(second, Assert.Single(button.BackgroundChildren));

        Assert.Throws<ArgumentException>(() => button[
            Button.Background()[first], Button.Background()[foreign]]);
        Assert.Same(first, Assert.Single(button.BackgroundChildren));
        Assert.Null(second.Parent);
        Assert.Same(foreignParent, foreign.Parent);
    }

    /// <summary>Tests deferred slot assignment snapshotting and exclusive ownership on reuse.</summary>
    [Fact]
    public void DeferredSlotCopiesItsInputButCannotShareOwnedWidgets()
    {
        Definitions.Panel child = Panel();
        Widget[] input = [child];
        CompositionSlotAssignment<Definitions.Button> assignment = Button.Background()[input];
        input[0] = Panel();
        Assert.Null(child.Parent);
        Definitions.Button first = Button()[assignment];
        Assert.Same(child, Assert.Single(first.BackgroundChildren));
        Assert.Throws<ArgumentException>(() => Button()[assignment]);

        _ = first[Button.Background()[Array.Empty<Widget>()]];
        Definitions.Button second = Button()[assignment];
        Assert.Same(second, child.Parent);
    }

    /// <summary>Tests that a throwing input accessor cannot partially attach children or poison the mutation guard.</summary>
    [Fact]
    public void InputAccessorFailurePreservesOwnershipAndAllowsRetry()
    {
        Definitions.Panel old = Panel();
        Definitions.Panel replacement = Panel();
        Definitions.Panel root = Panel()[old];
        var input = new CallbackList([replacement], () => throw new InvalidOperationException("input failure"));

        Assert.Throws<InvalidOperationException>(() => root.Children = input);
        Assert.Same(old, Assert.Single(root.Children));
        Assert.Null(replacement.Parent);
        root.Children = [replacement];
        Assert.Same(root, replacement.Parent);
    }

    /// <summary>Tests reentrant modifications and disposal invoked by user-provided collection accessors.</summary>
    [Fact]
    public void ReentrantCollectionAccessCannotMutateOrDisposeTheSameTree()
    {
        Definitions.Panel old = Panel();
        Definitions.Panel root = Panel()[old];
        using var surface = new UiSurface(root);
        var input = new CallbackList([Panel()], () => root.Children = []);
        Assert.Throws<InvalidOperationException>(() => root.Children = input);

        input = new CallbackList([Panel()], surface.Dispose);
        Assert.Throws<InvalidOperationException>(() => root.Children = input);
        Assert.Same(root, surface.Root);
        Assert.Same(root, old.Parent);
        root.Children = [];
        Assert.Null(old.Parent);
    }

    /// <summary>Tests that a detached tree cannot be connected or adopted halfway through a mutation.</summary>
    [Fact]
    public void MutatingDetachedTreeCannotBeAdoptedOrConnectedByAnInputAccessor()
    {
        Definitions.Panel old = Panel();
        Definitions.Panel source = Panel()[old];
        Definitions.Panel destination = Panel();
        var input = new CallbackList([Panel()], () => destination.Children = [source]);
        Assert.Throws<InvalidOperationException>(() => source.Children = input);
        Assert.Empty(destination.Children);
        Assert.Null(source.Parent);
        Assert.Same(old, Assert.Single(source.Children));

        input = new CallbackList([Panel()], () => _ = new UiSurface(source));
        Assert.Throws<InvalidOperationException>(() => source.Children = input);
        using var surface = new UiSurface(source);
        Assert.Same(source, surface.Root);
    }

    private sealed class CustomPanel : Definitions.Panel
    {
    }

    private sealed class EqualPanel : Definitions.Panel
    {
        public override bool Equals(object? obj) => obj is EqualPanel;

        public override int GetHashCode() => 0;
    }

    private sealed class CallbackList(Widget[] children, Action beforeRead) : IReadOnlyList<Widget>
    {
        public int Count => children.Length;

        public Widget this[int index]
        {
            get
            {
                beforeRead();
                return children[index];
            }
        }

        public IEnumerator<Widget> GetEnumerator() => ((IEnumerable<Widget>)children).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
