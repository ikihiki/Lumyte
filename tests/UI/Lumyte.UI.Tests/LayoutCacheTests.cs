using System.Numerics;
using Xunit;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Tests;

/// <summary>Checks successful layout reuse and invalidation without per-pass node objects.</summary>
public sealed class LayoutCacheTests
{
    /// <summary>Checks an unrelated dirty sibling does not execute a clean subtree's overrides.</summary>
    [Fact]
    public void DirtySiblingReusesTheWholeCleanSubtree()
    {
        var leaf = new Probe(new(10, 10));
        var branch = new Container(leaf);
        var changing = new Probe(new(10, 10));
        using var surface = new UiSurface(Panel()[branch, changing]);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        changing.Runtime.ChangeSize(new(20, 20));
        surface.UpdateLayout();
        Assert.Equal(1, branch.Runtime.Calls);
        Assert.Equal(1, leaf.Runtime.Calls);
        Assert.Equal(2, changing.Runtime.Calls);
        leaf.Runtime.ChangeSize(new(30, 30));
        surface.UpdateLayout();
        Assert.Equal(2, branch.Runtime.Calls);
        Assert.Equal(2, leaf.Runtime.Calls);
        Assert.Equal(new Vector2(30, 30), branch.Runtime.Size);
    }

    /// <summary>Checks effective rather than incoming constraints identify reusable geometry.</summary>
    [Fact]
    public void ChangedViewportReusesOnlyMatchingEffectiveConstraints()
    {
        var fixedSize = new Probe(new(10, 10)) { Width = 20, Height = 15 };
        var flexible = new Probe(new(300, 300));
        using var surface = new UiSurface(Panel()[fixedSize, flexible]);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        surface.SetViewport(new(200, 200));
        surface.UpdateLayout();
        Assert.Equal(1, fixedSize.Runtime.Calls);
        Assert.Equal(2, flexible.Runtime.Calls);
        Assert.Equal(new Vector2(20, 15), fixedSize.Runtime.Size);
        Assert.Equal(new Vector2(200, 200), flexible.Runtime.Size);
    }

    /// <summary>Checks parents still assign new positions to cached children.</summary>
    [Fact]
    public void StackRepositionsCachedChildrenAfterAnEarlierChildChangesSize()
    {
        var first = new Probe(new(10, 10));
        var second = new Probe(new(10, 10));
        using var surface = new UiSurface(Stack()[first, second]);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        first.Runtime.ChangeSize(new(10, 30));
        surface.UpdateLayout();
        Assert.Equal(1, second.Runtime.Calls);
        Assert.Equal(30, second.Runtime.Bounds.Y);
    }

    /// <summary>Checks zeroing hidden geometry does not leave reusable visible results behind.</summary>
    [Fact]
    public void HiddenDescendantsRecomputeWhenTheirAncestorReturns()
    {
        var leaf = new Probe(new(10, 10));
        using var surface = new UiSurface(Panel()[Panel()[leaf]]);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        ProbeElement instance = leaf.Runtime;
        surface.Rebuild(Panel()[Panel(isVisible: false)[leaf]]);
        surface.UpdateLayout();
        Assert.Same(instance, leaf.Runtime);
        Assert.Equal(Vector2.Zero, instance.Size);
        surface.Rebuild(Panel()[Panel()[leaf]]);
        surface.UpdateLayout();
        Assert.Equal(2, instance.Calls);
        Assert.Equal(new Vector2(10, 10), instance.Size);
    }

    /// <summary>Checks cached child sizes still obey the once-per-pass protocol and failures invalidate reuse.</summary>
    /// <param name="mode">The invalid custom layout operation.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void InvalidCachedChildUseFailsAndTheNextPassRecomputes(int mode)
    {
        var leaf = new Probe(new(10, 10));
        var root = new Container(leaf);
        using var surface = new UiSurface(root);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        root.Runtime.ChangeMode(mode);
        Assert.Throws<InvalidOperationException>(surface.UpdateLayout);
        Assert.Equal(1, leaf.Runtime.Calls);
        root.Runtime.ChangeMode(0);
        surface.UpdateLayout();
        Assert.Equal(2, leaf.Runtime.Calls);
        Assert.Equal(new Vector2(10, 10), leaf.Runtime.Size);
    }

    /// <summary>Checks mounted instances of one declaration do not share cached geometry or pass bookkeeping.</summary>
    [Fact]
    public void SharedDeclarationsKeepIndependentLayoutCaches()
    {
        var leaf = new Probe(new(100, 100));
        using var first = new UiSurface(leaf);
        using var second = new UiSurface(leaf);
        first.SetViewport(new(10, 20));
        second.SetViewport(new(30, 40));
        first.UpdateLayout();
        second.UpdateLayout();
        first.SetViewport(new(50, 60));
        first.UpdateLayout();
        Assert.Equal(new Vector2(50, 60), first.RootElement.Size);
        Assert.Equal(new Vector2(30, 40), second.RootElement.Size);
        Assert.Equal(2, ((ProbeElement)first.RootElement).Calls);
        Assert.Equal(1, ((ProbeElement)second.RootElement).Calls);
    }

    private sealed class Probe(Vector2 desired) : Widget
    {
        internal Vector2 Desired { get; } = desired;

        internal ProbeElement Runtime { get; private set; } = null!;

        protected override UiElement CreateElement() => Runtime = new(this);
    }

    private sealed class ProbeElement(Probe widget) : UiElement<Probe>(widget)
    {
        private Vector2 _desired = widget.Desired;

        internal int Calls { get; private set; }

        internal void ChangeSize(Vector2 desired)
        {
            _desired = desired;
            InvalidateLayout();
        }

        protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
        {
            Calls++;
            return constraints.Constrain(_desired);
        }
    }

    private sealed class Container : Widget
    {
        internal Container(Widget child)
        {
            Content = CreateChildren();
            Content.Initialize(child);
        }

        internal WidgetChildren Content { get; }

        internal ContainerElement Runtime { get; private set; } = null!;

        protected override UiElement CreateElement() => Runtime = new(this);
    }

    private sealed class ContainerElement(Container widget) : UiElement<Container>(widget)
    {
        private int _mode;

        internal int Calls { get; private set; }

        internal void ChangeMode(int mode)
        {
            _mode = mode;
            InvalidateLayout();
        }

        protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
        {
            Calls++;
            UiElement child = GetChildren(Widget.Content)[0];
            Vector2 size = LayoutChild(child, constraints.Loosen());
            if (_mode == 1)
            {
                LayoutChild(child, constraints.Loosen());
            }

            if (_mode != 3)
            {
                SetChildOffset(child, Vector2.Zero);
            }

            if (_mode == 2)
            {
                SetChildOffset(child, Vector2.Zero);
            }

            if (_mode == 4)
            {
                throw new InvalidOperationException("Parent failed after reading a cached child.");
            }

            return constraints.Constrain(size);
        }
    }
}
