using Xunit;
using static Lumyte.UI.Collections.Compose;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Collections.Tests;

/// <summary>Deterministic lazy hierarchy adoption contracts.</summary>
public sealed class TreeViewTests
{
    /// <summary>Collapsed children remain unqueried; accepted expansion follows layout.</summary>
    [Fact]
    public void ChildrenAreLazyAndExpansionIsAcceptedAtomically()
    {
        int children = 0;
        int notifications = 0;
        using var surface = new UiSurface(TreeView<string>(
            roots: new[] { "root" },
            keySelector: x => x,
            childrenSelector: _ =>
            {
                children++;
                return new[] { "child" };
            },
            hasChildren: x => x == "root",
            itemTemplate: _ => SizedBox(height: 20),
            onExpandedChanged: _ => notifications++,
            virtualization: new UiItemVirtualization(20, 0)));
        surface.SetViewport(new(100, 60));
        surface.UpdateLayout();
        TreeViewElement<string> tree = Assert.IsType<TreeViewElement<string>>(surface.RootElement);
        Assert.Equal(0, children);
        Assert.True(tree.SetExpanded("root", true));
        Assert.Empty(tree.ExpandedKeys);
        surface.UpdateLayout();
        Assert.Equal(new[] { "root", "child" }, tree.VisibleKeys);
        Assert.Equal(1, children);
        Assert.Equal(1, notifications);
        Assert.True(tree.SetExpanded("root", false));
        surface.UpdateLayout();
        Assert.Equal(new[] { "root" }, tree.VisibleKeys);
        Assert.True(tree.SetExpanded("root", true));
        surface.UpdateLayout();
        Assert.Equal(1, children);
        Assert.Equal(3, notifications);
    }

    /// <summary>Duplicate discovery rejects before changing accepted hierarchy or notification.</summary>
    [Fact]
    public void DuplicateDiscoveryPreservesOldModel()
    {
        int notifications = 0;
        using var surface = new UiSurface(TreeView<string>(
            roots: new[] { "root" },
            keySelector: x => x,
            childrenSelector: _ => new[] { "root" },
            hasChildren: _ => true,
            itemTemplate: _ => SizedBox(height: 20),
            onExpandedChanged: _ => notifications++,
            virtualization: new UiItemVirtualization(20, 0)));
        surface.SetViewport(new(100, 60));
        surface.UpdateLayout();
        TreeViewElement<string> tree = Assert.IsType<TreeViewElement<string>>(surface.RootElement);
        UiAutomationId old = surface.RootElement.Children[0].Children[0].Children[0].Children[0].AutomationId;
        Assert.True(tree.SetExpanded("root", true));
        Assert.Throws<ArgumentException>(surface.UpdateLayout);
        Assert.Equal(new[] { "root" }, tree.VisibleKeys);
        Assert.Empty(tree.ExpandedKeys);
        Assert.Equal(0, notifications);
        Assert.Equal(old, surface.RootElement.Children[0].Children[0].Children[0].Children[0].AutomationId);
    }

    /// <summary>Large root data uses bounded template realization and no collapsed discovery.</summary>
    [Fact]
    public void ThousandRootsHaveBoundedRealization()
    {
        int templates = 0;
        using var surface = new UiSurface(TreeView<int>(
            roots: Enumerable.Range(0, 1000).ToArray(),
            keySelector: x => x.ToString(System.Globalization.CultureInfo.InvariantCulture),
            childrenSelector: _ => throw new InvalidOperationException("Collapsed children were read."),
            hasChildren: _ => true,
            itemTemplate: _ =>
            {
                templates++;
                return SizedBox(height: 20);
            },
            virtualization: new UiItemVirtualization(20, 1),
            height: 60));
        surface.SetViewport(new(100, 60));
        surface.UpdateLayout();
        Assert.Equal(4, templates);
        TreeViewElement<int> tree = Assert.IsType<TreeViewElement<int>>(surface.RootElement);
        Assert.Equal(1000, tree.VisibleKeys.Count);
        Assert.True(tree.BringIntoView("999"));
        surface.UpdateLayout();
        Assert.InRange(templates, 7, 9);
    }

