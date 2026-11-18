using System.Numerics;
using Lumyte.Composition;
using Lumyte.Input.Abstractions;
using Lumyte.Signals;
using Xunit;
using static Lumyte.UI.Collections.Compose;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Collections.Tests;

/// <summary>Exercises generated template entrances through mounted collection hosts.</summary>
public sealed class TemplateSlotTests
{
    /// <summary>Inline lambdas infer each concrete declaration and run only during preparation.</summary>
    [Fact]
    public void ConcreteOwnersAndLegacyPrecedenceAreDeferred()
    {
        var owners = new List<object>();
        Compose.Definitions.ItemsRepeater<string> repeater = ItemsRepeater<string>(items: new[] { "a" }, keySelector: value => value, itemTemplate: _ => throw new Exception("legacy"))[
            ItemsRepeaterFactory<string>().Item()[
                (value, owner) => Record(owner, owners)]];
        Compose.Definitions.ListBox<string> list = ListBox<string>(items: new[] { "b" }, keySelector: value => value, itemTemplate: _ => throw new Exception("legacy"))[
            ListBoxFactory<string>().Item()[
                (value, owner) => Record(owner, owners)]];
        Assert.Empty(owners);
        using var first = new UiSurface(repeater);
        using var second = new UiSurface(list);
        Assert.Equal(new object[] { repeater, list }, owners);
    }

    /// <summary>Missing required factories still reject, including zero-item declarations.</summary>
    [Fact]
    public void RequiredTemplatesAndLegacyFallbackRemainSupported()
    {
        Assert.Throws<ArgumentNullException>(() => new UiSurface(ItemsRepeater<string>(keySelector: value => value)));
        int calls = 0;
        using var surface = new UiSurface(ItemsRepeater<string>(items: new[] { "a" }, keySelector: value => value, itemTemplate: _ =>
        {
            calls++;
            return SizedBox();
        }));
        Assert.Equal(1, calls);
        using var empty = new UiSurface(ItemsRepeater<string>(keySelector: value => value)[ItemsRepeaterFactory<string>().Item()[
            (_, _) => SizedBox()]]);
        Assert.Empty(empty.RootElement.Children[0].Children);
    }

    /// <summary>A constant is supported for a single fresh item, never cloned for multiple items.</summary>
    [Fact]
    public void FixedCompositionRespectsFreshDisjointOwnership()
    {
        Lumyte.UI.Compose.Definitions.SizedBox root = SizedBox();
        CompositionSlotAssignment<Compose.Definitions.ItemsRepeater<string>> assignment = ItemsRepeaterFactory<string>().Item()[root];
        Compose.Definitions.ItemsRepeater<string> declaration = ItemsRepeater<string>(items: new[] { "a" }, keySelector: value => value)[assignment];
        using (var surface = new UiSurface(declaration))
        {
            Assert.Same(root, surface.RootElement.Children[0].Children[0].Widget);
            Assert.Throws<InvalidOperationException>(() => new UiSurface(ItemsRepeater<string>(items: new[] { "a" }, keySelector: value => value)[assignment]));
        }

        Assert.Throws<InvalidOperationException>(() => new UiSurface(ItemsRepeater<string>(items: new[] { "a" }, keySelector: value => value)[assignment]));
        Lumyte.UI.Compose.Definitions.SizedBox shared = SizedBox();
        Assert.Throws<InvalidOperationException>(() => new UiSurface(ItemsRepeater<string>(items: new[] { "a", "b" }, keySelector: value => value)[ItemsRepeaterFactory<string>().Item()[shared]]));
    }

