using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Xunit;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Tests;

/// <summary>Tests one-way layout, generated parameters and transactional geometry publication.</summary>
public sealed class LayoutTests
{
    /// <summary>Checks components control layout order independently of child collection registration order.</summary>
    [Fact]
    public void CustomGroupsParticipateInComponentDefinedLayout()
    {
        var order = new List<string>();
        var embedded = new Leaf(new(10, 20)) { DuringLayout = () => order.Add("embedded") };
        var foreground = new Leaf(new(30, 10)) { DuringLayout = () => order.Add("foreground") };
        var root = new GroupedWidget();
        root.Embeddeds.Initialize(embedded);
        root.Foreground.Initialize(foreground);
        using var surface = new UiSurface(root);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        Assert.Equal(new[] { "foreground", "embedded" }, order);
        Assert.Equal(new Rect2D(0, 0, 10, 20), embedded.Bounds);
        Assert.Equal(new Rect2D(0, 0, 30, 10), foreground.Bounds);
        surface.Dispose();
        root.IsVisible = false;
        using var hiddenSurface = new UiSurface(root);
        hiddenSurface.UpdateLayout();
        Assert.Equal(Vector2.Zero, embedded.Size);
        Assert.Equal(Vector2.Zero, foreground.Size);
        Assert.Equal(2, order.Count);
    }

    /// <summary>Checks every registered group must be handled, including custom properties unknown to the base widget.</summary>
    [Fact]
    public void LayoutRejectsAnOmittedCustomGroup()
    {
        var root = new GroupedWidget { SkipEmbeddeds = true };
        var child = new Leaf(new(10, 20));
        root.Embeddeds.Initialize(child);
        using var surface = new UiSurface(root);
        surface.SetViewport(new(100, 100));
        Assert.Throws<InvalidOperationException>(surface.UpdateLayout);
        Assert.Equal(default, child.Bounds);
        root.SkipEmbeddeds = false;
        surface.UpdateLayout();
        Assert.Equal(new Vector2(10, 20), child.Size);
    }

    /// <summary>Checks declaration setters do not reject calls based on the surface's active pass.</summary>
    [Fact]
    public void DeclarationSettersDoNotInspectTheLayoutPhase()
    {
        var leaf = new Leaf(new(10, 10));
        Definitions.Stack stack = Stack()[leaf];
        var root = new ProtocolPanel();
        root.Children.Initialize(stack);
        root.Compute = (owner, constraints) =>
        {
            owner.Width = null;
            owner.Height = null;
            owner.IsVisible = true;
            stack.Orientation = UiOrientation.Vertical;
            stack.Spacing = 0;
            owner.RunChild(stack, constraints.Loosen());
            owner.OffsetChild(stack, Vector2.Zero);
            return constraints.Constrain(Vector2.Zero);
        };
        using var surface = new UiSurface(root);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        Assert.Equal(new Vector2(10, 10), leaf.Size);
    }

    /// <summary>Tests root constraints and the absence of implicit layout in property getters.</summary>
    [Fact]
    public void RootUsesTheViewportAndGeometryGettersDoNotRunLayout()
    {
        var leaf = new Leaf(new(20, 30)) { Width = 160 };
        using var surface = new UiSurface(leaf);
        surface.SetViewport(new(800, 600));
        Assert.Equal(Vector2.Zero, leaf.Size);
        Assert.Equal(default, leaf.Bounds);
        Assert.Equal(0, leaf.Calls);

        surface.UpdateLayout();
        Assert.Equal(new Vector2(800, 600), leaf.Size);
        Assert.Equal(new Rect2D(0, 0, 800, 600), leaf.Bounds);
        Assert.Equal(UiBoxConstraints.Tight(new(800, 600)), leaf.LastConstraints);
        Assert.Equal(1, leaf.Calls);
    }

