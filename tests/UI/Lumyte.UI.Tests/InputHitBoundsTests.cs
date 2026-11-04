using System.Numerics;
using Xunit;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Tests;

/// <summary>Checks subtree bounds preserve hit testing with overflow, clipping and runtime input changes.</summary>
public sealed class InputHitBoundsTests
{
    /// <summary>Checks a zero-area parent still includes overflowing children on either side of its origin.</summary>
    /// <param name="offset">The child's horizontal offset.</param>
    [Theory]
    [InlineData(-20f)]
    [InlineData(20f)]
    public void ZeroAreaParentPreservesChildOverflow(float offset)
    {
        var leaf = new Target { Width = 10, Height = 10 };
        var inner = new OffsetPanel(new(offset, 0)) { Width = 0, Height = 0 };
        inner.Children.Initialize(leaf);
        var outer = new OffsetPanel(new(40, 20));
        outer.Children.Initialize(inner);
        using UiSurface surface = Surface(outer);
        Assert.True(Down(surface, new(41 + offset, 21)));
        Assert.Equal(1, leaf.Hits);
        Assert.False(Down(surface, new(40 + offset + 10, 21)));
    }

    /// <summary>Checks a clipped overflow cannot hide a separate valid sibling in the aggregate bounds.</summary>
    [Fact]
    public void EmptyClippedRegionDoesNotExcludeValidSibling()
    {
        var clipped = new Target { Width = 10, Height = 10 };
        var clipper = new OffsetPanel(new(50, 0)) { Width = 5, Height = 5, ClipToBounds = true };
        clipper.Children.Initialize(clipped);
        var valid = new Target { Width = 10, Height = 10 };
        var parent = new OffsetPanel(new(20, 20)) { Width = 0, Height = 0 };
        parent.Children.Initialize(clipper, valid);
        using UiSurface surface = Surface(Panel()[parent]);
        Assert.True(Down(surface, new(21, 21)));
        Assert.Equal(1, valid.Hits);
        Assert.False(Down(surface, new(71, 21)));
        Assert.Equal(0, clipped.Hits);
    }

    /// <summary>Checks moving a nested child updates both old and new ancestor bounds before subsequent input.</summary>
    [Fact]
    public void RuntimeLayoutRebuildsTheSubtreeBounds()
    {
        var leaf = new Target { Width = 10, Height = 10 };
        var parent = new OffsetPanel(new(20, 0)) { Width = 0, Height = 0 };
        parent.Children.Initialize(leaf);
        using UiSurface surface = Surface(Panel()[parent]);
        Assert.True(Down(surface, new(21, 1)));
        parent.MoveTo(new(50, 0));
        surface.UpdateLayout();
        Assert.False(Down(surface, new(21, 1)));
        Assert.True(Down(surface, new(51, 1)));
        Assert.Equal(2, leaf.Hits);
    }

    /// <summary>Checks frontmost blockers and child input selection survive aggregate-bound changes without relayout.</summary>
    [Fact]
    public void InputSelectionChangesPreserveFrontToBackBlocking()
    {
        var behind = new Target { Width = 10, Height = 10 };
        var blocker = new Definitions.Panel { Width = 10, Height = 10 };
        var parent = new OffsetPanel(new(30, 30)) { Width = 0, Height = 0 };
        parent.Children.Initialize(behind, blocker);
        using UiSurface surface = Surface(Panel()[parent]);
        Assert.False(Down(surface, new(31, 31)));
        parent.Select(behind);
        surface.UpdateLayout();
        Assert.True(Down(surface, new(31, 31)));
        parent.Select();
        surface.UpdateLayout();
        Assert.False(Down(surface, new(31, 31)));
        Assert.Equal(1, behind.Hits);
    }

    private static UiSurface Surface(Widget root)
    {
        var surface = new UiSurface(root);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        return surface;
    }

    private static bool Down(UiSurface surface, Vector2 position) => surface.Dispatch(new UiPointerInput(0, UiPointerPhase.Down, position));

    private sealed class Target : Definitions.Widget
    {
        public TargetElement Runtime { get; private set; } = null!;

        public int Hits => Runtime.Hits;

        protected override UiElement CreateElement() => Runtime = new(this);
    }

    private sealed class TargetElement(Target widget) : UiElement<Target>(widget)
    {
        public int Hits { get; private set; }

        protected override UiInputResult OnInput(in UiInputContext context)
        {
            if (context.Input is UiPointerInput { Phase: UiPointerPhase.Down })
            {
                Hits++;
                return UiInputResult.Handled;
            }

            return UiInputResult.Unhandled;
        }
    }

    private sealed class OffsetPanel(Vector2 offset) : Definitions.Panel
    {
        public Vector2 Offset { get; private set; } = offset;

        public Widget[]? Selection { get; private set; }

        public OffsetElement? Runtime { get; private set; }

        public void MoveTo(Vector2 position)
        {
            Offset = position;
            Runtime?.RefreshLayout();
        }

        public void Select(params Widget[] children)
        {
            Selection = children;
            Runtime?.RefreshInput();
        }

        protected override UiElement CreateElement() => Runtime = new(this);
    }

    private sealed class OffsetElement(OffsetPanel widget) : UiElement<OffsetPanel>(widget)
    {
        public void RefreshLayout() => InvalidateLayout();

        public void RefreshInput() => InvalidateInput();

        protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
        {
            foreach (UiElement child in GetChildren(Widget.Children))
            {
                LayoutChild(child, new UiBoxConstraints(0, float.PositiveInfinity, 0, float.PositiveInfinity));
                SetChildOffset(child, Widget.Offset);
            }

            return constraints.Constrain(Vector2.Zero);
        }

        protected override IEnumerable<UiElement> GetInputChildren() => Widget.Selection is { } selection
            ? selection.Select(child => Children.FirstOrDefault(element => ReferenceEquals(element.Widget, child)) ?? child.CreateInstance())
            : base.GetInputChildren();
    }
}