    /// <summary>Reusing an assignment or declaration across surfaces invokes fresh roots per host.</summary>
    [Fact]
    public void ReusedAssignmentUsesReceivingDeclarationPerSurfaceAndUpdate()
    {
        var owners = new List<Compose.Definitions.ListBox<string>>();
        CompositionSlotAssignment<Compose.Definitions.ListBox<string>> assignment = ListBoxFactory<string>().Item()[
            (value, owner) => Record(owner, owners)];
        Compose.Definitions.ListBox<string> first = ListBox<string>(items: new[] { "a" }, keySelector: value => value)[assignment];
        Compose.Definitions.ListBox<string> next = ListBox<string>(items: new[] { "b" }, keySelector: value => value)[assignment];
        using var a = new UiSurface(first);
        using var b = new UiSurface(first);
        Assert.NotSame(a.RootElement.Children[0], b.RootElement.Children[0]);
        a.Rebuild(next);
        a.UpdateLayout();
        Assert.Equal(new[] { first, first, next }, owners);
        Assert.Equal("b", Assert.IsType<ListBoxElement<string>>(a.RootElement).Keys[0]);
    }

    /// <summary>A rejected slot preserves content, focus and accepted selection atomically.</summary>
    [Fact]
    public void SlotFailureDoesNotPublishPartialItems()
    {
        using var surface = new UiSurface(ListBox<string>(items: new[] { "a" }, keySelector: value => value, selectedKey: "a")[
            ListBoxFactory<string>().Item()[
                (_, _) => SizedBox(height: 20)]]);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        UiElement root = surface.RootElement;
        UiElement content = root.Children[0];
        UiElement option = content.Children[0].Children[0];
        Assert.True(surface.TryFocus(option));
        surface.Rebuild(ListBox<string>(items: new[] { "b", "c" }, keySelector: value => value, selectedKey: "b")[
            ListBoxFactory<string>().Item()[
                (value, _) => value == "c" ? throw new InvalidOperationException("slot") : SizedBox(height: 20)]]);
        Assert.Throws<InvalidOperationException>(surface.UpdateTree);
        Assert.Same(root, surface.RootElement);
        Assert.Same(content, root.Children[0]);
        Assert.Same(option, surface.FocusedElement);
        Assert.Equal("a", Assert.IsType<ListBoxElement<string>>(root).SelectedKey);
        Assert.Equal(new[] { "a" }, Assert.IsType<ListBoxElement<string>>(root).Keys);
    }

    /// <summary>Viewport range additions use the same typed owner while overlapping items survive.</summary>
    [Fact]
    public void VirtualRangesUseSlotAndRetainOverlap()
    {
        var owners = new List<Compose.Definitions.ListBox<int>>();
        Compose.Definitions.ListBox<int> declaration = ListBox<int>(items: Enumerable.Range(0, 1000).ToArray(), keySelector: value => value.ToString(System.Globalization.CultureInfo.InvariantCulture), height: 60, virtualization: new UiItemVirtualization(20, 1))[
            ListBoxFactory<int>().Item()[
                (value, owner) => Record(owner, owners)]];
        using var surface = new UiSurface(declaration);
        Assert.Empty(owners);
        surface.SetViewport(new(100, 60));
        surface.UpdateLayout();
        Assert.Equal(4, owners.Count);
        ScrollViewElement scroll = Assert.IsType<ScrollViewElement>(surface.RootElement.Children[0]);
        UiElement overlap = scroll.Children[0].Children[1];
        scroll.Controller.ScrollTo(20);
        surface.UpdateLayout();
        Assert.Equal(5, owners.Count);
        Assert.Same(overlap, scroll.Children[0].Children[1]);
        scroll.Controller.ScrollTo(1000);
        surface.UpdateLayout();
        Assert.All(owners, owner => Assert.Same(declaration, owner));
        Assert.InRange(scroll.Children[0].Children.Count, 1, 5);
    }

