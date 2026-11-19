using Lumyte.Signals;
using Xunit;
using static Lumyte.UI.Collections.Compose;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Collections.Tests;

/// <summary>Factory failure must not partially publish sibling item ranges.</summary>
public sealed class VirtualizationTransactionTests
{
    /// <summary>Both traversal orders retain all prior realized identities.</summary>
    /// <param name="reverse">Whether the failing list is first.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SiblingFactoryFailureIsAtomic(bool reverse)
    {
        bool fail = false;
        Compose.Definitions.ListBox<int> Create(bool throwing) => ListBox<int>(
            items: Enumerable.Range(0, 100).ToArray(),
            keySelector: item => item.ToString(System.Globalization.CultureInfo.InvariantCulture),
            itemTemplate: _ => throwing && fail ? throw new InvalidOperationException("factory") : SizedBox(height: 20),
            height: 60,
            virtualization: new UiItemVirtualization(20, 0));
        Compose.Definitions.ListBox<int> first = Create(reverse);
        Compose.Definitions.ListBox<int> second = Create(!reverse);
        using var surface = new UiSurface(Stack()[first, second]);
        surface.SetViewport(new(100, 120));
        surface.UpdateLayout();
        UiElement left = surface.RootElement.Children[0];
        UiElement right = surface.RootElement.Children[1];
        UiElement oldLeft = left.Children[0].Children[0];
        UiElement oldRight = right.Children[0].Children[0];
        ScrollViewElement leftScroll = Assert.IsType<ScrollViewElement>(left.Children[0]);
        ScrollViewElement rightScroll = Assert.IsType<ScrollViewElement>(right.Children[0]);
        leftScroll.Controller.ScrollTo(1000);
        rightScroll.Controller.ScrollTo(1000);
        fail = true;
        Assert.Throws<InvalidOperationException>(surface.UpdateLayout);
        Assert.Same(oldLeft, left.Children[0].Children[0]);
        Assert.Same(oldRight, right.Children[0].Children[0]);
        Assert.Equal("0", oldLeft.Children[0].Widget.Key);
        Assert.Equal("0", oldRight.Children[0].Widget.Key);
    }

    /// <summary>Key reveal cannot accept a position or callback before its factory succeeds.</summary>
    [Fact]
    public void FailedKeyRevealKeepsAcceptedControllerState()
    {
        bool fail = false;
        using var surface = new UiSurface(ListBox<int>(
            items: Enumerable.Range(0, 1000).ToArray(),
            keySelector: item => item.ToString(System.Globalization.CultureInfo.InvariantCulture),
            itemTemplate: _ => fail ? throw new InvalidOperationException("factory") : SizedBox(height: 20),
            height: 60,
            virtualization: new UiItemVirtualization(20, 0)));
        surface.SetViewport(new(100, 60));
        surface.UpdateLayout();
        ListBoxElement<int> list = Assert.IsType<ListBoxElement<int>>(surface.RootElement);
        ScrollViewElement scroll = Assert.IsType<ScrollViewElement>(list.Children[0]);
        int callbacks = 0;
        scroll.Controller.OnChanged = _ => callbacks++;
        fail = true;
        Assert.True(list.BringIntoView("999"));
        Assert.Equal(0, scroll.Controller.Offset);
        Assert.Equal(0, callbacks);
        Assert.Throws<InvalidOperationException>(surface.UpdateLayout);
        Assert.Equal(0, scroll.Controller.Offset);
        Assert.Equal(0, callbacks);
        fail = false;
        surface.UpdateLayout();
        Assert.Equal(19940, scroll.Controller.Offset);
        Assert.Equal(1, callbacks);
    }