    /// <summary>Explicit paths discover only ancestors and reveal the target in the same adoption.</summary>
    [Fact]
    public void ExplicitPathRevealIsAtomicAndRestoresAncestorFocus()
    {
        int reads = 0;
        using var surface = new UiSurface(TreeView<string>(
            roots: new[] { "root" },
            keySelector: x => x,
            childrenSelector: key =>
            {
                reads++;
                return key == "root" ? new[] { "branch" } : Enumerable.Range(0, 1000).Select(value => "leaf" + value).ToArray();
            },
            hasChildren: key => key is "root" or "branch",
            itemTemplate: _ => SizedBox(height: 20),
            virtualization: new UiItemVirtualization(20, 0),
            height: 60));
        surface.SetViewport(new(120, 60));
        surface.UpdateLayout();
        TreeViewElement<string> tree = Assert.IsType<TreeViewElement<string>>(surface.RootElement);
        Assert.True(tree.BringPathIntoView(new[] { "root", "branch", "leaf999" }));
        Assert.Equal(0, reads);
        Assert.Empty(tree.ExpandedKeys);
        surface.UpdateLayout();
        Assert.Equal(2, reads);
        Assert.Equal(1002, tree.VisibleKeys.Count);
        UiAutomationNode leaf = Assert.Single(surface.CaptureAutomationSnapshot().Nodes, node => node.OptionKey == "leaf999");
        Assert.Equal(tree.AutomationId, leaf.SelectionGroup);
        Assert.Equal(new UiTreeItemState(2, "branch"), leaf.TreeItem);
        Assert.True(leaf.ClippedBounds.Height > 0);
    }

    /// <summary>Invalid path discovery leaves expansion and connected identifiers unchanged.</summary>
    [Fact]
    public void InvalidPathRollsBackHierarchy()
    {
        using var surface = new UiSurface(TreeView<string>(
            roots: new[] { "root" },
            keySelector: x => x,
            childrenSelector: _ => new[] { "child" },
            hasChildren: key => key == "root",
            itemTemplate: _ => SizedBox(height: 20),
            virtualization: new UiItemVirtualization(20, 0)));
        surface.SetViewport(new(120, 60));
        surface.UpdateLayout();
        TreeViewElement<string> tree = Assert.IsType<TreeViewElement<string>>(surface.RootElement);
        UiAutomationId before = surface.CaptureAutomationSnapshot().Nodes.Single(node => node.OptionKey == "root").Id;
        Assert.True(tree.BringPathIntoView(new[] { "root", "missing" }));
        Assert.Throws<ArgumentException>(surface.UpdateLayout);
        Assert.Empty(tree.ExpandedKeys);
        Assert.Equal(new[] { "root" }, tree.VisibleKeys);
        Assert.Equal(before, surface.CaptureAutomationSnapshot().Nodes.Single(node => node.OptionKey == "root").Id);
    }

    /// <summary>Collapsing the focused descendant retains selection and restores its visible ancestor.</summary>
    [Fact]
    public void CollapseRestoresAncestorWithoutClearingSelection()
    {
        using var surface = new UiSurface(TreeView<string>(
            roots: new[] { "root" },
            keySelector: key => key,
            childrenSelector: _ => new[] { "child" },
            hasChildren: key => key == "root",
            expandedKeys: new[] { "root" },
            selectedKey: "child",
            itemTemplate: _ => SizedBox(height: 20),
            virtualization: new UiItemVirtualization(20, 0)));
        surface.SetViewport(new(100, 60));
        surface.UpdateLayout();
        TreeViewElement<string> tree = Assert.IsType<TreeViewElement<string>>(surface.RootElement);
        UiAutomationId child = surface.CaptureAutomationSnapshot().Nodes.Single(node => node.OptionKey == "child").Id;
        Assert.True(surface.TryFocus(surface.FindElement(child)!));
        tree.SetExpanded("root", false);
        surface.UpdateLayout();
        Assert.Equal("root", surface.FocusedElement?.Widget.Key);
        Assert.Equal("child", tree.SelectedKey);
        Assert.Null(surface.FindElement(child));
    }

