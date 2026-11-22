using System.Numerics;
using Xunit;
using static Lumyte.UI.Collections.Compose;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Collections.Tests;

/// <summary>Publication exceptions retain accepted geometry and lifecycle invalidations remain pending.</summary>
public sealed class VirtualizationLifecycleTests
{
    /// <summary>Exceptions after publication cannot restore geometry belonging to the removed range.</summary>
    [Fact]
    public void MountedExceptionKeepsAcceptedNewGeometry()
    {
        using var surface = new UiSurface(ListBox<int>(items: Enumerable.Range(0, 1000).ToArray(), keySelector: item => item.ToString(System.Globalization.CultureInfo.InvariantCulture), itemTemplate: item => Stack()[new LifecycleRow(item >= 50, false)], height: 60, virtualization: new UiItemVirtualization(20, 0)));
        surface.SetViewport(new(100, 60));
        surface.UpdateLayout();
        ScrollViewElement scroll = Assert.IsType<ScrollViewElement>(surface.RootElement.Children[0]);
        int notifications = 0;
        scroll.Controller.OnChanged = _ => notifications++;
        scroll.Controller.ScrollTo(1000);
        Assert.Throws<InvalidOperationException>(surface.UpdateLayout);
        Assert.Equal(1, notifications);
        Assert.Equal(1000, scroll.Controller.Offset);
        Assert.Equal("50", scroll.Children[0].Children[0].Widget.Key);
        UiElement row = scroll.Children[0].Children[0].Children[0].Children[0];
        Assert.True(row.IsMounted);
        Assert.Equal(10, row.Size.Y);
        Assert.NotEqual(default, row.AutomationId);
    }

    /// <summary>State changed by OnMounted must invalidate the already prepared layout.</summary>
    [Fact]
    public void MountedInvalidationSurvivesGeometryAcceptance()
    {
        using var surface = new UiSurface(ListBox<int>(items: new[] { 0 }, keySelector: _ => "a", itemTemplate: _ => Stack()[new LifecycleRow(false, true)], height: 60, virtualization: new UiItemVirtualization(40, 0)));
        surface.SetViewport(new(100, 60));
        surface.UpdateLayout();
        surface.UpdateLayout();
        UiElement row = surface.RootElement.Children[0].Children[0].Children[0].Children[0].Children[0];
        Assert.Equal(20, row.Size.Y);
    }

    /// <summary>A selection callback may queue a new model but cannot rebuild the owned tree inside dispatch.</summary>
    [Fact]
    public void SelectionCallbackQueuesModelAtOwnerBoundary()
    {
        UiSurface? surface = null;
        int changes = 0;
        using (surface = new UiSurface(ListBox<int>(
            items: new[] { 0, 1, 2 },
            keySelector: item => item.ToString(System.Globalization.CultureInfo.InvariantCulture),
            itemTemplate: _ => SizedBox(height: 20),
            height: 60,
            virtualization: new UiItemVirtualization(20, 0),
            onSelectionChanged: _ =>
            {
                changes++;
                surface!.Rebuild(ListBox<int>(items: new[] { 5, 6 }, keySelector: item => item.ToString(System.Globalization.CultureInfo.InvariantCulture), itemTemplate: _ => SizedBox(height: 20), height: 60, virtualization: new UiItemVirtualization(20, 0)));
                surface.UpdateLayout();
            })))
        {
            surface.SetViewport(new(100, 60));
            surface.UpdateLayout();
            UiElement old = surface.RootElement.Children[0].Children[0].Children[0];
            surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(10, 10)));
            surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(10, 10)));
            Assert.Equal(1, changes);
            Assert.True(old.IsMounted);
            surface.UpdateLayout();
            Assert.False(old.IsMounted);
            Assert.Equal(5, Assert.IsType<ListBoxElement<int>>(surface.RootElement).Items[0]);
        }
    }

    /// <summary>A throwing parent publication callback cannot skip a child's mandatory model adoption.</summary>
    [Fact]
    public void ThrowingParentCommitStillAdoptsChildModel()
    {
        ThrowingParent Create(bool fail, int[] items)
        {
            var parent = new ThrowingParent { Fail = fail };
            parent.Children.Initialize(ListBox<int>(items: items, keySelector: item => item.ToString(System.Globalization.CultureInfo.InvariantCulture), itemTemplate: _ => SizedBox(height: 20), height: 60, virtualization: new UiItemVirtualization(20, 0)));
            return parent;
        }

        using var surface = new UiSurface(Create(false, new[] { 0, 1 }));
        surface.SetViewport(new(100, 60));
        surface.UpdateLayout();
        surface.Rebuild(Create(true, new[] { 5, 6 }));
        Assert.Throws<InvalidOperationException>(surface.UpdateLayout);
        ListBoxElement<int> list = Assert.IsType<ListBoxElement<int>>(surface.RootElement.Children[0]);
        Assert.Equal(new[] { 5, 6 }, list.Items);
        Assert.True(list.BringIntoView("5"));
        Assert.False(list.BringIntoView("0"));
    }

    private sealed class ThrowingParent : Lumyte.UI.Compose.Definitions.Panel
    {
        internal bool Fail { get; init; }

        protected override UiElement CreateElement() => new ParentElement(this);

        private sealed class ParentElement(ThrowingParent widget) : UiElement<ThrowingParent>(widget)
        {
            protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
            {
                Vector2 size = LayoutChild(Children[0], constraints);
                SetChildOffset(Children[0], Vector2.Zero);
                return constraints.Constrain(size);
            }

            protected override Action? PrepareUpdate(ThrowingParent next, UiElementUpdateContext context)
            {
                if (!next.Fail)
                {
                    return null;
                }

                return () =>
                {
                    Assert.Equal(5, Assert.IsType<ListBoxElement<int>>(Children[0]).Items[0]);
                    throw new InvalidOperationException("parent commit");
                };
            }
        }
    }

    private sealed class LifecycleRow(bool throwOnMount, bool resizeOnMount) : Widget
    {
        internal bool ThrowOnMount { get; } = throwOnMount;

        internal bool ResizeOnMount { get; } = resizeOnMount;

        protected override UiElement CreateElement() => new RowElement(this);

        private sealed class RowElement(LifecycleRow widget) : UiElement<LifecycleRow>(widget)
        {
            private float _height = 10;

            protected override Vector2 LayoutOverride(UiBoxConstraints constraints) => constraints.Constrain(new(10, _height));

            protected override void OnMounted()
            {
                if (Widget.ResizeOnMount)
                {
                    _height = 20;
                    InvalidateLayout();
                }

                if (Widget.ThrowOnMount)
                {
                    throw new InvalidOperationException("mounted callback");
                }
            }
        }
    }
}
