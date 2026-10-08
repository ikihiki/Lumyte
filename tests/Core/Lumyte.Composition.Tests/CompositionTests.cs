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