    /// <summary>User callback exceptions occur after both hierarchy and child model adoption.</summary>
    [Fact]
    public void ExpansionCallbackExceptionDoesNotRollBack()
    {
        using var surface = new UiSurface(TreeView<string>(
            roots: new[] { "root" },
            keySelector: key => key,
            childrenSelector: _ => new[] { "child" },
            hasChildren: key => key == "root",
            itemTemplate: _ => SizedBox(height: 20),
            onExpandedChanged: _ => throw new InvalidOperationException("user callback"),
            virtualization: new UiItemVirtualization(20, 0)));
        surface.SetViewport(new(100, 60));
        surface.UpdateLayout();
        TreeViewElement<string> tree = Assert.IsType<TreeViewElement<string>>(surface.RootElement);
        tree.SetExpanded("root", true);
        Assert.Throws<InvalidOperationException>(surface.UpdateLayout);
        Assert.Equal(new[] { "root", "child" }, tree.VisibleKeys);
        Assert.Equal(new[] { "root" }, tree.ExpandedKeys);
        Assert.Contains(surface.CaptureAutomationSnapshot().Nodes, node => node.OptionKey == "child");
    }

    /// <summary>Enqueue-time positions reject old requests even when the newer scroll offset is unchanged.</summary>
    /// <returns>The asynchronous assertion.</returns>
    [Fact]
    public async Task QueuedPathCannotOverrideNewerSameOffsetScrollAsync()
    {
        using var surface = new UiSurface(TreeView<string>(
            roots: new[] { "root" },
            keySelector: key => key,
            childrenSelector: _ => new[] { "child" },
            hasChildren: key => key == "root",
            itemTemplate: _ => SizedBox(height: 20),
            virtualization: new UiItemVirtualization(20, 0)));
        surface.SetViewport(new(100, 60));
        surface.UpdateLayout();
        TreeViewElement<string> tree = Assert.IsType<TreeViewElement<string>>(surface.RootElement);
        var dispatcher = new DeferredDispatcher();
        Task pending = tree.BringPathIntoViewAsync(dispatcher, new[] { "root", "child" }).AsTask();
        ScrollController controller = Assert.IsType<ScrollViewElement>(tree.Children[0].Children[0]).Controller;
        controller.ScrollTo(0);
        dispatcher.Drain();
        await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
        Assert.Empty(tree.ExpandedKeys);
    }

    /// <summary>Collapse retains accepted hierarchy until a borrowed input lifetime finishes.</summary>
    [Fact]
    public void CollapseWaitsForPendingInputLifetime()
    {
        bool pending = true;
        using var surface = new UiSurface(TreeView<string>(
            roots: new[] { "root" },
            keySelector: key => key,
            childrenSelector: _ => new[] { "child" },
            hasChildren: key => key == "root",
            expandedKeys: new[] { "root" },
            itemTemplate: key => key == "child" ? new PendingInput(() => pending) : SizedBox(height: 20),
            virtualization: new UiItemVirtualization(20, 0)));
        surface.SetViewport(new(100, 60));
        surface.UpdateLayout();
        TreeViewElement<string> tree = Assert.IsType<TreeViewElement<string>>(surface.RootElement);
        tree.SetExpanded("root", false);
        surface.UpdateLayout();
        Assert.Equal(new[] { "root", "child" }, tree.VisibleKeys);
        Assert.Equal(new[] { "root" }, tree.ExpandedKeys);
        pending = false;
        surface.UpdateLayout();
        Assert.Equal(new[] { "root" }, tree.VisibleKeys);
        Assert.Empty(tree.ExpandedKeys);
    }