    /// <summary>Tests generated inherited parameters and the typed Stack content indexer.</summary>
    [Fact]
    public void GeneratedFactoriesExposeInheritedDimensionsAndTypedStackContent()
    {
        Definitions.Panel child = Panel(width: 30, height: 20);
        Definitions.Stack stack = Stack(orientation: UiOrientation.Horizontal, spacing: 7, width: 50)[child];
        Definitions.Panel root = Panel()[stack];
        using var surface = new UiSurface(root);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();

        Assert.Equal(new Vector2(50, 100), stack.Size);
        Assert.Equal(new Vector2(30, 20), child.Size);
        Assert.Equal(UiOrientation.Horizontal, stack.Orientation);
        Assert.Equal(7, stack.Spacing);
    }

    /// <summary>Tests an intrinsic leaf's constrained zero implementation.</summary>
    [Fact]
    public void BaseLeafUsesTheConstrainedZeroSize()
    {
        var child = new EmptyLeaf { Width = 12, Height = 7 };
        Definitions.Panel root = Panel()[child];
        using var surface = new UiSurface(root);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        Assert.Equal(new Vector2(12, 7), child.Size);
    }

    /// <summary>Tests overflow without resizing the second child into the remaining space.</summary>
    /// <param name="orientation">The main axis.</param>
    [Theory]
    [InlineData(UiOrientation.Vertical)]
    [InlineData(UiOrientation.Horizontal)]
    public void StackPreservesChildSizesWhenItsContentsOverflow(UiOrientation orientation)
    {
        var first = new Leaf(new(80, 80));
        var second = new Leaf(new(80, 80));
        Definitions.Stack root = Stack(orientation: orientation)[first, second];
        using var surface = new UiSurface(root);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();

        Assert.Equal(new Vector2(100, 100), root.Size);
        Assert.Equal(new Vector2(80, 80), second.Size);
        Assert.Equal(orientation == UiOrientation.Vertical ? new Rect2D(0, 80, 80, 80) : new Rect2D(80, 0, 80, 80), second.Bounds);
        Assert.Equal(1, first.Calls);
        Assert.Equal(1, second.Calls);
    }

    /// <summary>Tests gaps only between visible children and zero geometry for a hidden subtree.</summary>
    [Fact]
    public void StackSpacingSkipsHiddenChildrenAndZerosTheirDescendants()
    {
        var first = new Leaf(new(10, 20));
        var descendant = new Leaf(new(9, 9));
        Definitions.Panel hidden = Panel()[descendant];
        var last = new Leaf(new(10, 30));
        Definitions.Stack root = Stack(spacing: 5)[first, hidden, last];
        hidden.IsVisible = false;
        using var surface = new UiSurface(root);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        Assert.Equal(new Rect2D(0, 25, 10, 30), last.Bounds);
        Assert.Equal(Vector2.Zero, hidden.Size);
        Assert.Equal(default, hidden.Bounds);
        Assert.Equal(Vector2.Zero, descendant.Size);
        Assert.Equal(default, descendant.Bounds);
        Assert.Equal(0, descendant.Calls);
    }

    /// <summary>Tests a build function produces independent visible and hidden declarations.</summary>
    [Fact]
    public void RebuiltRootUsesItsDeclaredVisibility()
    {
        static Definitions.Panel Build(bool visible) => Panel(isVisible: visible)[new Leaf(new(10, 10))];

        Definitions.Panel hidden = Build(false);
        Leaf hiddenLeaf = Assert.IsType<Leaf>(Assert.Single(hidden.Children));
        hiddenLeaf.DuringLayout = () => throw new InvalidOperationException("Hidden override ran.");
        using var hiddenSurface = new UiSurface(hidden);
        hiddenSurface.SetViewport(new(100, 100));
        hiddenSurface.UpdateLayout();
        Assert.Equal(Vector2.Zero, hidden.Size);
        Assert.Equal(Vector2.Zero, hiddenLeaf.Size);
        Assert.Equal(0, hiddenLeaf.Calls);

        Definitions.Panel visible = Build(true);
        Leaf visibleLeaf = Assert.IsType<Leaf>(Assert.Single(visible.Children));
        using var visibleSurface = new UiSurface(visible);
        visibleSurface.SetViewport(new(100, 100));
        visibleSurface.UpdateLayout();
        Assert.Equal(new Vector2(100, 100), visible.Size);
        Assert.Equal(new Vector2(10, 10), visibleLeaf.Size);
        Assert.Equal(1, visibleLeaf.Calls);
        Assert.Equal(Vector2.Zero, hidden.Size);
    }