    /// <summary>Closed display and popup slots receive the combo, not their implementation wrappers.</summary>
    [Fact]
    public void ComboSelectedEmptyAndPopupUseExactOwner()
    {
        using var popup = new PopupHost();
        var itemOwners = new List<Compose.Definitions.ComboBox<string>>();
        var selectedOwners = new List<Compose.Definitions.ComboBox<string>>();
        var emptyOwners = new List<Compose.Definitions.ComboBox<string>>();
        CompositionSlotAssignment<Compose.Definitions.ComboBox<string>> selectedDisplaySlot = ComboBoxFactory<string>().SelectedItem()[
            (value, owner) => Record(owner, selectedOwners)];
        Compose.Definitions.ComboBox<string> declaration = ComboBox<string>(items: new[] { "a", "b" }, keySelector: value => value, popupHost: popup, selectedKey: "a", itemTemplate: _ => throw new Exception("legacy item"), selectedItemTemplate: _ => throw new Exception("legacy selected"), emptyTemplate: () => throw new Exception("legacy empty"))[
            ComboBoxFactory<string>().Item()[
                (value, owner) => Record(owner, itemOwners)],
            selectedDisplaySlot,
            ComboBoxFactory<string>().Empty()[owner => Record(owner, emptyOwners)]];
        Assert.Empty(selectedOwners);
        using var surface = new UiSurface(declaration);
        surface.SetViewport(new(100, 40));
        surface.UpdateLayout();
        Assert.Single(selectedOwners);
        Assert.Same(declaration, selectedOwners[0]);
        Assert.True(surface.TryFocus(surface.RootElement));
        surface.Dispatch(new UiKeyInput(Key.ArrowDown, true));
        Assert.Equal(2, itemOwners.Count);
        Assert.All(itemOwners, owner => Assert.Same(declaration, owner));
        Assert.NotNull(popup.Surface);
        UiElement first = popup.Surface!.RootElement.Children[0].Children[0].Children[0];
        Assert.True(popup.Surface.TryFocus(first));
        popup.Surface.Dispatch(new UiKeyInput(Key.ArrowDown, true));
        popup.Surface.Dispatch(new UiKeyInput(Key.ArrowDown, false));
        popup.Surface.Dispatch(new UiKeyInput(Key.Enter, true));
        popup.Surface.Dispatch(new UiKeyInput(Key.Enter, false));
        surface.UpdateLayout();
        Assert.Equal("b", Assert.IsType<ComboBoxElement<string>>(surface.RootElement).SelectedKey);
        Assert.All(selectedOwners, owner => Assert.Same(declaration, owner));
        Assert.Empty(emptyOwners);
        CompositionSlotAssignment<Compose.Definitions.ComboBox<string>> emptySelectedSlot = ComboBoxFactory<string>().SelectedItem()[
            (_, _) => SizedBox(height: 20)];
        Compose.Definitions.ComboBox<string> emptyDeclaration = ComboBox<string>(items: new[] { "a" }, keySelector: value => value, popupHost: popup, emptyTemplate: () => throw new Exception("legacy empty"))[
            ComboBoxFactory<string>().Item()[
                (_, _) => SizedBox(height: 20)],
            emptySelectedSlot,
            ComboBoxFactory<string>().Empty()[owner => Record(owner, emptyOwners)]];
        surface.Rebuild(emptyDeclaration);
        surface.UpdateLayout();
        Assert.Single(emptyOwners);
        Assert.Same(emptyDeclaration, emptyOwners[0]);
    }

    /// <summary>Both selected and empty templates remain required independently of current selection.</summary>
    [Fact]
    public void ComboRequiredValidationAcceptsOnlySuppliedAlternatives()
    {
        using var popup = new PopupHost();
        Assert.Throws<ArgumentNullException>(() => new UiSurface(ComboBox<string>(keySelector: value => value, popupHost: popup)[
            ComboBoxFactory<string>().Item()[
                (_, _) => SizedBox()],
            ComboBoxFactory<string>().Empty()[_ => SizedBox()]]));
        CompositionSlotAssignment<Compose.Definitions.ComboBox<string>> requiredSelectedSlot = ComboBoxFactory<string>().SelectedItem()[
            (_, _) => SizedBox()];
        Assert.Throws<ArgumentNullException>(() => new UiSurface(ComboBox<string>(keySelector: value => value, popupHost: popup)[
            ComboBoxFactory<string>().Item()[
                (_, _) => SizedBox()],
            requiredSelectedSlot]));
        int calls = 0;
        Widget LegacySelected(string value)
        {
            calls++;
            return SizedBox();
        }

        using var fallback = new UiSurface(ComboBox<string>(items: new[] { "a" }, keySelector: value => value, popupHost: popup, selectedKey: "a", itemTemplate: _ => SizedBox(), selectedItemTemplate: LegacySelected, emptyTemplate: () => SizedBox()));
        Assert.Equal(1, calls);
    }

