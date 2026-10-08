using Lumyte.Composition.Sample;
using Xunit;
using static Lumyte.Composition.Sample.Compose;

namespace Lumyte.Composition.Tests;

/// <summary>Tests generated factories and attached settings across an assembly boundary.</summary>
public sealed class CompositionTests
{
    /// <summary>Tests the requested expression using only generated factories and extensions.</summary>
    [Fact]
    public void RequestedExpressionAttachesColumnToText()
    {
        Compose.Definitions.Grid grid = Grid()[Text(with: [Grid.Column(1)])];
        Compose.Definitions.Widget child = Assert.Single(grid.Children);
        Assert.IsType<Compose.Definitions.Text>(child);
        Assert.Equal(1, (int)child.AttachedValues["Grid.Column"]!);
    }

    /// <summary>Tests that declared methods update widget-owned storage only when applied.</summary>
    [Fact]
    public void ActionsAreDeferredAndUseWidgetOwnedStorage()
    {
        Action<Compose.Definitions.Widget> action = Grid.Column(3);
        Compose.Definitions.Text first = Text();
        Compose.Definitions.Text second = Text();
        Assert.Empty(first.AttachedValues);
        action(first);
        Assert.Equal(3, first.AttachedValues["Grid.Column"]);
        Assert.Empty(second.AttachedValues);
    }

    /// <summary>Tests that validation belongs to the user method and executes during application.</summary>
    [Fact]
    public void InvalidColumnDoesNotMutateTargets()
    {
        Action<Compose.Definitions.Widget> invalid = Grid.Column(-1);
        Compose.Definitions.Text text = Text(with: [Grid.Column(2)]);
        Assert.Throws<ArgumentOutOfRangeException>(() => invalid(text));
        Assert.Equal(2, text.AttachedValues["Grid.Column"]);
        Assert.Throws<ArgumentOutOfRangeException>(() => Text(with: [invalid]));
    }

    /// <summary>Tests style operations, multiple captured arguments and parameterless operations.</summary>
    [Fact]
    public void ActionsCanRewriteStylesWithoutAttachedStorageInComposition()
    {
        Compose.Definitions.Text text = Text(with: [Text.Fade(0.5f), Text.Tag("color", "red")]);
        Assert.Equal(0.5f, text.Opacity);
        Assert.Equal("red", text.AttachedValues["color"]);
        Text.Reset()(text);
        Assert.Equal(1, text.Opacity);
        Text.Tag("color", null)(text);
        Assert.Null(text.AttachedValues["color"]);
        Assert.DoesNotContain(typeof(ComposableAttribute).Assembly.GetTypes(), type => type.Name.StartsWith("AttachedProperty", StringComparison.Ordinal));
    }

    /// <summary>Tests generic factories across an assembly boundary, including caching and typed operations.</summary>
    [Fact]
    public void GenericFactoryCreatesTypedLists()
    {
        Compose.Definitions.ListView<int> list = ListView<int>(selected: 1, with: [ListViewFactory<int>().Select(2)])[1, 2, 3];
        Assert.Equal(new[] { 1, 2, 3 }, list.Items);
        Assert.Equal(2, list.Selected);
        Assert.Same(ListViewFactory<int>(), ListViewFactory<int>());
        Assert.NotSame(ListView<int>(), ListView<int>());
        Assert.Equal("hello", ListView<string>(selected: "hello").Selected);
        Assert.Same(list, list[4]);
    }

    /// <summary>Tests the requested named slot expression and mixed ordinary content.</summary>
    [Fact]
    public void NamedSlotAcceptsImagesAlongsideOrdinaryChildren()
    {
        Compose.Definitions.Image image = Image("sample.jpeg");
        Compose.Definitions.Button button = Button()[Button.Background()[image]];
        Assert.Same(image, Assert.Single(button.BackgroundChildren));
        Assert.Empty(button.Children);
        Compose.Definitions.Text text = Text();
        Assert.Same(button, button[text, Button.Background()[Image("other.jpeg")]]);
        Assert.Same(text, Assert.Single(button.Children));
        Assert.Equal("other.jpeg", Assert.IsType<Compose.Definitions.Image>(Assert.Single(button.BackgroundChildren)).Source);
    }

    /// <summary>Tests slot deferral, reuse, snapshotting, and preservation of ordinary content.</summary>
    [Fact]
    public void SlotAssignmentsAreDeferredAndReusable()
    {
        Compose.Definitions.Image image = Image("first.jpeg");
        Compose.Definitions.Widget[] children = [image];
        CompositionSlotAssignment<Compose.Definitions.Button> slot = Button.Background()[children];
        children[0] = Image("changed.jpeg");
        Compose.Definitions.Button first = Button()[Text()];
        Assert.Empty(first.BackgroundChildren);
        Assert.Same(first, first[slot]);
        Assert.Same(image, Assert.Single(first.BackgroundChildren));
        Assert.Single(first.Children);
        Compose.Definitions.Button second = Button()[slot];
        Assert.Same(image, Assert.Single(second.BackgroundChildren));
        Assert.NotSame(first.BackgroundChildren, second.BackgroundChildren);
        Assert.Same(first, first[Button.Background()[Image("next.jpeg")], Button.Background()[Array.Empty<Compose.Definitions.Widget>()]]);
        Assert.Empty(first.BackgroundChildren);
    }