    /// <summary>Tests bounded filling and unbounded content sizing in Panel.</summary>
    [Fact]
    public void PanelFillsBoundedAxesAndUsesContentOnUnboundedAxes()
    {
        var leaf = new Leaf(new(20, 4));
        Definitions.Panel panel = Panel()[leaf];
        var root = new ProtocolPanel();
        root.Children.Initialize([panel]);
        root.Compute = (owner, constraints) =>
        {
            owner.RunChild(panel, new(5, float.PositiveInfinity, 7, 100));
            owner.OffsetChild(panel, Vector2.Zero);
            return constraints.Constrain(Vector2.Zero);
        };
        using var surface = new UiSurface(root);
        surface.UpdateLayout();
        Assert.Equal(new Vector2(20, 100), panel.Size);
        Assert.Equal(new Vector2(20, 4), leaf.Size);
        Assert.Equal(default, new EmptyLeaf().Size);
    }

    /// <summary>Tests that Button computes content first and gives its final tight size to each background.</summary>
    [Fact]
    public void ButtonLaysOutContentThenTightBackgrounds()
    {
        var order = new List<string>();
        var content = new Leaf(new(32, 12)) { DuringLayout = () => order.Add("content") };
        var first = new Leaf(new(1, 1)) { Width = 500, DuringLayout = () => order.Add("first") };
        var second = new Leaf(new(2, 2)) { DuringLayout = () => order.Add("second") };
        Definitions.Button button = Button()[content, Button.Background()[first, second]];
        Definitions.Panel root = Panel()[button];
        using var surface = new UiSurface(root);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();

        Assert.Equal(new[] { "content", "first", "second" }, order);
        Assert.Equal(new Vector2(32, 12), button.Size);
        Assert.Equal(UiBoxConstraints.Tight(button.Size), first.LastConstraints);
        Assert.Equal(button.Size, second.Size);
        Assert.Equal(new Rect2D(0, 0, 32, 12), first.Bounds);
        Assert.Equal(1, content.Calls);
    }

    /// <summary>Tests Button's empty content rule, even when backgrounds request a size.</summary>
    [Fact]
    public void EmptyButtonDoesNotTakeItsSizeFromTheBackground()
    {
        Definitions.Button button = Button()[Button.Background()[Panel(width: 90, height: 90)]];
        Definitions.Panel root = Panel()[button];
        using var surface = new UiSurface(root);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        Assert.Equal(Vector2.Zero, button.Size);
        Assert.Equal(Vector2.Zero, Assert.Single(button.BackgroundChildren).Size);
    }

    /// <summary>Tests a width-dependent height computed once using the actual incoming width.</summary>
    [Fact]
    public void WidthDependentHeightIsComputedWithTheFinalWidth()
    {
        var text = new WrappingLeaf { Width = 40 };
        Definitions.Panel root = Panel()[text];
        using var surface = new UiSurface(root);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        Assert.Equal(new Vector2(40, 30), text.Size);
        Assert.Equal(1, text.Calls);
    }

    /// <summary>Tests clean passes, same-value properties and rejected repeat content initialization.</summary>
    [Fact]
    public void UnchangedInputsDoNotRepeatLayout()
    {
        var leaf = new Leaf(new(10, 10));
        Definitions.Stack root = Stack(spacing: 3)[leaf];
        using var surface = new UiSurface(root);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();

        surface.UpdateLayout();
        root.Spacing = 3;
        root.Orientation = UiOrientation.Vertical;
        leaf.Width = null;
        leaf.Height = null;
        leaf.IsVisible = true;
        Assert.Throws<InvalidOperationException>(() => root.Children.Initialize(leaf));
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        Assert.Equal(1, leaf.Calls);

        leaf.UpdatePreferredSize(new(20, 20));
        surface.UpdateLayout();
        Assert.Equal(new Vector2(20, 20), leaf.Size);
        Assert.Equal(2, leaf.Calls);
    }