    /// <summary>Viewport growth cannot silently clamp an accepted offset before factories succeed.</summary>
    [Fact]
    public void FailedResizeKeepsControllerGeometry()
    {
        bool fail = false;
        using var surface = new UiSurface(ListBox<int>(
            items: Enumerable.Range(0, 100).ToArray(),
            keySelector: item => item.ToString(System.Globalization.CultureInfo.InvariantCulture),
            itemTemplate: _ => fail ? throw new InvalidOperationException("factory") : SizedBox(height: 20),
            height: (float?)null,
            virtualization: new UiItemVirtualization(20, 0)));
        surface.SetViewport(new(100, 60));
        surface.UpdateLayout();
        ScrollViewElement scroll = Assert.IsType<ScrollViewElement>(surface.RootElement.Children[0]);
        scroll.Controller.ScrollTo(1900);
        surface.UpdateLayout();
        int callbacks = 0;
        scroll.Controller.OnChanged = _ => callbacks++;
        fail = true;
        surface.SetViewport(new(100, 200));
        Assert.Throws<InvalidOperationException>(surface.UpdateLayout);
        Assert.Equal(1900, scroll.Controller.Offset);
        Assert.Equal(60, scroll.Controller.Viewport);
        Assert.Equal(0, callbacks);
    }

    /// <summary>Newly introduced nested owners must converge in one standard update boundary.</summary>
    [Fact]
    public void NestedOwnersRealizeWithoutRepeatedApplicationUpdates()
    {
        using var surface = new UiSurface(ListBox<int>(
            items: Enumerable.Range(0, 100).ToArray(),
            keySelector: item => item.ToString(System.Globalization.CultureInfo.InvariantCulture),
            itemTemplate: _ => ListBox<int>(
                items: Enumerable.Range(0, 100).ToArray(),
                keySelector: item => item.ToString(System.Globalization.CultureInfo.InvariantCulture),
                itemTemplate: _ => SizedBox(height: 20),
                height: 60,
                virtualization: new UiItemVirtualization(20, 0)),
            height: 120,
            virtualization: new UiItemVirtualization(60, 0)));
        surface.SetViewport(new(100, 120));
        surface.UpdateLayout();
        UiElement panel = surface.RootElement.Children[0].Children[0];
        Assert.Equal(2, panel.Children.Count);
        foreach (UiElement outerItem in panel.Children)
        {
            UiElement nested = outerItem.Children[0];
            Assert.Equal(3, nested.Children[0].Children[0].Children.Count);
        }
    }

    /// <summary>A newly discovered nested factory failure retains the outer tree and offset.</summary>
    [Fact]
    public void NestedFactoryFailureRetainsOuterPublication()
    {
        bool fail = false;
        using var surface = new UiSurface(ListBox<int>(
            items: Enumerable.Range(0, 100).ToArray(),
            keySelector: item => item.ToString(System.Globalization.CultureInfo.InvariantCulture),
            itemTemplate: _ => ListBox<int>(
                items: Enumerable.Range(0, 100).ToArray(),
                keySelector: item => item.ToString(System.Globalization.CultureInfo.InvariantCulture),
                itemTemplate: _ => fail ? throw new InvalidOperationException("nested factory") : SizedBox(height: 20),
                height: 60,
                virtualization: new UiItemVirtualization(20, 0)),
            height: 120,
            virtualization: new UiItemVirtualization(60, 0)));
        surface.SetViewport(new(100, 120));
        surface.UpdateLayout();
        UiElement first = surface.RootElement.Children[0].Children[0].Children[0];
        ScrollViewElement scroll = Assert.IsType<ScrollViewElement>(surface.RootElement.Children[0]);
        fail = true;
        scroll.Controller.ScrollTo(1000);
        Assert.Throws<InvalidOperationException>(surface.UpdateLayout);
        Assert.Same(first, surface.RootElement.Children[0].Children[0].Children[0]);
        Assert.Equal(0, scroll.Controller.Offset);
        Assert.Equal("0", first.Widget.Key);
    }

