using Lumyte.Input.Abstractions;
using Xunit;
using static Lumyte.UI.Collections.Compose;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Collections.Tests;

/// <summary>Exercises public typed factories and ordinary item input.</summary>
public sealed class CollectionTests
{
    /// <summary>Copies items and rejects bad keys before calling templates.</summary>
    [Fact]
    public void ItemsAreCopiedAndDuplicateKeysRejectBeforeTemplates()
    {
        var items = new List<string> { "a", "b" };
        using var surface = new UiSurface(ItemsRepeater<string>(items: items, keySelector: item => item, itemTemplate: _ => SizedBox(height: 20)));
        items.Clear();
        Assert.Equal(2, Assert.IsType<ItemsRepeaterElement<string>>(surface.RootElement).Items.Count);
        int templates = 0;
        Assert.Throws<ArgumentException>(() => new UiSurface(ItemsRepeater<string>(items: new[] { "a", "a" }, keySelector: item => item, itemTemplate: _ =>
        {
            templates++;
            return SizedBox();
        })));
        Assert.Equal(0, templates);
    }

    /// <summary>Failed preparation preserves the entire old tree and selection.</summary>
    [Fact]
    public void FailedTemplateKeepsTreeSelectionAndFocus()
    {
        using UiSurface surface = CreateList();
        UiElement oldRoot = surface.RootElement;
        UiElement oldContent = oldRoot.Children[0];
        UiElement option = oldContent.Children[0].Children[0];
        Assert.True(surface.TryFocus(option));
        surface.Rebuild(ListBox<string>(items: new[] { "x", "y" }, keySelector: item => item, itemTemplate: _ => throw new InvalidOperationException("template")));
        Assert.Throws<InvalidOperationException>(surface.UpdateTree);
        Assert.Same(oldRoot, surface.RootElement);
        Assert.Same(oldContent, surface.RootElement.Children[0]);
        Assert.Same(option, surface.FocusedElement);
        Assert.Equal("a", Assert.IsType<ListBoxElement<string>>(oldRoot).SelectedKey);
    }

    /// <summary>Normal pointer and keys distinguish active focus from accepted selection.</summary>
    [Fact]
    public void KeyboardMovesActiveOnlyAndActivationSelectsOnce()
    {
        int changes = 0;
        using UiSurface surface = CreateList(_ => changes++);
        UiElement first = surface.RootElement.Children[0].Children[0].Children[0];
        Assert.True(surface.TryFocus(first));
        surface.Dispatch(new UiKeyInput(Key.ArrowDown, true));
        surface.Dispatch(new UiKeyInput(Key.ArrowDown, false));
        ListBoxElement<string> list = Assert.IsType<ListBoxElement<string>>(surface.RootElement);
        Assert.Equal("b", list.ActiveKey);
        Assert.Equal("a", list.SelectedKey);
        surface.Dispatch(new UiKeyInput(Key.Enter, true));
        surface.Dispatch(new UiKeyInput(Key.Enter, false));
        Assert.Equal("b", list.SelectedKey);
        Assert.Equal(1, changes);
    }

    /// <summary>A reset gives item roots fresh connection identities.</summary>
    [Fact]
    public void ResetRebuildsAllItemsAndRemovesMissingSelectionSilently()
    {
        int changes = 0;
        using UiSurface surface = CreateList(_ => changes++);
        UiAutomationId old = surface.RootElement.Children[0].Children[0].Children[0].AutomationId;
        surface.Rebuild(ListBox<string>(items: new[] { "a", "c" }, keySelector: item => item, itemTemplate: _ => SizedBox(height: 20), selectedKey: "b", onSelectionChanged: _ => changes++));
        surface.UpdateLayout();
        Assert.NotEqual(old, surface.RootElement.Children[0].Children[0].Children[0].AutomationId);
        Assert.Null(Assert.IsType<ListBoxElement<string>>(surface.RootElement).SelectedKey);
        Assert.Equal(0, changes);
    }