    /// <summary>A sibling list factory failure cannot publish only the tree expansion.</summary>
    [Fact]
    public void TreeAndListProposalsAreAtomicTogether()
    {
        bool fail = false;
        using var surface = new UiSurface(Stack()[
            TreeView<string>(roots: new[] { "root" }, keySelector: key => key, childrenSelector: _ => new[] { "child" }, hasChildren: key => key == "root", itemTemplate: _ => SizedBox(height: 20), height: 60, virtualization: new UiItemVirtualization(20, 0)),
            ListBox<int>(items: Enumerable.Range(0, 100).ToArray(), keySelector: item => item.ToString(System.Globalization.CultureInfo.InvariantCulture), itemTemplate: _ => fail ? throw new InvalidOperationException("sibling factory") : SizedBox(height: 20), height: 60, virtualization: new UiItemVirtualization(20, 0))]);
        surface.SetViewport(new(100, 120));
        surface.UpdateLayout();
        TreeViewElement<string> tree = Assert.IsType<TreeViewElement<string>>(surface.RootElement.Children[0]);
        ScrollController controller = Assert.IsType<ScrollViewElement>(surface.RootElement.Children[1].Children[0]).Controller;
        tree.SetExpanded("root", true);
        controller.ScrollTo(1000);
        fail = true;
        Assert.Throws<InvalidOperationException>(surface.UpdateLayout);
        Assert.Empty(tree.ExpandedKeys);
        Assert.Equal(new[] { "root" }, tree.VisibleKeys);
        Assert.Equal(0, controller.Offset);
        fail = false;
        surface.UpdateLayout();
        Assert.Equal(new[] { "root", "child" }, tree.VisibleKeys);
        Assert.Equal(1000, controller.Offset);
    }

    /// <summary>Failed target templates cannot publish expansion, scroll geometry or callbacks.</summary>
    [Fact]
    public void FailedPathFactoryPreservesAcceptedHierarchyAndPosition()
    {
        bool fail = false;
        int changes = 0;
        using var surface = new UiSurface(TreeView<string>(
            roots: new[] { "root" },
            keySelector: key => key,
            childrenSelector: _ => Enumerable.Range(0, 100).Select(index => "child" + index).ToArray(),
            hasChildren: key => key == "root",
            itemTemplate: key => fail && key == "child99" ? throw new InvalidOperationException("factory") : SizedBox(height: 20),
            onExpandedChanged: _ => changes++,
            virtualization: new UiItemVirtualization(20, 0),
            height: 60));
        surface.SetViewport(new(100, 60));
        surface.UpdateLayout();
        TreeViewElement<string> tree = Assert.IsType<TreeViewElement<string>>(surface.RootElement);
        ScrollController controller = Assert.IsType<ScrollViewElement>(tree.Children[0].Children[0]).Controller;
        int positions = 0;
        controller.OnChanged = _ => positions++;
        fail = true;
        tree.BringPathIntoView(new[] { "root", "child99" });
        Assert.Throws<InvalidOperationException>(surface.UpdateLayout);
        Assert.Empty(tree.ExpandedKeys);
        Assert.Equal(0, controller.Offset);
        Assert.Equal(0, positions);
        Assert.Equal(0, changes);
        fail = false;
        surface.UpdateLayout();
        Assert.Equal(new[] { "root" }, tree.ExpandedKeys);
        Assert.Equal(1960, controller.Offset);
        Assert.Equal(1, changes);
        Assert.Equal(1, positions);
    }

    /// <summary>Right and Left implement hierarchy navigation independently from selection.</summary>
    /// <param name="virtualized">Whether to use fixed row virtualization.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KeyboardHierarchyDoesNotImplicitlySelect(bool virtualized)
    {
        using var surface = new UiSurface(TreeView<string>(
            roots: new[] { "root" },
            keySelector: key => key,
            childrenSelector: _ => new[] { "child" },
            hasChildren: key => key == "root",
            itemTemplate: _ => SizedBox(height: 20),
            virtualization: virtualized ? new UiItemVirtualization(20, 0) : null));
        surface.SetViewport(new(100, 60));
        surface.UpdateLayout();
        TreeViewElement<string> tree = Assert.IsType<TreeViewElement<string>>(surface.RootElement);
        UiAutomationId root = surface.CaptureAutomationSnapshot().Nodes.Single(node => node.OptionKey == "root").Id;
        surface.TryFocus(surface.FindElement(root));
        Press(Lumyte.Input.Abstractions.Key.ArrowRight);
        Assert.Equal(new[] { "root" }, tree.ExpandedKeys);
        Press(Lumyte.Input.Abstractions.Key.ArrowRight);
        Assert.Equal("child", surface.FocusedElement?.Widget.Key);
        Assert.Null(tree.SelectedKey);
        Press(Lumyte.Input.Abstractions.Key.Space);
        Assert.Equal("child", tree.SelectedKey);
        Press(Lumyte.Input.Abstractions.Key.ArrowLeft);
        Assert.Equal("root", surface.FocusedElement?.Widget.Key);
        Press(Lumyte.Input.Abstractions.Key.ArrowLeft);
        Assert.Empty(tree.ExpandedKeys);
        Assert.Equal("child", tree.SelectedKey);

        void Press(Lumyte.Input.Abstractions.Key key)
        {
            surface.Dispatch(new UiKeyInput(key, true));
            surface.Dispatch(new UiKeyInput(key, false));
            surface.UpdateLayout();
        }
    }