    /// <summary>Tests declaration setters do not schedule another layout on an already published tree.</summary>
    [Fact]
    public void DeclarationSettersDoNotInvalidatePublishedLayout()
    {
        var first = new Leaf(new(10, 10));
        var second = new Leaf(new(10, 20));
        Definitions.Stack root = Stack()[first, second];
        using var surface = new UiSurface(root);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        first.Width = 40;
        first.Height = 30;
        root.Spacing = 2;
        root.Orientation = UiOrientation.Horizontal;
        first.IsVisible = false;
        surface.UpdateLayout();
        Assert.Equal(1, first.Calls);
        Assert.Equal(1, second.Calls);
        Assert.Equal(new Vector2(10, 10), first.Size);
        Assert.Equal(new Rect2D(0, 10, 10, 20), second.Bounds);

        surface.SetViewport(new(200, 50));
        surface.UpdateLayout();
        Assert.Equal(new Vector2(200, 50), root.Size);
        Assert.Equal(2, second.Calls);
    }

    /// <summary>Tests rebuilding dimensions and container settings leaves the previous tree's geometry unchanged.</summary>
    [Fact]
    public void BuildFunctionCreatesNewLayoutInputs()
    {
        static Definitions.Stack Build(float height, float spacing, UiOrientation orientation) =>
            Stack(spacing: spacing, orientation: orientation)[Panel(width: 10, height: height), Panel(width: 10, height: 20)];

        Definitions.Stack previous = Build(10, 0, UiOrientation.Vertical);
        using var previousSurface = new UiSurface(previous);
        previousSurface.SetViewport(new(100, 100));
        previousSurface.UpdateLayout();
        Definitions.Stack next = Build(30, 2, UiOrientation.Horizontal);
        using var nextSurface = new UiSurface(next);
        nextSurface.SetViewport(new(100, 100));
        nextSurface.UpdateLayout();
        Assert.Equal(new Rect2D(0, 10, 10, 20), previous.Children[1].Bounds);
        Assert.Equal(new Rect2D(12, 0, 10, 20), next.Children[1].Bounds);
        Assert.Equal(new Vector2(10, 30), next.Children[0].Size);
        Assert.NotSame(previous.Children[0], next.Children[0]);
    }

    /// <summary>Tests that a layout exception aborts traversal and leaves earlier results in place.</summary>
    [Fact]
    public void FailedPassAbortsWithoutRollingBackEarlierGeometry()
    {
        var first = new Leaf(new(10, 10));
        var second = new Leaf(new(10, 20));
        Definitions.Stack root = Stack()[first, second];
        using var surface = new UiSurface(root);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        Rect2D oldSecond = second.Bounds;
        first.UpdatePreferredSize(new(30, 40));
        surface.SetViewport(new(200, 200));
        second.DuringLayout = () =>
        {
            Assert.Equal(new Vector2(30, 40), first.Size);
            throw new InvalidOperationException("fixture");
        };

        Assert.Throws<InvalidOperationException>(surface.UpdateLayout);
        Assert.Equal(new Vector2(100, 100), root.Size);
        Assert.Equal(new Vector2(30, 40), first.Size);
        Assert.Equal(oldSecond, second.Bounds);

        second.DuringLayout = null;
        surface.UpdateLayout();
        Assert.Equal(new Vector2(200, 200), root.Size);
        Assert.Equal(new Rect2D(0, 40, 10, 20), second.Bounds);
    }

    /// <summary>Tests rejection of invalid custom sizes without clamping them silently.</summary>
    /// <param name="width">The invalid width.</param>
    /// <param name="height">The invalid height.</param>
    [Theory]
    [InlineData(-1f, 10f)]
    [InlineData(float.NaN, 10f)]
    [InlineData(float.PositiveInfinity, 10f)]
    [InlineData(10f, float.NegativeInfinity)]
    [InlineData(101f, 10f)]
    public void InvalidCustomSizesDoNotPublish(float width, float height)
    {
        var leaf = new Leaf(new(10, 10)) { RawResult = new(width, height) };
        Definitions.Panel root = Panel()[leaf];
        using var surface = new UiSurface(root);
        surface.SetViewport(new(100, 100));
        Assert.Throws<InvalidOperationException>(surface.UpdateLayout);
        Assert.Equal(default, leaf.Bounds);
        leaf.RawResult = null;
        surface.UpdateLayout();
        Assert.Equal(new Vector2(10, 10), leaf.Size);
    }