    /// <summary>Fixed selected and empty compositions use the same fresh-root rules as item content.</summary>
    [Fact]
    public void FixedComboDisplayRejectsAlreadyMountedRoot()
    {
        using var popup = new PopupHost();
        Lumyte.UI.Compose.Definitions.SizedBox root = SizedBox();
        CompositionSlotAssignment<Compose.Definitions.ComboBox<string>> selected = ComboBoxFactory<string>().SelectedItem()[root];
        Compose.Definitions.ComboBox<string> first = ComboBox<string>(items: new[] { "a" }, keySelector: value => value, popupHost: popup, selectedKey: "a", itemTemplate: _ => SizedBox(), emptyTemplate: () => SizedBox())[selected];
        using var surface = new UiSurface(first);
        Assert.Throws<InvalidOperationException>(() => new UiSurface(ComboBox<string>(keySelector: value => value, popupHost: popup, itemTemplate: _ => SizedBox(), selectedItemTemplate: _ => SizedBox())[
            ComboBoxFactory<string>().Empty()[root]]));
    }

    /// <summary>Signal arguments recreate declarations without losing slots or their new owner binding.</summary>
    [Fact]
    public void ReactiveArgumentReplayRebindsSlotOwner()
    {
        var height = Signal.Create<float?>(60);
        var owners = new List<Compose.Definitions.ListBox<string>>();
        Compose.Definitions.ListBox<string> declaration = ListBox<string>(items: new[] { "a" }, keySelector: value => value, height: height)[
            ListBoxFactory<string>().Item()[
                (value, owner) => Record(owner, owners)]];
        using var surface = new UiSurface(declaration);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        Assert.Same(declaration, owners.Single());
        height.Value = 80;
        surface.UpdateLayout();
        Compose.Definitions.ListBox<string> replacement = Assert.IsType<Compose.Definitions.ListBox<string>>(surface.RootElement.Widget);
        Assert.NotSame(declaration, replacement);
        Assert.Equal(80, replacement.Height);
        Assert.Same(replacement, owners.Last());
        Assert.Equal(2, owners.Count);
    }

    /// <summary>An enclosing reactive recipe reconstructs host-owned children instead of treating them as declaration slots.</summary>
    [Fact]
    public void ParentReactiveRecipeReconstructsCollectionSlot()
    {
        var height = Signal.Create<float?>(100);
        var owners = new List<Compose.Definitions.ItemsRepeater<string>>();
        Compose.Definitions.ItemsRepeater<string> declaration = ItemsRepeater<string>(items: new[] { "a" }, keySelector: value => value)[
            ItemsRepeaterFactory<string>().Item()[
                (value, owner) => Record(owner, owners)]];
        using var surface = new UiSurface(Stack()[declaration, SlotRecipes.Compose.RecipeTrigger(height: height)]);
        surface.SetViewport(new(100, 120));
        surface.UpdateLayout();
        height.Value = 120;
        surface.UpdateLayout();
        Compose.Definitions.ItemsRepeater<string> replacement = Assert.IsType<Compose.Definitions.ItemsRepeater<string>>(surface.RootElement.Children[0].Widget);
        Assert.NotSame(declaration, replacement);
        Assert.Same(replacement, owners.Last());
        Assert.Equal(2, owners.Count);
    }