    /// <summary>Duplicate keys in separately discovered siblings reject before accepting either expansion.</summary>
    [Fact]
    public void DuplicateKeysAreGlobalAcrossKnownBranches()
    {
        using var surface = new UiSurface(TreeView<string>(
            roots: new[] { "a", "b" },
            keySelector: key => key,
            childrenSelector: _ => new[] { "duplicate" },
            hasChildren: key => key is "a" or "b",
            itemTemplate: _ => SizedBox(height: 20),
            virtualization: new UiItemVirtualization(20, 0)));
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        TreeViewElement<string> tree = Assert.IsType<TreeViewElement<string>>(surface.RootElement);
        tree.SetExpanded("a", true);
        surface.UpdateLayout();
        tree.SetExpanded("b", true);
        Assert.Throws<ArgumentException>(surface.UpdateLayout);
        Assert.Equal(new[] { "a" }, tree.ExpandedKeys);
        Assert.Equal(new[] { "a", "duplicate", "b" }, tree.VisibleKeys);
    }

    /// <summary>Sharing a passive declaration never shares child caches or accepted expansion.</summary>
    [Fact]
    public void SharedDeclarationHasIndependentHierarchyState()
    {
        int reads = 0;
        Compose.Definitions.TreeView<string> declaration = TreeView<string>(
            roots: new[] { "root" },
            keySelector: key => key,
            childrenSelector: _ =>
            {
                reads++;
                return new[] { "child" };
            },
            hasChildren: key => key == "root",
            itemTemplate: _ => SizedBox(height: 20),
            virtualization: new UiItemVirtualization(20, 0));
        using var first = new UiSurface(declaration);
        using var second = new UiSurface(declaration);
        first.SetViewport(new(100, 60));
        second.SetViewport(new(100, 60));
        first.UpdateLayout();
        second.UpdateLayout();
        TreeViewElement<string> a = Assert.IsType<TreeViewElement<string>>(first.RootElement);
        TreeViewElement<string> b = Assert.IsType<TreeViewElement<string>>(second.RootElement);
        a.SetExpanded("root", true);
        first.UpdateLayout();
        Assert.Equal(1, reads);
        Assert.Empty(b.ExpandedKeys);
        Assert.Equal(new[] { "root" }, b.VisibleKeys);
        Assert.Empty(declaration.ExpandedKeys);
    }

    /// <summary>Queued paths from the prior root model cannot discover into a replacement.</summary>
    /// <returns>The asynchronous assertion.</returns>
    [Fact]
    public async Task ReplacedModelRejectsQueuedPathAsync()
    {
        Compose.Definitions.TreeView<string> Create() => TreeView<string>(roots: new[] { "root" }, keySelector: key => key, childrenSelector: _ => new[] { "child" }, hasChildren: key => key == "root", itemTemplate: _ => SizedBox(height: 20), virtualization: new UiItemVirtualization(20, 0));
        using var surface = new UiSurface(Create());
        surface.SetViewport(new(100, 60));
        surface.UpdateLayout();
        TreeViewElement<string> tree = Assert.IsType<TreeViewElement<string>>(surface.RootElement);
        var dispatcher = new DeferredDispatcher();
        Task pending = tree.BringPathIntoViewAsync(dispatcher, new[] { "root", "child" }).AsTask();
        surface.Rebuild(Create());
        surface.UpdateLayout();
        dispatcher.Drain();
        await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
        Assert.Empty(tree.ExpandedKeys);
    }