    /// <summary>Factory callbacks cannot observe speculative geometry or connection identities.</summary>
    [Fact]
    public void ResizeFactoryReadsAcceptedGeometry()
    {
        ScrollViewElement? owner = null;
        bool inspect = false;
        using var surface = new UiSurface(ListBox<int>(
            items: Enumerable.Range(0, 100).ToArray(),
            keySelector: item => item.ToString(System.Globalization.CultureInfo.InvariantCulture),
            itemTemplate: _ =>
            {
                if (inspect)
                {
                    Assert.Equal(60, owner!.Size.Y);
                    Assert.Equal(60, owner.Controller.Viewport);
                    Assert.Equal(1900, owner.Controller.Offset);
                    throw new InvalidOperationException("factory");
                }

                return SizedBox(height: 20);
            },
            height: (float?)null,
            virtualization: new UiItemVirtualization(20, 0)));
        surface.SetViewport(new(100, 60));
        surface.UpdateLayout();
        owner = Assert.IsType<ScrollViewElement>(surface.RootElement.Children[0]);
        owner.Controller.ScrollTo(1900);
        surface.UpdateLayout();
        inspect = true;
        surface.SetViewport(new(100, 200));
        Assert.Throws<InvalidOperationException>(surface.UpdateLayout);
        Assert.Equal(60, owner.Size.Y);
        Assert.Equal(60, owner.ContentBounds.Height);
    }

    /// <summary>Model replacement preserves the first visible key and whole-model selection silently.</summary>
    [Fact]
    public void ResetRestoresKeyAnchorBeforeRealization()
    {
        Compose.Definitions.ListBox<int> Create(int[] items) => ListBox<int>(items: items, keySelector: item => item.ToString(System.Globalization.CultureInfo.InvariantCulture), itemTemplate: _ => SizedBox(height: 20), selectedKey: "99", height: 60, virtualization: new UiItemVirtualization(20, 0));
        using var surface = new UiSurface(Create(Enumerable.Range(0, 100).ToArray()));
        surface.SetViewport(new(100, 60));
        surface.UpdateLayout();
        ScrollViewElement scroll = Assert.IsType<ScrollViewElement>(surface.RootElement.Children[0]);
        scroll.Controller.ScrollTo(1900);
        surface.UpdateLayout();
        UiAutomationId old = scroll.Children[0].Children[0].AutomationId;
        int callbacks = 0;
        scroll.Controller.OnChanged = _ => callbacks++;
        surface.Rebuild(Create(new[] { -2, -1 }.Concat(Enumerable.Range(0, 100)).ToArray()));
        surface.UpdateLayout();
        Assert.Equal(1940, scroll.Controller.Offset);
        Assert.Equal("95", scroll.Children[0].Children[0].Widget.Key);
        Assert.NotEqual(old, scroll.Children[0].Children[0].AutomationId);
        Assert.Equal("99", Assert.IsType<ListBoxElement<int>>(surface.RootElement).SelectedKey);
        Assert.Equal(0, callbacks);
    }

    /// <summary>A replacement that introduces a failing nested template cannot publish its new roots.</summary>
    [Fact]
    public void ResetNestedFactoryFailureKeepsOldTree()
    {
        bool fail = false;
        Compose.Definitions.ListBox<int> Create() => ListBox<int>(
            items: new[] { 0, 1 },
            keySelector: item => item.ToString(System.Globalization.CultureInfo.InvariantCulture),
            itemTemplate: _ => ListBox<int>(
                items: new[] { 0, 1, 2 },
                keySelector: item => item.ToString(System.Globalization.CultureInfo.InvariantCulture),
                itemTemplate: _ => fail ? throw new InvalidOperationException("nested factory") : SizedBox(height: 20),
                height: 60,
                virtualization: new UiItemVirtualization(20, 0)),
            height: 120,
            virtualization: new UiItemVirtualization(60, 0));
        using var surface = new UiSurface(Create());
        surface.SetViewport(new(100, 120));
        surface.UpdateLayout();
        UiElement old = surface.RootElement.Children[0].Children[0].Children[0];
        fail = true;
        surface.Rebuild(Create());
        Assert.Throws<InvalidOperationException>(surface.UpdateLayout);
        Assert.Same(old, surface.RootElement.Children[0].Children[0].Children[0]);
        Assert.True(old.IsMounted);
    }