    /// <summary>Reactive combo reconstruction replays selected, empty and popup templates with the newest owner.</summary>
    [Fact]
    public void ComboReactiveRecipeRebindsAllSlotOwners()
    {
        using var popup = new PopupHost();
        var selection = Signal.Create<string?>("a");
        var itemOwners = new List<Compose.Definitions.ComboBox<string>>();
        var selectedOwners = new List<Compose.Definitions.ComboBox<string>>();
        var emptyOwners = new List<Compose.Definitions.ComboBox<string>>();
        CompositionSlotAssignment<Compose.Definitions.ComboBox<string>> reactiveSelectedSlot = ComboBoxFactory<string>().SelectedItem()[
            (value, owner) => Record(owner, selectedOwners)];
        Compose.Definitions.ComboBox<string> declaration = ComboBox<string>(items: new[] { "a", "b" }, keySelector: value => value, popupHost: popup, selectedKey: selection)[
            ComboBoxFactory<string>().Item()[
                (value, owner) => Record(owner, itemOwners)],
            reactiveSelectedSlot,
            ComboBoxFactory<string>().Empty()[owner => Record(owner, emptyOwners)]];
        using var surface = new UiSurface(declaration);
        surface.SetViewport(new(100, 40));
        surface.UpdateLayout();
        Assert.Same(declaration, selectedOwners.Single());
        selection.Value = null;
        surface.UpdateLayout();
        Compose.Definitions.ComboBox<string> emptyOwner = Assert.IsType<Compose.Definitions.ComboBox<string>>(surface.RootElement.Widget);
        Assert.NotSame(declaration, emptyOwner);
        Assert.Same(emptyOwner, emptyOwners.Single());
        selection.Value = "b";
        surface.UpdateLayout();
        Compose.Definitions.ComboBox<string> selectedOwner = Assert.IsType<Compose.Definitions.ComboBox<string>>(surface.RootElement.Widget);
        Assert.NotSame(emptyOwner, selectedOwner);
        Assert.Same(selectedOwner, selectedOwners.Last());
        Assert.True(surface.TryFocus(surface.RootElement));
        surface.Dispatch(new UiKeyInput(Key.ArrowDown, true));
        Assert.Equal(2, itemOwners.Count);
        Assert.All(itemOwners, owner => Assert.Same(selectedOwner, owner));
    }

    /// <summary>Repeated slot assignments retain last-assignment precedence through generated replay.</summary>
    [Fact]
    public void LastSlotWinsBeforeAndAfterReactiveReplay()
    {
        var height = Signal.Create<float?>(60);
        var owners = new List<Compose.Definitions.ListBox<string>>();
        CompositionSlotAssignment<Compose.Definitions.ListBox<string>> winningItemSlot = ListBoxFactory<string>().Item()[
            (value, owner) => Record(owner, owners)];
        Compose.Definitions.ListBox<string> declaration = ListBox<string>(items: new[] { "a" }, keySelector: value => value, height: height, itemTemplate: _ => throw new InvalidOperationException("legacy"))[
            ListBoxFactory<string>().Item()[
                (_, _) => throw new InvalidOperationException("first slot")],
            winningItemSlot];
        using var surface = new UiSurface(declaration);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        height.Value = 80;
        surface.UpdateLayout();
        Assert.Equal(2, owners.Count);
        Assert.Same(declaration, owners[0]);
        Assert.Same(surface.RootElement.Widget, owners[1]);
        Assert.NotSame(owners[0], owners[1]);
    }