    /// <summary>Tests invalid child protocol operations, including a swallowed duplicate call.</summary>
    /// <param name="failure">The protocol violation.</param>
    [Theory]
    [InlineData("missing-layout")]
    [InlineData("missing-offset")]
    [InlineData("early-offset")]
    [InlineData("duplicate-layout")]
    [InlineData("duplicate-offset")]
    [InlineData("foreign-child")]
    [InlineData("swallowed-duplicate")]
    public void InvalidChildProtocolFailsTheWholePass(string failure)
    {
        var leaf = new Leaf(new(10, 10));
        var root = new ProtocolPanel();
        root.Children.Initialize(leaf);
        root.Compute = (owner, constraints) =>
        {
            if (failure == "missing-layout")
            {
                return constraints.Constrain(Vector2.Zero);
            }

            if (failure == "early-offset")
            {
                owner.OffsetChild(leaf, Vector2.Zero);
            }

            if (failure == "foreign-child")
            {
                owner.RunChild(new EmptyLeaf(), constraints);
            }

            owner.RunChild(leaf, constraints.Loosen());
            if (failure == "missing-offset")
            {
                return constraints.Constrain(Vector2.Zero);
            }

            owner.OffsetChild(leaf, Vector2.Zero);
            if (failure is "duplicate-layout" or "swallowed-duplicate")
            {
                try
                {
                    owner.RunChild(leaf, constraints);
                }
                catch (InvalidOperationException) when (failure == "swallowed-duplicate")
                {
                }
            }

            if (failure == "duplicate-offset")
            {
                owner.OffsetChild(leaf, Vector2.Zero);
            }

            return constraints.Constrain(Vector2.Zero);
        };
        using var surface = new UiSurface(root);
        surface.SetViewport(new(100, 100));
        Assert.Throws<InvalidOperationException>(surface.UpdateLayout);
        root.Compute = null;
        surface.UpdateLayout();
        Assert.Equal(new Vector2(10, 10), leaf.Size);
    }

    /// <summary>Tests that sequential worker updates and layout do not introduce thread affinity.</summary>
    /// <returns>A task representing sequential layout across worker tasks.</returns>
    [Fact]
    public async Task LayoutCanMoveAcrossThreadsSequentiallyAsync()
    {
        var leaf = new Leaf(new(10, 10));
        Definitions.Panel root = Panel()[leaf];
        using var surface = new UiSurface(root);
        await Task.Run(() =>
        {
            surface.SetViewport(new(100, 100));
            surface.UpdateLayout();
        });
        leaf.UpdatePreferredSize(new(10, 30));
        await Task.Run(surface.UpdateLayout);
        Assert.Equal(new Vector2(10, 30), leaf.Size);
    }

    /// <summary>Tests finite double accumulation with bounded size and overflowing offsets.</summary>
    [Fact]
    public void BoundedSizeCanClampLargeContentButOffsetsMustRemainFinite()
    {
        var first = new Leaf(new(1, float.MaxValue));
        var second = new Leaf(new(1, float.MaxValue));
        Definitions.Stack root = Stack()[first, second];
        using var surface = new UiSurface(root);
        surface.SetViewport(new(10, 10));
        surface.UpdateLayout();
        Assert.Equal(new Vector2(10, 10), root.Size);
        Assert.Equal(float.MaxValue, second.Bounds.Y);
        using var overflow = new UiSurface(Stack()[new Leaf(new(1, float.MaxValue)), new Leaf(new(1, float.MaxValue)), new Leaf(new(1, 1))]);
        overflow.SetViewport(new(10, 10));
        Assert.Throws<InvalidOperationException>(overflow.UpdateLayout);
        Assert.Equal(float.MaxValue, second.Bounds.Y);
    }