    /// <summary>Model replacement creates new connected row identities and silently removes absent selection.</summary>
    [Fact]
    public void ResetFreshensRowsAndDropsAbsentSelectionSilently()
    {
        int changes = 0;
        Compose.Definitions.TreeView<string> Create(string[] roots, string? selected) => TreeView<string>(roots: roots, keySelector: key => key, childrenSelector: _ => Array.Empty<string>(), hasChildren: _ => false, itemTemplate: _ => SizedBox(height: 20), selectedKey: selected, onSelectionChanged: _ => changes++, virtualization: new UiItemVirtualization(20, 0));
        using var surface = new UiSurface(Create(new[] { "a", "b" }, "a"));
        surface.SetViewport(new(100, 60));
        surface.UpdateLayout();
        UiAutomationId old = surface.CaptureAutomationSnapshot().Nodes.Single(node => node.OptionKey == "a").Id;
        surface.Rebuild(Create(new[] { "a", "b" }, "a"));
        surface.UpdateLayout();
        UiAutomationId fresh = surface.CaptureAutomationSnapshot().Nodes.Single(node => node.OptionKey == "a").Id;
        Assert.NotEqual(old, fresh);
        surface.Rebuild(Create(new[] { "b" }, "a"));
        surface.UpdateLayout();
        Assert.Null(Assert.IsType<TreeViewElement<string>>(surface.RootElement).SelectedKey);
        Assert.Equal(0, changes);
    }

