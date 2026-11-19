using Lumyte.UI.Collections;
using Xunit;
using static Lumyte.UI.Collections.Compose;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Automation.Tests;

/// <summary>Tree selection uses real pointer probes and component-owned selection bodies.</summary>
public sealed class TreeSelectionTests
{
    /// <summary>Selection never expands a deep row whose glyph overlaps the row center.</summary>
    /// <param name="slot">Uses the receiving-owner item slot.</param>
    /// <returns>The asynchronous assertion.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeepRowSelectOptionExcludesGlyphAsync(bool slot)
    {
        Collections.Compose.Definitions.TreeView<int> declaration = TreeView<int>(roots: new[] { 0 }, keySelector: x => x.ToString(System.Globalization.CultureInfo.InvariantCulture), childrenSelector: x => new[] { x + 1 }, hasChildren: x => x < 4, expandedKeys: new[] { "0", "1", "2" }, itemTemplate: _ => SizedBox(height: 20), testId: "tree", height: 80);
        if (slot)
        {
            declaration = declaration[TreeViewFactory<int>().Item()[
                (_, _) => SizedBox(height: 20)]];
        }

        await using var fixture = new Fixture(declaration);
        await fixture.Page.GetByTestId("tree").SelectOptionAsync("3");
        TreeViewElement<int> tree = Assert.IsType<TreeViewElement<int>>(fixture.Surface.RootElement);
        Assert.Equal("3", tree.SelectedKey);
        Assert.DoesNotContain("3", tree.ExpandedKeys);
        UiAutomationNode row = (await fixture.Page.SnapshotAsync()).Nodes.Single(n => n.OptionKey == "3");
        Assert.Equal(tree.AutomationId, row.SelectionGroup);
        Assert.True(row.ActionableBounds.X >= 64);
        Assert.DoesNotContain(fixture.Host.Sent, input => input is UiCheckInput);
        await fixture.Page.GetByTestId("tree").GetByItemKey("3").ExpandAsync();
        fixture.Surface.UpdateLayout();
        Assert.Contains("3", tree.ExpandedKeys);
        Assert.Equal("3", tree.SelectedKey);
    }

    /// <summary>Nonvirtual offscreen selection gets current geometry after one reveal update.</summary>
    /// <returns>The asynchronous assertion.</returns>
    [Fact]
    public async Task NonvirtualSelectOptionRevealsActionableRowAsync()
    {
        await using var fixture = new Fixture(TreeView<string>(roots: new[] { "root" }, keySelector: x => x, childrenSelector: _ => Enumerable.Range(0, 100).Select(i => "leaf" + i).ToArray(), hasChildren: x => x == "root", keyPathSelector: key => new[] { "root", key }, itemTemplate: _ => SizedBox(height: 20), testId: "tree", height: 60));
        await fixture.Page.GetByTestId("tree").SelectOptionAsync("leaf99");
        TreeViewElement<string> tree = Assert.IsType<TreeViewElement<string>>(fixture.Surface.RootElement);
        Assert.Equal("leaf99", tree.SelectedKey);
        UiAutomationNode row = (await fixture.Page.SnapshotAsync()).Nodes.Single(n => n.OptionKey == "leaf99");
        Assert.True(row.ActionableBounds.Height > 0);
        Assert.InRange(row.ActionableBounds.Y, 0, 79);
    }

    /// <summary>A glyph-only row exposes no false selectable click capability.</summary>
    /// <returns>The asynchronous assertion.</returns>
    [Fact]
    public async Task GlyphOnlyRowCannotSelectAsync()
    {
        await using var fixture = new Fixture(TreeView<int>(roots: new[] { 0 }, keySelector: x => x.ToString(System.Globalization.CultureInfo.InvariantCulture), childrenSelector: x => new[] { x + 1 }, hasChildren: x => x < 7, expandedKeys: new[] { "0", "1", "2", "3", "4", "5" }, itemTemplate: _ => SizedBox(height: 10), testId: "tree", height: 80));
        UiAutomationNode row = (await fixture.Page.SnapshotAsync()).Nodes.Single(n => n.OptionKey == "6");
        Assert.Equal(0, row.ActionableBounds.Width);
        Assert.Equal(UiAutomationActions.None, row.Actions & UiAutomationActions.Click);
        await Assert.ThrowsAsync<UiAutomationException>(() => fixture.Page.GetByTestId("tree").SelectOptionAsync("6"));
        Assert.Empty(fixture.Host.Sent);
    }

    /// <summary>Real hit probes reject a sibling covering the tree selection body.</summary>
    /// <returns>The asynchronous assertion.</returns>
    [Fact]
    public async Task OccludedTreeSelectionSendsNoInputAsync()
    {
        await using var fixture = new Fixture(Panel()[
            TreeView<string>(roots: new[] { "root" }, keySelector: x => x, childrenSelector: _ => Array.Empty<string>(), hasChildren: _ => false, itemTemplate: _ => SizedBox(height: 20), testId: "tree", height: 60),
            Button(width: 100, height: 80)]);
        await Assert.ThrowsAsync<UiAutomationException>(() => fixture.Page.GetByTestId("tree").SelectOptionAsync("root", new() { Timeout = TimeSpan.FromMilliseconds(40) }));
        Assert.Empty(fixture.Host.Sent);
    }
}