    /// <summary>The offscreen focused restoration path invokes the replacement declaration's slot.</summary>
    [Fact]
    public void VirtualResetRestoresFreshFocusedPinWithNewOwner()
    {
        var calls = new List<(int Value, Compose.Definitions.ListBox<int> Owner)>();
        CompositionSlotAssignment<Compose.Definitions.ListBox<int>> assignment = ListBoxFactory<int>().Item()[
            (value, owner) => RecordCall(value, owner, calls)];
        Compose.Definitions.ListBox<int> Create() => ListBox<int>(items: Enumerable.Range(0, 1000).ToArray(), keySelector: value => value.ToString(System.Globalization.CultureInfo.InvariantCulture), selectedKey: "0", height: 60, virtualization: new UiItemVirtualization(20, 0))[assignment];
        using var surface = new UiSurface(Create());
        surface.SetViewport(new(100, 60));
        surface.UpdateLayout();
        UiElement previous = surface.RootElement.Children[0].Children[0].Children[0];
        Assert.True(surface.TryFocus(previous));
        surface.UpdateLayout();
        ScrollController scroll = Assert.IsType<ScrollViewElement>(surface.RootElement.Children[0]).Controller;
        scroll.ScrollTo(1000);
        surface.UpdateLayout();
        Compose.Definitions.ListBox<int> next = Create();
        calls.Clear();
        surface.Rebuild(next);
        surface.UpdateLayout();
        Assert.Contains(calls, call => call.Value == 0);
        Assert.All(calls, call => Assert.Same(next, call.Owner));
        Assert.Equal(1000, scroll.Offset);
        Assert.Equal("0", surface.FocusedElement!.Widget.Key);
        Assert.NotEqual(previous.AutomationId, surface.FocusedElement.AutomationId);
        Assert.False(previous.IsMounted);
        Assert.Equal(4, surface.RootElement.Children[0].Children[0].Children.Count);
    }

    /// <summary>Failed virtual slot preparation cannot publish scroll geometry or callbacks.</summary>
    [Fact]
    public void VirtualSlotFailureKeepsAcceptedScrollAndRealizations()
    {
        bool fail = false;
        using var surface = new UiSurface(ListBox<int>(items: Enumerable.Range(0, 1000).ToArray(), keySelector: value => value.ToString(System.Globalization.CultureInfo.InvariantCulture), height: 60, virtualization: new UiItemVirtualization(20, 0))[
            ListBoxFactory<int>().Item()[
                (_, _) => fail ? throw new InvalidOperationException("slot") : SizedBox(height: 20)]]);
        surface.SetViewport(new(100, 60));
        surface.UpdateLayout();
        ListBoxElement<int> list = Assert.IsType<ListBoxElement<int>>(surface.RootElement);
        ScrollViewElement scroll = Assert.IsType<ScrollViewElement>(list.Children[0]);
        UiElement previous = scroll.Children[0];
        int callbacks = 0;
        scroll.Controller.OnChanged = _ => callbacks++;
        fail = true;
        Assert.True(list.BringIntoView("999"));
        Assert.Throws<InvalidOperationException>(surface.UpdateLayout);
        Assert.Equal(0, scroll.Controller.Offset);
        Assert.Equal(0, callbacks);
        Assert.Same(previous, scroll.Children[0]);
        fail = false;
        surface.UpdateLayout();
        Assert.Equal(19940, scroll.Controller.Offset);
        Assert.Equal(1, callbacks);
    }

    private static Widget Record<T>(T owner, List<T> owners)
    {
        owners.Add(owner);
        return SizedBox(height: 20);
    }

    private static Widget RecordCall(int value, Compose.Definitions.ListBox<int> owner, List<(int Value, Compose.Definitions.ListBox<int> Owner)> calls)
    {
        calls.Add((value, owner));
        return SizedBox(height: 20);
    }

    private sealed class PopupHost : ICollectionPopupHost, IDisposable
    {
        public UiSurface? Surface { get; private set; }

        public IDisposable Open(UiElement anchor, Vector2 size, Func<Widget> createContent, Action closed)
        {
            Surface = new UiSurface(createContent());
            Surface.SetViewport(size);
            Surface.UpdateLayout();
            return new CloseRequest(closed);
        }

        public void Dispose() => Surface?.Dispose();

        private sealed class CloseRequest(Action closed) : IDisposable
        {
            public void Dispose() => closed();
        }
    }
}