    /// <summary>Tests disposed surface methods and disconnected helper calls.</summary>
    [Fact]
    public void LayoutOperationsValidateTheirLifecycle()
    {
        var root = new ProtocolPanel();
        Assert.Throws<InvalidOperationException>(() => root.RunChild(new EmptyLeaf(), default));
        var surface = new UiSurface(root);
        surface.Dispose();
        Assert.Throws<ObjectDisposedException>(surface.UpdateLayout);
        Assert.Throws<ObjectDisposedException>(() => surface.SetViewport(Vector2.Zero));
    }

    /// <summary>Tests finite negative offsets and the rejection of non-finite offsets.</summary>
    /// <param name="offset">The parent-local offset.</param>
    /// <param name="valid">Whether the offset is finite.</param>
    [Theory]
    [InlineData(-5f, true)]
    [InlineData(float.NaN, false)]
    [InlineData(float.PositiveInfinity, false)]
    [InlineData(float.NegativeInfinity, false)]
    public void ChildOffsetsMustBeFiniteButCanBeNegative(float offset, bool valid)
    {
        var leaf = new Leaf(new(10, 10));
        var root = new ProtocolPanel();
        root.Children.Initialize(leaf);
        root.Compute = (owner, constraints) =>
        {
            owner.RunChild(leaf, constraints.Loosen());
            owner.OffsetChild(leaf, new(offset, offset));
            return constraints.Constrain(Vector2.Zero);
        };
        using var surface = new UiSurface(root);
        surface.SetViewport(new(100, 100));
        if (valid)
        {
            surface.UpdateLayout();
            Assert.Equal(new Rect2D(offset, offset, 10, 10), leaf.Bounds);
        }
        else
        {
            Assert.Throws<ArgumentOutOfRangeException>(surface.UpdateLayout);
            Assert.Equal(new Rect2D(0, 0, 10, 10), leaf.Bounds);
            root.Compute = null;
            surface.UpdateLayout();
            Assert.Equal(new Vector2(10, 10), leaf.Size);
        }
    }

    /// <summary>Tests failed setter validation without invalidating or changing a clean tree.</summary>
    [Fact]
    public void InvalidInputsPreserveTheCleanPublishedState()
    {
        var leaf = new Leaf(new(10, 10));
        Definitions.Stack root = Stack()[leaf];
        using var surface = new UiSurface(root);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        Assert.Throws<ArgumentOutOfRangeException>(() => leaf.Width = -1);
        Assert.Throws<ArgumentOutOfRangeException>(() => leaf.Height = float.NaN);
        Assert.Throws<ArgumentOutOfRangeException>(() => root.Spacing = float.PositiveInfinity);
        Assert.Throws<ArgumentOutOfRangeException>(() => surface.SetViewport(new(-1, 100)));
        Assert.Throws<InvalidOperationException>(() => root.Children.Initialize(leaf, leaf));
        surface.UpdateLayout();
        Assert.Equal(1, leaf.Calls);
        Assert.Null(leaf.Width);
        Assert.Equal(new Vector2(100, 100), root.Size);
    }

    /// <summary>Tests hidden children omitted by a custom container without requiring an override call.</summary>
    [Fact]
    public void CustomContainerCanOmitHiddenChildren()
    {
        var leaf = new Leaf(new(10, 10)) { IsVisible = false, DuringLayout = () => throw new InvalidOperationException("hidden") };
        var root = new ProtocolPanel();
        root.Children.Initialize(leaf);
        root.Compute = (_, constraints) => constraints.Constrain(Vector2.Zero);
        using var surface = new UiSurface(root);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        Assert.Equal(0, leaf.Calls);
        Assert.Equal(default, leaf.Bounds);
    }

    /// <summary>Tests that fixed ownership survives disposal and reconnection requires a new layout.</summary>
    [Fact]
    public void FixedOwnershipAndReconnectionPreserveValuesUntilTheNextSuccessfulPass()
    {
        var leaf = new Leaf(new(10, 10));
        Definitions.Panel root = Panel()[leaf];
        var surface = new UiSurface(root);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        Assert.Throws<InvalidOperationException>(() => root.Children.Initialize());
        Assert.Equal(new Vector2(10, 10), leaf.Size);
        Assert.Same(root, leaf.Parent);
        surface.Dispose();
        using var replacement = new UiSurface(root);
        replacement.SetViewport(new(200, 200));
        replacement.UpdateLayout();
        Assert.Equal(new Vector2(200, 200), root.Size);
        Assert.Equal(2, leaf.Calls);
    }