    /// <summary>Unpublished nested range refresh retains the nearest inherited provider.</summary>
    [Fact]
    public void NestedRangeRefreshKeepsInheritedContext()
    {
        var context = new UiContextKey<string>();
        using var surface = new UiSurface(Provide(context, "provided", ListBox<int>(
            items: new[] { 0 },
            keySelector: item => item.ToString(System.Globalization.CultureInfo.InvariantCulture),
            itemTemplate: _ => ListBox<int>(
                items: new[] { 0, 1, 2 },
                keySelector: item => item.ToString(System.Globalization.CultureInfo.InvariantCulture),
                itemTemplate: _ => OwnershipFunctions.ReadContext(context),
                height: 60,
                virtualization: new UiItemVirtualization(20, 0)),
            height: 60,
            virtualization: new UiItemVirtualization(60, 0))));
        surface.SetViewport(new(100, 60));
        surface.UpdateLayout();
        Assert.Equal(3, surface.CaptureAutomationSnapshot().Nodes.Count(node => node.Name == "provided"));
    }

    /// <summary>All accepted viewport values precede notifications, and one callback failure cannot drop another.</summary>
    [Fact]
    public void SiblingNotificationsSeeCommittedValuesAndDrainAfterFailure()
    {
        Compose.Definitions.ListBox<int> Create() => ListBox<int>(items: Enumerable.Range(0, 100).ToArray(), keySelector: item => item.ToString(System.Globalization.CultureInfo.InvariantCulture), itemTemplate: _ => SizedBox(height: 20), height: 60, virtualization: new UiItemVirtualization(20, 0));
        using var surface = new UiSurface(Stack()[Create(), Create()]);
        surface.SetViewport(new(100, 120));
        surface.UpdateLayout();
        ScrollController first = Assert.IsType<ScrollViewElement>(surface.RootElement.Children[0].Children[0]).Controller;
        ScrollController second = Assert.IsType<ScrollViewElement>(surface.RootElement.Children[1].Children[0]).Controller;
        int notifications = 0;
        first.OnChanged = _ =>
        {
            Assert.Equal(1000, second.Offset);
            throw new InvalidOperationException("callback");
        };
        second.OnChanged = _ => notifications++;
        first.ScrollTo(1000);
        second.ScrollTo(1000);
        Assert.Throws<InvalidOperationException>(surface.UpdateLayout);
        Assert.Equal(1, notifications);
        Assert.Equal(1000, first.Offset);
        Assert.Equal(1000, second.Offset);
    }

    /// <summary>Reactive item replacement cannot publish outer rows before a nested factory fails.</summary>
    [Fact]
    public void SignalModelReplacementKeepsOldPublicationOnFailure()
    {
        bool fail = false;
        var items = Signal.Create<IReadOnlyList<int>>(new[] { 0 });
        using var surface = new UiSurface(ListBox<int>(
            items: items,
            keySelector: item => item.ToString(System.Globalization.CultureInfo.InvariantCulture),
            itemTemplate: _ => ListBox<int>(
                items: new[] { 0, 1, 2 },
                keySelector: item => item.ToString(System.Globalization.CultureInfo.InvariantCulture),
                itemTemplate: _ => fail ? throw new InvalidOperationException("factory") : SizedBox(height: 20),
                height: 60,
                virtualization: new UiItemVirtualization(20, 0)),
            height: 60,
            virtualization: new UiItemVirtualization(60, 0)));
        surface.SetViewport(new(100, 60));
        surface.UpdateLayout();
        UiElement old = surface.RootElement.Children[0].Children[0].Children[0];
        fail = true;
        items.Value = new[] { 1 };
        Assert.ThrowsAny<Exception>(surface.UpdateLayout);
        Assert.Same(old, surface.RootElement.Children[0].Children[0].Children[0]);
        Assert.True(old.IsMounted);
    }
}