    /// <summary>Tests null and uninitialized assignment rejection at the runtime boundary.</summary>
    [Fact]
    public void InvalidSlotArgumentsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new CompositionSlot<Compose.Definitions.Button, Compose.Definitions.Widget>(null!));
        Assert.Throws<ArgumentNullException>(() => Button.Background()[null!]);
        CompositionSlotAssignment<Compose.Definitions.Button> empty = default;
        Assert.Throws<InvalidOperationException>(() => empty.Apply(Button()));
        Assert.Throws<ArgumentNullException>(() => empty.Apply(null!));
    }

    /// <summary>Tests slot ordering and stopping after a callback failure without rollback.</summary>
    [Fact]
    public void SlotFailureStopsLaterAssignments()
    {
        var order = new List<int>();
        var expected = new InvalidOperationException("slot");
        var first = new CompositionSlotAssignment<Compose.Definitions.Button>(target => order.Add(1));
        var failure = new CompositionSlotAssignment<Compose.Definitions.Button>(target => throw expected);
        var last = new CompositionSlotAssignment<Compose.Definitions.Button>(target => order.Add(3));
        Compose.Definitions.Button button = Button();
        Compose.Definitions.Text text = Text();
        InvalidOperationException actual = Assert.Throws<InvalidOperationException>(() => button[text, first, failure, last]);
        Assert.Same(expected, actual);
        Assert.Equal(new[] { 1 }, order);
        Assert.Same(text, Assert.Single(button.Children));
    }

    /// <summary>Tests that a user operation mutating its array cannot alter later applications.</summary>
    [Fact]
    public void EachSlotApplicationReceivesItsOwnChildArray()
    {
        var observed = new List<int>();
        var slot = new CompositionSlot<Compose.Definitions.Button, int>((target, children) =>
        {
            observed.Add(children[0]);
            children[0] = 99;
        });
        CompositionSlotAssignment<Compose.Definitions.Button> assignment = slot[1];
        assignment.Apply(Button());
        assignment.Apply(Button());
        Assert.Equal(new[] { 1, 1 }, observed);
    }

    /// <summary>Tests defaults, explicit false and zero, required arguments and init assignments.</summary>
    [Fact]
    public void FactoriesPreserveOmittedDefaultsAndApplyExplicitValues()
    {
        Assert.Equal("untitled", Text().Content);
        Assert.Null(Text(content: (string?)null).Content);
        TestKit.Definitions.Item omitted = TestKit.Item("required");
        Assert.True(omitted.Enabled);
        Assert.Equal(5, omitted.Count);
        TestKit.Definitions.Item configured = TestKit.Item("required", count: 0, enabled: false);
        Assert.Equal("required", configured.Content);
        Assert.False(configured.Enabled);
        Assert.Equal(0, configured.Count);
    }

    /// <summary>Tests delegate caching, fresh instances and indexer replacement.</summary>
    [Fact]
    public void FactoryIsCachedAndIndexerReturnsTheSameInstance()
    {
        Assert.Same(Compose.Grid, Compose.Grid);
        Assert.NotSame(Grid(), Grid());
        Compose.Definitions.Grid grid = Grid()[Text()];
        Compose.Definitions.Text replacement = Text();
        Assert.Same(grid, grid[replacement]);
        Assert.Same(replacement, Assert.Single(grid.Children));
    }

    /// <summary>Tests action ordering and stopping after a callback failure.</summary>
    [Fact]
    public void WithActionsRunInOrderAndStopOnFailure()
    {
        var order = new List<int>();
        Compose.Definitions.Text text = Text(with: [node => order.Add(1), Grid.Column(1), node => order.Add(2), Grid.Column(4)]);
        Assert.Equal(new[] { 1, 2 }, order);
        Assert.Equal(4, (int)text.AttachedValues["Grid.Column"]!);
        order.Clear();
        var expected = new InvalidOperationException("callback");
        InvalidOperationException actual = Assert.Throws<InvalidOperationException>(() => Text(with:
            [node => order.Add(1), node => throw expected, node => order.Add(2)]));
        Assert.Same(expected, actual);
        Assert.Equal(new[] { 1 }, order);
    }

    /// <summary>Tests null action rejection before applying any action.</summary>
    [Fact]
    public void NullActionIsRejectedBeforeAnyActionRuns()
    {
        bool ran = false;
        Assert.Throws<ArgumentException>(() => Text(with: [node => ran = true, null!]));
        Assert.False(ran);
        Assert.Equal("untitled", Text(with: null).Content);
        Assert.Equal("untitled", Text(with: []).Content);
    }

    /// <summary>Tests omitted values and explicit null at the runtime contract boundary.</summary>
    [Fact]
    public void OptionalDistinguishesExplicitNullFromOmission()
    {
        Optional<string?> omitted = default;
        var supplied = new Optional<string?>(null);
        Assert.False(omitted.HasValue);
        Assert.Throws<InvalidOperationException>(() => omitted.Value);
        Assert.True(supplied.HasValue);
        Assert.Null(supplied.Value);
    }
}