    /// <summary>The expansion glyph owns one gesture and releasing over the body cannot expand or select.</summary>
    [Fact]
    public void GlyphPressDoesNotDoubleActivateOrLeakIntoBody()
    {
        int expansions = 0;
        int selections = 0;
        using var surface = new UiSurface(TreeView<string>(
            roots: new[] { "root" },
            keySelector: key => key,
            childrenSelector: _ => new[] { "child" },
            hasChildren: key => key == "root",
            itemTemplate: _ => SizedBox(height: 20),
            onExpandedChanged: _ => expansions++,
            onSelectionChanged: _ => selections++,
            virtualization: new UiItemVirtualization(20, 0)));
        surface.SetViewport(new(100, 60));
        surface.UpdateLayout();
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(4, 10)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(40, 10)));
        surface.UpdateLayout();
        Assert.Equal(0, expansions);
        Assert.Equal(0, selections);
        surface.Dispatch(new UiPointerInput(2, UiPointerPhase.Down, new(4, 10)));
        surface.Dispatch(new UiPointerInput(2, UiPointerPhase.Up, new(4, 10)));
        surface.UpdateLayout();
        Assert.Equal(1, expansions);
        Assert.Equal(0, selections);
        surface.Dispatch(new UiPointerInput(3, UiPointerPhase.Down, new(40, 10)));
        surface.Dispatch(new UiPointerInput(3, UiPointerPhase.Up, new(40, 10)));
        surface.UpdateLayout();
        Assert.Equal(1, expansions);
        Assert.Equal(1, selections);
    }

    /// <summary>Selectors cannot change accepted or requested tree state reentrantly.</summary>
    [Fact]
    public void ChildSelectorReentryRejectsWithoutPublication()
    {
        TreeViewElement<string>? tree = null;
        using var surface = new UiSurface(TreeView<string>(
            roots: new[] { "root" },
            keySelector: key => key,
            childrenSelector: _ =>
            {
                tree!.SetExpanded("root", false);
                return new[] { "child" };
            },
            hasChildren: key => key == "root",
            itemTemplate: _ => SizedBox(height: 20),
            virtualization: new UiItemVirtualization(20, 0)));
        surface.SetViewport(new(100, 60));
        surface.UpdateLayout();
        tree = Assert.IsType<TreeViewElement<string>>(surface.RootElement);
        tree.SetExpanded("root", true);
        Assert.Throws<InvalidOperationException>(surface.UpdateLayout);
        Assert.Empty(tree.ExpandedKeys);
        Assert.Equal(new[] { "root" }, tree.VisibleKeys);
    }

    /// <summary>Unpublished declarations reject mutation from templates and sibling preparation without losing a generation.</summary>
    /// <param name="remove">Removes an accepted root instead of adding one.</param>
    /// <param name="sibling">Reenters from a sibling prepare callback.</param>
    /// <param name="reveal">Uses explicit path reveal instead of expansion.</param>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    public void DeclarationPreparationRejectsReentryAndPreservesAcceptedState(bool remove, bool sibling, bool reveal)
    {
        TreeViewElement<string>? tree = null;
        bool attempt = false;
        int reads = 0;
        int notifications = 0;
        void Reenter()
        {
            if (!attempt)
            {
                return;
            }

            attempt = false;
            if (reveal)
            {
                tree!.BringPathIntoView(new[] { "old", "child" });
            }
            else
            {
                tree!.SetExpanded("old", true);
            }
        }

        Compose.Definitions.TreeView<string> Create(string[] roots) => TreeView<string>(
            roots: roots,
            keySelector: key => key,
            childrenSelector: _ =>
            {
                reads++;
                return new[] { "child" };
            },
            hasChildren: key => key == "old",
            itemTemplate: _ =>
            {
                if (!sibling)
                {
                    Reenter();
                }

                return SizedBox(height: 20);
            },
            selectedKey: "old",
            onExpandedChanged: _ => notifications++,
            onSelectionChanged: _ => notifications++,
            virtualization: new UiItemVirtualization(20, 0));
        Lumyte.UI.Compose.Definitions.Widget Build(string[] roots) => Stack()[Create(roots), new PreparingSibling(sibling ? Reenter : () => { })];
        string[] before = remove ? new[] { "old", "removed" } : new[] { "old" };
        string[] after = remove ? new[] { "old" } : new[] { "old", "new" };
        using var surface = new UiSurface(Build(before));
        surface.SetViewport(new(100, 60));
        surface.UpdateLayout();
        tree = Assert.IsType<TreeViewElement<string>>(surface.RootElement.Children[0]);
        static IEnumerable<UiElement> Descendants(UiElement root) => new[] { root }.Concat(root.Children.SelectMany(Descendants));
        UiAutomationId[] identities = Descendants(surface.RootElement).Select(node => node.AutomationId).ToArray();
        UiElement row = surface.FindElement(surface.CaptureAutomationSnapshot().Nodes.Single(node => node.OptionKey == "old").Id)!;
        Assert.True(surface.TryFocus(row));
        ScrollController controller = Assert.IsType<ScrollViewElement>(tree.Children[0].Children[0]).Controller;
        float offset = controller.Offset;
        surface.Rebuild(Build(after));
        attempt = true;
        Assert.Throws<InvalidOperationException>(surface.UpdateLayout);
        Assert.Equal(before, tree.VisibleKeys);
        Assert.Empty(tree.ExpandedKeys);
        Assert.Equal("old", tree.SelectedKey);
        Assert.Equal(identities, Descendants(surface.RootElement).Select(node => node.AutomationId));
        Assert.Same(row, surface.FocusedElement);
        Assert.Equal(offset, controller.Offset);
        Assert.Equal(0, reads);
        Assert.Equal(0, notifications);
        surface.UpdateLayout();
        Assert.Equal(after, tree.VisibleKeys);
        Assert.True(tree.SetExpanded("old", true));
        surface.UpdateLayout();
        Assert.Equal(new[] { "old", "child" }.Concat(after.Skip(1)), tree.VisibleKeys);
        Assert.Equal(1, reads);
        Assert.Equal(1, notifications);
    }

    /// <summary>Async enqueue and delivery cannot mutate the accepted generation during sibling preparation.</summary>
    /// <param name="path">Uses path reveal instead of expansion.</param>
    /// <param name="delivery">Delivers a request queued before preparation.</param>
    /// <returns>The asynchronous assertion.</returns>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task AsyncOperationsRejectUnpublishedPreparationAsync(bool path, bool delivery)
    {
        var dispatcher = new DeferredDispatcher();
        TreeViewElement<string>? tree = null;
        Task? queued = null;
        bool attempt = false;
        ValueTask Request() => path ? tree!.BringPathIntoViewAsync(dispatcher, new[] { "old", "child" }) : tree!.SetExpandedAsync(dispatcher, "old", true);
        void Reenter()
        {
            if (!attempt)
            {
                return;
            }

            attempt = false;
            if (!delivery)
            {
                _ = Request();
            }
            else
            {
                dispatcher.Drain();
            }
        }

        Compose.Definitions.TreeView<string> Create(string[] roots) => TreeView<string>(roots: roots, keySelector: key => key, childrenSelector: _ => new[] { "child" }, hasChildren: key => key == "old", itemTemplate: _ => SizedBox(height: 20), virtualization: new UiItemVirtualization(20, 0));
        using var surface = new UiSurface(Stack()[Create(new[] { "old" }), new PreparingSibling(Reenter)]);
        surface.SetViewport(new(100, 60));
        surface.UpdateLayout();
        tree = Assert.IsType<TreeViewElement<string>>(surface.RootElement.Children[0]);
        if (delivery)
        {
            queued = Request().AsTask();
        }

        surface.Rebuild(Stack()[Create(new[] { "old", "new" }), new PreparingSibling(Reenter)]);
        attempt = true;
        if (delivery)
        {
            surface.UpdateLayout();
            await Assert.ThrowsAsync<InvalidOperationException>(() => queued!);
        }
        else
        {
            Assert.Throws<InvalidOperationException>(surface.UpdateLayout);
            Assert.Equal(new[] { "old" }, tree.VisibleKeys);
            Assert.Empty(tree.ExpandedKeys);
            surface.UpdateLayout();
        }

        Assert.Equal(new[] { "old", "new" }, tree.VisibleKeys);
        Assert.True(tree.SetExpanded("old", true));
        surface.UpdateLayout();
        Assert.Equal(new[] { "old", "child", "new" }, tree.VisibleKeys);
    }

    /// <summary>Adopted expansion notifications can request a subsequent owner-idle update.</summary>
    [Fact]
    public void AdoptedCallbackMayQueueNextExpansion()
    {
        TreeViewElement<string>? tree = null;
        int notifications = 0;
        using var surface = new UiSurface(TreeView<string>(
            roots: new[] { "root" },
            keySelector: key => key,
            childrenSelector: _ => new[] { "child" },
            hasChildren: key => key == "root",
            itemTemplate: _ => SizedBox(height: 20),
            onExpandedChanged: keys =>
            {
                notifications++;
                if (keys.Count != 0)
                {
                    Assert.True(tree!.SetExpanded("root", false));
                }
            },
            virtualization: new UiItemVirtualization(20, 0)));
        surface.SetViewport(new(100, 60));
        surface.UpdateLayout();
        tree = Assert.IsType<TreeViewElement<string>>(surface.RootElement);
        tree.SetExpanded("root", true);
        surface.UpdateLayout();
        Assert.Equal(2, notifications);
        Assert.Empty(tree.ExpandedKeys);
        Assert.Equal(new[] { "root" }, tree.VisibleKeys);
    }

    private sealed class PreparingSibling(Action prepare) : Lumyte.UI.Compose.Definitions.Widget
    {
        internal Action Prepare { get; } = prepare;

        protected override UiElement CreateElement() => new SiblingElement(this);

        private sealed class SiblingElement(PreparingSibling widget) : UiElement<PreparingSibling>(widget)
        {
            protected override Action? PrepareUpdate(PreparingSibling next, UiElementUpdateContext context)
            {
                next.Prepare();
                return null;
            }

            protected override System.Numerics.Vector2 LayoutOverride(UiBoxConstraints constraints) => constraints.Constrain(new(1));
        }
    }

    private sealed class PendingInput(Func<bool> pending) : Lumyte.UI.Compose.Definitions.Widget
    {
        internal Func<bool> Pending { get; } = pending;

        protected override UiElement CreateElement() => new PendingElement(this);

        private sealed class PendingElement(PendingInput widget) : UiElement<PendingInput>(widget)
        {
            protected override bool PreserveRealization => Widget.Pending();

            protected override System.Numerics.Vector2 LayoutOverride(UiBoxConstraints constraints) => constraints.Constrain(new(20));
        }
    }

    private sealed class DeferredDispatcher : IUiDispatcher
    {
        private readonly Queue<Action> _pending = new();

        public bool CheckAccess() => true;

        public ValueTask InvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending.Enqueue(() =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    action();
                    completion.SetResult();
                }
                catch (Exception error)
                {
                    completion.SetException(error);
                }
            });
            return new(completion.Task);
        }

        public ValueTask<TResult> InvokeAsync<TResult>(Func<TResult> action, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        internal void Drain()
        {
            while (_pending.TryDequeue(out Action? action))
            {
                action();
            }
        }
    }
}