    /// <summary>Tests content overflow that cannot produce a finite unbounded container size.</summary>
    [Fact]
    public void UnboundedContentSizeOverflowFailsWithoutPublishing()
    {
        Definitions.Stack stack = Stack()[new Leaf(new(1, float.MaxValue)), new Leaf(new(1, float.MaxValue))];
        var root = new ProtocolPanel();
        root.Children.Initialize(stack);
        root.Compute = (owner, constraints) =>
        {
            owner.RunChild(stack, new(0, 10, 0, float.PositiveInfinity));
            owner.OffsetChild(stack, Vector2.Zero);
            return constraints.Constrain(Vector2.Zero);
        };
        using var surface = new UiSurface(root);
        Assert.Throws<InvalidOperationException>(surface.UpdateLayout);
        Assert.Equal(default, stack.Bounds);
    }

    /// <summary>Tests independent surface layouts nested inside a custom override.</summary>
    [Fact]
    public void IndependentSurfacesHaveIndependentLayoutContexts()
    {
        var otherLeaf = new Leaf(new(5, 5));
        using var other = new UiSurface(Panel()[otherLeaf]);
        other.SetViewport(new(20, 20));
        var leaf = new Leaf(new(10, 10)) { DuringLayout = other.UpdateLayout };
        using var surface = new UiSurface(Panel()[leaf]);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        Assert.Equal(new Vector2(5, 5), otherLeaf.Size);
        Assert.Equal(new Vector2(10, 10), leaf.Size);
    }

    private sealed class EmptyLeaf : Widget
    {
    }

    private sealed class GroupedWidget : Widget
    {
        public GroupedWidget()
        {
            Embeddeds = CreateChildren();
            Foreground = CreateChildren();
        }

        public WidgetChildren Embeddeds { get; }

        public WidgetChildren Foreground { get; }

        public bool SkipEmbeddeds { get; set; }

        protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
        {
            foreach (Widget child in Foreground.Concat(SkipEmbeddeds ? Array.Empty<Widget>() : Embeddeds))
            {
                LayoutChild(child, constraints.Loosen());
                SetChildOffset(child, Vector2.Zero);
            }

            return constraints.Constrain(Vector2.Zero);
        }
    }

    private sealed class Leaf(Vector2 preferredSize) : Widget
    {
        private Vector2 _preferredSize = preferredSize;

        public int Calls { get; private set; }

        public UiBoxConstraints LastConstraints { get; private set; }

        public Action? DuringLayout { get; set; }

        public Vector2? RawResult { get; set; }

        public void UpdatePreferredSize(Vector2 size)
        {
            _preferredSize = size;
            InvalidateLayout();
        }

        protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
        {
            Calls++;
            LastConstraints = constraints;
            DuringLayout?.Invoke();
            return RawResult ?? constraints.Constrain(_preferredSize);
        }
    }

    private sealed class WrappingLeaf : Widget
    {
        public int Calls { get; private set; }

        protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
        {
            Calls++;
            float width = constraints.Constrain(new(100, 0)).X;
            float height = MathF.Ceiling(100 / width) * 10;
            return constraints.Constrain(new(width, height));
        }
    }

    private sealed class ProtocolPanel : Definitions.Panel
    {
        public Func<ProtocolPanel, UiBoxConstraints, Vector2>? Compute { get; set; }

        public Vector2 RunChild(Widget child, UiBoxConstraints constraints) => LayoutChild(child, constraints);

        public void OffsetChild(Widget child, Vector2 offset) => SetChildOffset(child, offset);

        protected override Vector2 LayoutOverride(UiBoxConstraints constraints) =>
            Compute?.Invoke(this, constraints) ?? base.LayoutOverride(constraints);
    }
}
