using Lumyte.Signals;
using Xunit;
using static Lumyte.UI.Collections.Compose;
using static Lumyte.UI.Collections.Tests.OwnershipFunctions;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Collections.Tests;

/// <summary>Checks ownership after actual component preparation, not just declaration slots.</summary>
public sealed class GeneratedOwnershipTests
{
    /// <summary>Tree rows validate generated descendants through the same ownership scope.</summary>
    [Fact]
    public void TreeFunctionsCannotShareGeneratedDescendants()
    {
        Widget shared = SizedBox();
        Assert.Throws<InvalidOperationException>(() => new UiSurface(TreeView<string>(
            roots: new[] { "a", "b" },
            keySelector: key => key,
            childrenSelector: _ => Array.Empty<string>(),
            hasChildren: _ => false,
            itemTemplate: _ => Generated(() => shared))));
    }

    /// <summary>Fresh function declarations cannot hide a shared generated result.</summary>
    [Fact]
    public void FreshFunctionsCannotReturnTheSameDescendant()
    {
        Widget shared = SizedBox();
        Assert.Throws<InvalidOperationException>(() => new UiSurface(ItemsRepeater<string>(items: new[] { "a", "b" }, keySelector: x => x, itemTemplate: _ => Generated(() => shared))));
    }

    /// <summary>A generated result cannot borrow an already mounted ordinary declaration.</summary>
    [Fact]
    public void FunctionCannotReturnMountedDescendant()
    {
        Widget shared = SizedBox();
        using var ordinary = new UiSurface(shared);
        Assert.Throws<InvalidOperationException>(() => new UiSurface(ItemsRepeater<string>(items: new[] { "a" }, keySelector: x => x, itemTemplate: _ => Generated(() => shared))));
    }

    /// <summary>Nested functions and custom element content participate in ownership.</summary>
    [Fact]
    public void NestedCustomContentCannotHideDuplicate()
    {
        Widget shared = SizedBox();
        Assert.Throws<InvalidOperationException>(() => new UiSurface(ItemsRepeater<string>(items: new[] { "a", "b" }, keySelector: x => x, itemTemplate: _ => Generated(() => new Custom(() => Generated(() => shared))))));
    }

    /// <summary>A later signal-only function update cannot share another item's mounted result.</summary>
    /// <param name="virtualized">Whether ownership is attached to a virtual item range.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SignalUpdateRejectsDuplicateAndKeepsLiveTree(bool virtualized)
    {
        var change = Signal.Create(false);
        Widget first = SizedBox(height: 20);
        Widget second = SizedBox(height: 20);
        int builds = 0;
        using var surface = new UiSurface(ListBox<string>(items: new[] { "a", "b" }, keySelector: x => x, selectedKey: "a", virtualization: virtualized ? new UiItemVirtualization(20, 0) : null, itemTemplate: x => Generated(() =>
        {
            builds++;
            return x == "a" ? first : change.Value ? first : second;
        })));
        surface.SetViewport(new(100, 30));
        surface.UpdateLayout();
        UiElement oldRoot = surface.RootElement;
        UiElement option = oldRoot.Children[0].Children[0].Children[1];
        Assert.True(surface.TryFocus(option));
        ScrollViewElement scroll = Assert.IsType<ScrollViewElement>(oldRoot.Children[0]);
        scroll.Controller.ScrollTo(10);
        surface.UpdateLayout();
        UiAutomationId[] ids = Descendants(oldRoot).Select(x => x.AutomationId).ToArray();
        UiSignalRevision subscriptions = surface.SignalRevision;
        int callbacks = 0;
        scroll.Controller.OnChanged = _ => callbacks++;
        change.Value = true;
        UiStateUpdateException error = Assert.Throws<UiStateUpdateException>(virtualized ? surface.UpdateLayout : surface.UpdateTree);
        Assert.IsType<InvalidOperationException>(error.InnerException);
        Assert.Same(oldRoot, surface.RootElement);
        Assert.Same(option, surface.FocusedElement);
        Assert.Equal("a", Assert.IsType<ListBoxElement<string>>(oldRoot).SelectedKey);
        Assert.Equal(10, scroll.Controller.Offset);
        Assert.Equal(0, callbacks);
        Assert.Equal(ids, Descendants(oldRoot).Select(x => x.AutomationId));
        Assert.Equal(subscriptions.Connections, surface.SignalRevision.Connections);
        Assert.Equal(3, builds);
        change.Value = false;
        Assert.Equal(3, builds);
    }

    /// <summary>Disjoint signal plans are validated together before either publishes a fresh shared result.</summary>
    [Fact]
    public void BatchedSignalsCannotIntroduceFreshSharedGeneratedResult()
    {
        var change = Signal.Create(false);
        Widget shared = SizedBox();
        using var surface = new UiSurface(ItemsRepeater<string>(items: new[] { "a", "b" }, keySelector: x => x, itemTemplate: _ => Generated(() => new Custom(() => Generated(() => change.Value ? shared : SizedBox())))));
        UiElement root = surface.RootElement;
        UiAutomationId[] ids = Descendants(root).Select(x => x.AutomationId).ToArray();
        change.Value = true;
        UiStateUpdateException error = Assert.Throws<UiStateUpdateException>(surface.UpdateTree);
        Assert.IsType<InvalidOperationException>(error.InnerException);
        Assert.Equal(ids, Descendants(root).Select(x => x.AutomationId));
    }