    /// <summary>Already mounted roots and shared item content are rejected.</summary>
    [Fact]
    public void TemplateRootsCannotBeMountedOrShared()
    {
        Lumyte.UI.Compose.Definitions.SizedBox root = SizedBox();
        using var mounted = new UiSurface(root);
        Assert.Throws<InvalidOperationException>(() => new UiSurface(ItemsRepeater<string>(items: new[] { "a" }, keySelector: item => item, itemTemplate: _ => root)));
        Lumyte.UI.Compose.Definitions.SizedBox shared = SizedBox();
        Assert.Throws<InvalidOperationException>(() => new UiSurface(ItemsRepeater<string>(items: new[] { "a", "b" }, keySelector: item => item, itemTemplate: _ => shared)));
    }

    /// <summary>Preserves an ordinal scroll anchor when earlier items are inserted.</summary>
    [Fact]
    public void ResetPreservesScrollAnchorWithoutNotifying()
    {
        string[] items = Enumerable.Range(0, 20).Select(index => index.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        using var surface = new UiSurface(ListBox<string>(items: items, keySelector: item => item, itemTemplate: _ => SizedBox(height: 20), height: 60));
        surface.SetViewport(new(100, 60));
        surface.UpdateLayout();
        ScrollViewElement scroll = Assert.IsType<ScrollViewElement>(surface.RootElement.Children[0]);
        scroll.Controller.ScrollTo(105);
        surface.UpdateLayout();
        int notifications = 0;
        scroll.Controller.OnChanged = _ => notifications++;
        surface.Rebuild(ListBox<string>(items: new[] { "new" }.Concat(items).ToArray(), keySelector: item => item, itemTemplate: _ => SizedBox(height: 20), height: 60));
        surface.UpdateLayout();
        Assert.Equal(125, scroll.Controller.Offset);
        Assert.Equal(0, notifications);
    }

    /// <summary>Parent-specific layout data is checked before publishing item trees.</summary>
    [Fact]
    public void InvalidTemplateParentDataKeepsOldContent()
    {
        using UiSurface surface = CreateList();
        UiElement old = surface.RootElement.Children[0];
        surface.Rebuild(ListBox<string>(items: new[] { "a" }, keySelector: item => item, itemTemplate: _ => SizedBox(with: [Grid.Cell()])));
        Assert.Throws<InvalidOperationException>(surface.UpdateTree);
        Assert.Same(old, surface.RootElement.Children[0]);
    }

    /// <summary>Pointer activation accepts state before calling the latest delegate.</summary>
    [Fact]
    public void PointerSelectionUsesLatestCallbackAndSharedDeclarationsStayPassive()
    {
        Compose.Definitions.ListBox<string> declaration = ListBox<string>(items: new[] { "a", "b" }, keySelector: item => item, itemTemplate: _ => SizedBox(width: 100, height: 20));
        using var first = new UiSurface(declaration);
        using var second = new UiSurface(declaration);
        first.SetViewport(new(100, 100));
        second.SetViewport(new(100, 100));
        first.UpdateLayout();
        second.UpdateLayout();
        int changes = 0;
        declaration.OnSelectionChanged = key =>
        {
            Assert.Equal(key, Assert.IsType<ListBoxElement<string>>(first.RootElement).SelectedKey);
            changes++;
            throw new InvalidOperationException("callback");
        };
        first.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(10, 30)));
        Assert.Throws<InvalidOperationException>(() => first.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(10, 30))));
        Assert.Equal("b", Assert.IsType<ListBoxElement<string>>(first.RootElement).SelectedKey);
        Assert.Null(Assert.IsType<ListBoxElement<string>>(second.RootElement).SelectedKey);
        Assert.Null(declaration.SelectedKey);
        Assert.Equal(1, changes);
    }

    /// <summary>Nested buttons own pointer input rather than falsely selecting their item.</summary>
    [Fact]
    public void NestedFocusableTemplateUsesItsOwnProbeAndActivation()
    {
        int clicks = 0;
        using var surface = new UiSurface(ListBox<string>(items: new[] { "a" }, keySelector: item => item, itemTemplate: _ => Button(width: 80, height: 20, onClick: () => clicks++)));
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        UiAutomationNode button = surface.CaptureAutomationSnapshot().Nodes.Single(node => node.Role == UiRole.Button);
        Assert.Equal(button.Id, surface.ProbePointer(new(5)).ActivationTarget);
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(5)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(5)));
        Assert.Equal(1, clicks);
        Assert.Null(Assert.IsType<ListBoxElement<string>>(surface.RootElement).SelectedKey);
    }

    /// <summary>Rebuilt item IDs are new while focused keys are restored after geometry publication.</summary>
    [Fact]
    public void ResetRestoresFocusedItemByKey()
    {
        using UiSurface surface = CreateList();
        UiElement previous = surface.RootElement.Children[0].Children[0].Children[1];
        surface.TryFocus(previous);
        surface.Rebuild(ListBox<string>(items: new[] { "c", "b", "a" }, keySelector: item => item, itemTemplate: _ => SizedBox(height: 20)));
        surface.UpdateLayout();
        Assert.NotSame(previous, surface.FocusedElement);
        Assert.Equal("b", surface.FocusedElement?.Widget.Key);
    }

    /// <summary>Unavailable metadata is owned by real list item elements.</summary>
    [Fact]
    public void DisabledAndHiddenItemsAreSkippedWithoutClearingSelection()
    {
        using var surface = new UiSurface(ListBox<string>(items: new[] { "a", "b", "c", "d" }, keySelector: item => item, itemTemplate: _ => SizedBox(height: 20), itemEnabled: item => item != "b", itemVisible: item => item != "c", selectedKey: "b"));
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        UiElement first = surface.RootElement.Children[0].Children[0].Children[0];
        surface.TryFocus(first);
        surface.Dispatch(new UiKeyInput(Key.ArrowDown, true));
        surface.Dispatch(new UiKeyInput(Key.ArrowDown, false));
        Assert.Equal("d", surface.FocusedElement?.Widget.Key);
        Assert.Equal("b", Assert.IsType<ListBoxElement<string>>(surface.RootElement).SelectedKey);
    }

    /// <summary>Reentry on the accepted item does not recurse; the next operation uses the latest handler.</summary>
    [Fact]
    public void SelectionCallbackReentryIsBoundedAndLatestHandlerCanDispose()
    {
        using UiSurface surface = CreateList();
        Compose.Definitions.ListBox<string> declaration = Assert.IsType<Compose.Definitions.ListBox<string>>(surface.Root);
        int firstCalls = 0;
        int latestCalls = 0;
        declaration.OnSelectionChanged = _ =>
        {
            firstCalls++;
            declaration.OnSelectionChanged = _ =>
            {
                latestCalls++;
                surface.Dispose();
            };
            surface.Dispatch(new UiKeyInput(Key.Enter, true));
            surface.Dispatch(new UiKeyInput(Key.Enter, false));
        };
        UiElement second = surface.RootElement.Children[0].Children[0].Children[1];
        surface.TryFocus(second);
        surface.Dispatch(new UiKeyInput(Key.Enter, true));
        surface.Dispatch(new UiKeyInput(Key.Enter, false));
        Assert.Equal(1, firstCalls);
        Assert.Equal(0, latestCalls);
        UiElement third = surface.RootElement.Children[0].Children[0].Children[2];
        surface.TryFocus(third);
        surface.Dispatch(new UiKeyInput(Key.Enter, true));
        surface.Dispatch(new UiKeyInput(Key.Enter, false));
        Assert.Equal(1, latestCalls);
        Assert.Equal(1, firstCalls);
    }

    private static UiSurface CreateList(Action<string?>? changed = null)
    {
        var surface = new UiSurface(ListBox<string>(items: new[] { "a", "b", "c" }, keySelector: item => item, itemTemplate: _ => SizedBox(width: 100, height: 20), selectedKey: "a", onSelectionChanged: changed));
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        return surface;
    }
}