    /// <summary>A custom reactive item root cannot shed its ownership boundary by changing element type.</summary>
    [Fact]
    public void ReplacingItemRootKeepsOwnershipScope()
    {
        var change = Signal.Create(false);
        Widget mounted = SizedBox();
        using var ordinary = new UiSurface(mounted);
        using var surface = new UiSurface(ItemsRepeater<string>(items: new[] { "a" }, keySelector: x => x, itemTemplate: _ => Replacing(change: change, next: () => Generated(() => mounted))));
        UiElement oldItem = surface.RootElement.Children[0].Children[0];
        change.Value = true;
        UiStateUpdateException error = Assert.Throws<UiStateUpdateException>(surface.UpdateTree);
        Assert.IsType<InvalidOperationException>(error.InnerException);
        Assert.Same(oldItem, surface.RootElement.Children[0].Children[0]);
    }

    /// <summary>A successful custom root replacement keeps checking subsequent generated updates.</summary>
    [Fact]
    public void SuccessfulItemRootReplacementPersistsOwnershipScope()
    {
        var replace = Signal.Create(false);
        var share = Signal.Create(false);
        Widget mounted = SizedBox();
        using var ordinary = new UiSurface(mounted);
        using var surface = new UiSurface(ItemsRepeater<string>(items: new[] { "a" }, keySelector: x => x, itemTemplate: _ => Replacing(change: replace, next: () => Generated(() => share.Value ? mounted : SizedBox()))));
        UiElement oldItem = surface.RootElement.Children[0].Children[0];
        replace.Value = true;
        surface.UpdateTree();
        UiElement replacement = surface.RootElement.Children[0].Children[0];
        Assert.NotSame(oldItem, replacement);
        share.Value = true;
        UiStateUpdateException error = Assert.Throws<UiStateUpdateException>(surface.UpdateTree);
        Assert.IsType<InvalidOperationException>(error.InnerException);
        Assert.Same(replacement, surface.RootElement.Children[0].Children[0]);
    }

    /// <summary>Same-item retained results and ordinary multi-surface sharing remain legal.</summary>
    [Fact]
    public void RetainedResultAndOrdinarySharedDeclarationsRemainLegal()
    {
        var version = Signal.Create(0);
        Widget shared = SizedBox();
        using (var first = new UiSurface(Generated(() => shared)))
        using (var second = new UiSurface(Generated(() => shared)))
        {
            Assert.Same(first.RootElement.Children[0].Widget, second.RootElement.Children[0].Widget);
        }

        using var collection = new UiSurface(ItemsRepeater<string>(items: new[] { "a" }, keySelector: x => x, itemTemplate: item => Generated(() =>
        {
            _ = version.Value;
            return shared;
        })));
        UiElement retained = collection.RootElement.Children[0].Children[0].Children[0];
        version.Value++;
        collection.UpdateTree();
        Assert.Same(retained, collection.RootElement.Children[0].Children[0].Children[0]);
    }

    /// <summary>Rejected generated content leaves old connections and input state untouched.</summary>
    [Fact]
    public void FailedRebuildKeepsIdsFocusOffsetAndSubscriptions()
    {
        var oldSource = Signal.Create(20f);
        var rejectedSource = Signal.Create(20f);
        int oldBuilds = 0;
        int changes = 0;
        using var surface = new UiSurface(ListBox<string>(items: new[] { "a", "b", "c" }, keySelector: x => x, selectedKey: "a", onSelectionChanged: _ => changes++, itemTemplate: _ => Generated(() =>
        {
            oldBuilds++;
            return SizedBox(height: oldSource.Value);
        })));
        surface.SetViewport(new(100, 30));
        surface.UpdateLayout();
        UiElement root = surface.RootElement;
        UiElement option = root.Children[0].Children[0].Children[1];
        surface.TryFocus(option);
        ScrollViewElement scroll = Assert.IsType<ScrollViewElement>(root.Children[0]);
        scroll.Controller.ScrollTo(10);
        surface.UpdateLayout();
        UiAutomationId[] ids = Descendants(root).Select(x => x.AutomationId).ToArray();
        UiSignalRevision subscriptions = surface.SignalRevision;
        int scrollChanges = 0;
        scroll.Controller.OnChanged = _ => scrollChanges++;
        Widget duplicate = SizedBox();
        surface.Rebuild(ListBox<string>(items: new[] { "x", "y" }, keySelector: x => x, itemTemplate: item => Generated(() =>
        {
            _ = rejectedSource.Value;
            return duplicate;
        })));
        Assert.Throws<InvalidOperationException>(surface.UpdateTree);
        Assert.Same(root, surface.RootElement);
        Assert.Same(option, surface.FocusedElement);
        Assert.Equal(ids, Descendants(root).Select(x => x.AutomationId));
        Assert.Equal("a", Assert.IsType<ListBoxElement<string>>(root).SelectedKey);
        Assert.Equal(10, scroll.Controller.Offset);
        Assert.Equal(subscriptions, surface.SignalRevision);
        rejectedSource.Value++;
        Assert.Equal(subscriptions, surface.SignalRevision);
        oldSource.Value++;
        Assert.True(surface.SignalRevision.Values > subscriptions.Values);
        Assert.Equal(3, oldBuilds);
        Assert.Equal(0, changes);
        Assert.Equal(0, scrollChanges);
    }

    private static IEnumerable<UiElement> Descendants(UiElement root) => new[] { root }.Concat(root.Children.SelectMany(Descendants));

    private sealed class Custom(Func<Widget> build) : Widget
    {
        protected override UiElement CreateElement() => new CustomElement(this, build);
    }

    private sealed class CustomElement(Custom widget, Func<Widget> build) : UiElement(widget)
    {
        protected override UiElementContent? BuildContent(Widget next, UiElementBuildContext context) => new(build());
    }
}
