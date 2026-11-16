using System.Numerics;
using Lumyte.Core.Time;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Signals;
using Xunit;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Tests;

/// <summary>Checks one-pass layout allocation, finite math and transactional declarations.</summary>
public sealed class C4LayoutTests
{
    /// <summary>Checks nonflex-first measurement, declaration-order placement and tight parent priority.</summary>
    /// <param name="vertical">Whether to exchange axes.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FlexAllocatesAfterNonFlexExactlyOnce(bool vertical)
    {
        var order = new List<int>();
        var first = new Probe(new(80), () => order.Add(0));
        var one = new Probe(new(5), () => order.Add(1));
        var two = new Probe(new(5), () => order.Add(2));
        Definitions.Flex root = Flex(orientation: vertical ? UiOrientation.Vertical : UiOrientation.Horizontal, spacing: 10)[
            Expanded(width: 1, height: 1)[one], first, Expanded(weight: 2)[two]];
        using var surface = new UiSurface(root);
        surface.SetViewport(new(400));
        surface.UpdateLayout();
        Assert.Equal(new[] { 0, 1, 2 }, order);
        Assert.Equal(100, vertical ? one.Element.Size.Y : one.Element.Size.X);
        Assert.Equal(200, vertical ? two.Element.Size.Y : two.Element.Size.X);
        Assert.Equal(110, vertical ? first.Element.Bounds.Y : first.Element.Bounds.X);
        Assert.Equal(200, vertical ? two.Element.Parent!.Bounds.Y : two.Element.Parent!.Bounds.X);
        Assert.All(new[] { first, one, two }, item => Assert.Equal(1, item.Calls));
        surface.UpdateLayout();
        Assert.All(new[] { first, one, two }, item => Assert.Equal(1, item.Calls));
    }

    /// <summary>Checks overflow does not shrink ordinary content and large weights remain finite.</summary>
    [Fact]
    public void FlexOverflowAndLargeWeightsAreStable()
    {
        using var overflow = new UiSurface(Flex()[SizedBox(height: 80), SizedBox(height: 80), Expanded()]);
        overflow.SetViewport(new(100));
        overflow.UpdateLayout();
        Assert.Equal(new Rect2D(0, 160, 0, 0), overflow.RootElement.Children[2].Bounds);
        using var large = new UiSurface(Flex(orientation: UiOrientation.Horizontal)[Expanded(weight: float.MaxValue), Expanded(weight: float.MaxValue)]);
        large.SetViewport(new(100));
        large.UpdateLayout();
        Assert.All(large.RootElement.Children, item => Assert.Equal(50, item.Size.X));
    }

    /// <summary>Checks wrap measures with parent maxima, then uses returned dimensions without relayout.</summary>
    /// <param name="vertical">Whether runs are vertical.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WrapUsesFullConstraintsOnce(bool vertical)
    {
        var first = new Probe(vertical ? new(20, 60) : new(60, 20));
        var second = new Probe(vertical ? new(30, 60) : new(60, 30));
        using var surface = new UiSurface(Wrap(orientation: vertical ? UiOrientation.Vertical : UiOrientation.Horizontal, spacing: 4, runSpacing: 6)[first, second]);
        surface.SetViewport(new(100));
        surface.UpdateLayout();
        Assert.Equal(new Vector2(vertical ? 26 : 0, vertical ? 0 : 26), new(second.Element.Bounds.X, second.Element.Bounds.Y));
        Assert.Equal(new UiBoxConstraints(0, 100, 0, 100), second.Constraints);
        Assert.Equal(1, first.Calls);
        Assert.Equal(1, second.Calls);
    }

    /// <summary>Checks equal fits, hidden children and zero-sized visible items.</summary>
    [Fact]
    public void WrapExactFitsAndHiddenItemsDoNotCreateExtraRuns()
    {
        using var surface = new UiSurface(Wrap(spacing: 4, runSpacing: 6)[SizedBox(width: 48, height: 10), SizedBox(isVisible: false), SizedBox(width: 48, height: 20), SizedBox()]);
        surface.SetViewport(new(100));
        surface.UpdateLayout();
        Assert.Equal(52, surface.RootElement.Children[2].Bounds.X);
        Assert.Equal(26, surface.RootElement.Children[3].Bounds.Y);
        Assert.Equal(default, surface.RootElement.Children[1].Bounds);
    }

    /// <summary>Checks resolved spans, fixed overflow and zero remaining ratio tracks.</summary>
    /// <param name="width">The available width.</param>
    /// <param name="spanWidth">The expected second cell span.</param>
    [Theory]
    [InlineData(300, 210)]
    [InlineData(50, 10)]
    [InlineData(0, 10)]
    public void GridResolvesBeforeMeasuring(float width, float spanWidth)
    {
        var probe = new Probe(new(5));
        using var surface = new UiSurface(Grid(columns: new UiGridTrack[] { new(80), new(1, true), new(3, true) }, columnSpacing: 10)[GridCell(), GridCell(column: 1, columnSpan: 2)[probe]]);
        surface.SetViewport(new(width, 100));
        surface.UpdateLayout();
        Assert.Equal(new Vector2(spanWidth, 100), probe.Element.Size);
        Assert.Equal(90, probe.Element.Parent!.Bounds.X);
        Assert.Equal(1, probe.Calls);
        Assert.True(probe.Constraints.IsTight);
        Assert.Equal(width, surface.RootElement.Size.X);
    }

    /// <summary>Checks independent rounding and overflow-resistant ratio sums.</summary>
    [Fact]
    public void GridRatiosRoundIndependently()
    {
        using var surface = new UiSurface(Grid(columns: new UiGridTrack[] { new(float.MaxValue, true), new(float.MaxValue, true), new(float.MaxValue, true) })[GridCell(), GridCell(column: 1), GridCell(column: 2)]);
        surface.SetViewport(new(1));
        surface.UpdateLayout();
        Assert.All(surface.RootElement.Children, cell => Assert.Equal(1f / 3, cell.Size.X));
    }

    /// <summary>Checks unbounded axes reject ratios and expanded allocations, but permit fixed layouts.</summary>
    [Fact]
    public void UnboundedAxesHaveExplicitPolicies()
    {
        using var expanded = new UiSurface(Stack()[Flex()[Expanded(isVisible: false)]]);
        Assert.Throws<InvalidOperationException>(expanded.UpdateLayout);
        using var ratio = new UiSurface(Stack()[Grid()]);
        Assert.Throws<InvalidOperationException>(ratio.UpdateLayout);
        using var fixedGrid = new UiSurface(Stack()[Grid(rows: new UiGridTrack[] { new(20), new(30) }, rowSpacing: 5)[GridCell(), GridCell(row: 1)]]);
        fixedGrid.SetViewport(new(100));
        fixedGrid.UpdateLayout();
        Assert.Equal(55, fixedGrid.RootElement.Children[0].Size.Y);
        using var wrap = new UiSurface(Stack(orientation: UiOrientation.Horizontal)[Wrap()[SizedBox(width: 60), SizedBox(width: 60)]]);
        wrap.SetViewport(new(100));
        wrap.UpdateLayout();
        Assert.Equal(120, wrap.RootElement.Children[0].Size.X);
    }

    /// <summary>Checks invalid topology is rejected before replacing the old root.</summary>
    [Fact]
    public void InvalidTopologyPreservesPublishedTree()
    {
        using var surface = new UiSurface(Grid()[GridCell()]);
        UiElement old = surface.RootElement;
        Assert.Throws<InvalidOperationException>(() =>
        {
            surface.Rebuild(Grid()[GridCell(), GridCell(isVisible: false)]);
            surface.UpdateTree();
        });
        Assert.Same(old, surface.RootElement);
        Assert.Throws<InvalidOperationException>(() =>
        {
            surface.Rebuild(Grid()[GridCell(column: int.MaxValue, columnSpan: int.MaxValue)]);
            surface.UpdateTree();
        });
        Assert.Same(old, surface.RootElement);
        Assert.Throws<InvalidOperationException>(() =>
        {
            surface.Rebuild(Grid()[SizedBox()]);
            surface.UpdateTree();
        });
        Assert.Throws<InvalidOperationException>(() =>
        {
            surface.Rebuild(Panel()[Expanded()]);
            surface.UpdateTree();
        });
        Assert.Throws<InvalidOperationException>(() =>
        {
            surface.Rebuild(Panel()[GridCell()]);
            surface.UpdateTree();
        });
        Assert.Same(old, surface.RootElement);
    }

    /// <summary>Checks track snapshots and same declaration mounting do not share mutable runtime state.</summary>
    [Fact]
    public void TracksAreSnapshotsAndDeclarationsAreReusable()
    {
        UiGridTrack[] tracks = [new(20), new(1, true)];
        Definitions.Grid declaration = Grid(columns: tracks)[GridCell(), GridCell(column: 1)];
        tracks[0] = new(99);
        using var first = new UiSurface(declaration);
        using var second = new UiSurface(declaration);
        first.SetViewport(new(100));
        second.SetViewport(new(200));
        first.UpdateLayout();
        second.UpdateLayout();
        Assert.Equal(20, first.RootElement.Children[0].Size.X);
        Assert.Equal(80, first.RootElement.Children[1].Size.X);
        Assert.Equal(180, second.RootElement.Children[1].Size.X);
        Assert.NotSame(first.RootElement.Children[1], second.RootElement.Children[1]);
    }

    /// <summary>Checks source weight updates preserve identity and invalidate ancestor allocations.</summary>
    [Fact]
    public void SignalWeightUpdatesReallocateRetainedElements()
    {
        var weight = Signal.Create(1f);
        using var surface = new UiSurface(Flex(orientation: UiOrientation.Horizontal)[Expanded(weight: weight), Expanded()]);
        surface.SetViewport(new(120));
        surface.UpdateLayout();
        UiElement old = surface.RootElement.Children[0];
        weight.Value = 3;
        surface.UpdateLayout();
        Assert.Same(old, surface.RootElement.Children[0]);
        Assert.Equal(90, old.Size.X);
        weight.Value = 0;
        Assert.ThrowsAny<Exception>(surface.UpdateTree);
        Assert.Equal(3, Assert.IsType<Definitions.Expanded>(old.Widget).Weight);
        Assert.Equal(90, old.Size.X);
    }

    /// <summary>Checks empty grids and boxes honor finite zero constraints.</summary>
    [Fact]
    public void EmptyAndZeroLayoutsAreFinite()
    {
        using var grid = new UiSurface(Grid(rows: Array.Empty<UiGridTrack>(), columns: Array.Empty<UiGridTrack>()));
        grid.UpdateLayout();
        Assert.Equal(Vector2.Zero, grid.RootElement.Size);
        using var flex = new UiSurface(Flex()[Expanded()]);
        flex.UpdateLayout();
        Assert.Equal(Vector2.Zero, flex.RootElement.Children[0].Size);
        Assert.Throws<ArgumentException>(() => Expanded()[SizedBox(), SizedBox()]);
        Assert.Throws<ArgumentException>(() => GridCell()[SizedBox(), SizedBox()]);
    }

    /// <summary>Checks invalid numeric values cannot become declarations.</summary>
    /// <param name="value">The invalid value.</param>
    [Theory]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void InvalidNumbersAreRejected(float value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Expanded(weight: value));
        Assert.Throws<ArgumentOutOfRangeException>(() => Flex(spacing: value));
        Assert.Throws<ArgumentOutOfRangeException>(() => Wrap(runSpacing: value));
        Assert.Throws<ArgumentOutOfRangeException>(() => Grid(columnSpacing: value));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UiGridTrack(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UiGridTrack(value, true));
    }

    /// <summary>Checks C4 offsets flow through modifiers, hit routing, capture, focus and snapshots once.</summary>
    [Fact]
    public void LayoutGeometryDrivesInputAndAutomation()
    {
        int clicks = 0;
        Definitions.Grid grid = Grid(columns: new UiGridTrack[] { new(60), new(1, true) })[
            GridCell()[Button(testId: "action", width: 20, height: 10, with: [Align.Center()], onClick: () => clicks++)],
            GridCell(column: 1)[Wrap()[SizedBox(width: 5, height: 5)]]];
        Definitions.Flex declaration = Flex(orientation: UiOrientation.Horizontal, with: [Padding.All(8)])[SizedBox(width: 20), Expanded()[grid]];
        using var surface = new UiSurface(declaration);
        surface.SetViewport(new(200, 100));
        surface.UpdateLayout();
        UiElement button = surface.RootElement.Children[1].Children[0].Children[0].Children[0];
        UiAutomationNode before = surface.CaptureAutomationSnapshot().Nodes.Single(node => node.TestId == "action");
        Assert.Equal(new Rect2D(48, 45, 20, 10), before.ContentBounds);
        Assert.Equal(before.ContentBounds, before.ActionableBounds);
        Assert.False(surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(29, 9))));
        Assert.True(surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(49, 46))));
        Assert.True(surface.TryFocus(button));
        Assert.True(surface.CaptureAutomationSnapshot().Nodes.Single(node => node.TestId == "action").IsFocused);
        Assert.True(surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(49, 46))));
        Assert.Equal(1, clicks);
        UiAutomationSnapshot snapshot = surface.CaptureAutomationSnapshot();
        Assert.All(snapshot.Nodes.Where(node => node.TestId != "action"), node => Assert.Equal(UiAutomationActions.None, node.Actions));
    }

    /// <summary>Checks fixed-track overflow is clipped by the normal shared input geometry.</summary>
    /// <param name="clip">Whether the grid clips children.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GridOverflowUsesExistingClipRules(bool clip)
    {
        int clicks = 0;
        using var surface = new UiSurface(Panel()[Grid(width: 50, clipToBounds: clip, columns: new UiGridTrack[] { new(80) })[
            GridCell()[Button(testId: "overflow", onClick: () => clicks++)]]]);
        surface.SetViewport(new(100));
        surface.UpdateLayout();
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(70, 20)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(70, 20)));
        Assert.Equal(clip ? 0 : 1, clicks);
        UiAutomationNode node = surface.CaptureAutomationSnapshot().Nodes.Single(item => item.TestId == "overflow");
        Assert.Equal(80, node.ContentBounds.Width);
        Assert.Equal(clip ? 50 : 80, node.ActionableBounds.Width);
    }

    /// <summary>Checks invalid live track changes retain the accepted declaration and cell identity.</summary>
    [Fact]
    public void SignalTrackChangesValidateBeforePublication()
    {
        var tracks = Signal.Create<IReadOnlyList<UiGridTrack>>(new UiGridTrack[] { new(20), new(1, true) });
        using var surface = new UiSurface(Grid(columns: tracks)[GridCell(column: 1)]);
        surface.SetViewport(new(100));
        surface.UpdateLayout();
        UiElement root = surface.RootElement;
        UiElement cell = root.Children[0];
        tracks.Value = new UiGridTrack[] { new(40), new(1, true) };
        surface.UpdateLayout();
        Assert.Equal(40, cell.Bounds.X);
        tracks.Value = new UiGridTrack[] { new(40) };
        Assert.ThrowsAny<Exception>(surface.UpdateTree);
        Assert.Same(root, surface.RootElement);
        Assert.Same(cell, root.Children[0]);
        Assert.Equal(2, Assert.IsType<Definitions.Grid>(root.Widget).Columns.Count);
        Assert.Equal(40, cell.Bounds.X);
    }

    /// <summary>Checks unchanged parent validators see proposed child replacements, not stale declarations.</summary>
    [Fact]
    public void PartialUpdatesValidateProjectedSlotsBeforePublishing()
    {
        using var surface = new UiSurface(Grid(columns: new UiGridTrack[] { new(40), new(60) })[GridCell(), GridCell(column: 1)]);
        UiElement old = surface.RootElement.Children[1];
        Assert.Throws<InvalidOperationException>(() => surface.RebuildSubtree(old, GridCell()));
        Assert.Same(old, surface.RootElement.Children[1]);
        Assert.Throws<InvalidOperationException>(() => surface.RebuildSubtree(old, SizedBox()));
        Assert.Same(old, surface.RootElement.Children[1]);
        surface.RebuildSubtree(old, GridCell(column: 1, width: 1));
        Assert.Same(old, surface.RootElement.Children[1]);
        surface.SetViewport(new(100));
        surface.UpdateLayout();
        Assert.Equal(60, old.Size.X);
        using var flex = new UiSurface(Flex()[Expanded()]);
        UiElement expanded = flex.RootElement.Children[0];
        flex.RebuildSubtree(expanded, Expanded(weight: 2));
        Assert.Same(expanded, flex.RootElement.Children[0]);
        Assert.Throws<InvalidOperationException>(() => flex.RebuildSubtree(flex.RootElement, Panel()[expanded.Widget]));
        Assert.Same(expanded, flex.RootElement.Children[0]);
    }

    /// <summary>Checks a matching parent type is insufficient when the child occupies another slot.</summary>
    [Fact]
    public void MatchingParentInWrongSlotIsRejected()
    {
        var flex = new ExtraFlex();
        flex.Extra.Initialize(Expanded());
        Assert.Throws<InvalidOperationException>(() => new UiSurface(flex));
        var grid = new ExtraGrid();
        grid.Extra.Initialize(GridCell());
        Assert.Throws<InvalidOperationException>(() => new UiSurface(grid));
    }

    /// <summary>Checks inherited width and child Align use the framework rather than resizing returned sizes.</summary>
    [Fact]
    public void GridTightParentWinsAndAlignPositionsTheInnerBody()
    {
        using var surface = new UiSurface(Grid()[GridCell(width: 1)[SizedBox(width: 20, height: 10, with: [Align.Center()])]]);
        surface.SetViewport(new(100, 80));
        surface.UpdateLayout();
        UiElement cell = surface.RootElement.Children[0];
        Assert.Equal(new Vector2(100, 80), cell.Size);
        Assert.Equal(new Rect2D(40, 35, 20, 10), cell.Children[0].ContentBounds);
    }

    /// <summary>Checks finite-float representability failures do not publish infinity.</summary>
    [Fact]
    public void UnrepresentableContentAndOffsetsFailExplicitly()
    {
        using var grid = new UiSurface(Stack(orientation: UiOrientation.Horizontal)[Grid(columns: new UiGridTrack[] { new(float.MaxValue), new(float.MaxValue) })]);
        grid.SetViewport(new(100));
        Assert.Throws<InvalidOperationException>(grid.UpdateLayout);
        Assert.True(float.IsFinite(grid.RootElement.Size.X));
        using var flex = new UiSurface(Flex(orientation: UiOrientation.Horizontal, spacing: float.MaxValue)[SizedBox(width: float.MaxValue), SizedBox(width: 1)]);
        flex.SetViewport(new(100));
        Assert.Throws<InvalidOperationException>(flex.UpdateLayout);
        Assert.True(float.IsFinite(flex.RootElement.Children[1].Bounds.X));
        Assert.Throws<ArgumentOutOfRangeException>(() => Expanded(weight: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UiGridTrack(0, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => GridCell(row: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => GridCell(rowSpan: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UiSurface(Wrap(orientation: (UiOrientation)42)));
    }

    /// <summary>Checks zero fixed tracks and hidden flex items preserve the common one-pass rules.</summary>
    [Fact]
    public void ZeroTracksAndHiddenFlexChildrenAreWellDefined()
    {
        var hidden = new Probe(new(20));
        hidden.IsVisible = false;
        using var surface = new UiSurface(Flex(orientation: UiOrientation.Horizontal, spacing: 7)[hidden, Expanded(), Expanded()]);
        surface.SetViewport(new(107, 50));
        surface.UpdateLayout();
        Assert.Equal(0, hidden.Calls);
        Assert.Equal(default, hidden.Element.Bounds);
        Assert.Equal(50, surface.RootElement.Children[1].Size.X);
        Assert.Equal(57, surface.RootElement.Children[2].Bounds.X);
        using var grid = new UiSurface(Grid(columns: new UiGridTrack[] { default })[GridCell()[SizedBox(width: 99)]]);
        grid.SetViewport(new(100));
        grid.UpdateLayout();
        Assert.Equal(0, grid.RootElement.Children[0].Size.X);
        Assert.Equal(0, grid.RootElement.Children[0].Children[0].Size.X);
    }

    /// <summary>Checks width-dependent children receive final bounds before computing their height.</summary>
    [Fact]
    public void WrapDoesNotRemeasureWidthDependentContentAfterBreaking()
    {
        var first = new WidthDependent();
        var second = new WidthDependent();
        using var surface = new UiSurface(Wrap(runSpacing: 3)[first, second]);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        Assert.Equal(new Rect2D(0, 23, 100, 20), surface.RootElement.Children[1].Bounds);
        Assert.Equal(1, first.Calls);
        Assert.Equal(1, second.Calls);
    }

    /// <summary>Checks parent-relative validation resolves each use of a shared passive cell independently.</summary>
    [Fact]
    public void SharedCellsAndExpandedDeclarationsResolveTheirOwnParent()
    {
        Definitions.GridCell cell = GridCell();
        Definitions.Expanded expanded = Expanded();
        using var surface = new UiSurface(Stack()[Grid(height: 20)[cell], Grid(height: 30)[cell], Flex(height: 40)[expanded], Flex(height: 50)[expanded]]);
        surface.SetViewport(new(100, 200));
        surface.UpdateLayout();
        Assert.Equal(20, surface.RootElement.Children[0].Children[0].Size.Y);
        Assert.Equal(30, surface.RootElement.Children[1].Children[0].Size.Y);
        Assert.Equal(40, surface.RootElement.Children[2].Children[0].Size.Y);
        Assert.Equal(50, surface.RootElement.Children[3].Children[0].Size.Y);
        Assert.NotSame(surface.RootElement.Children[0].Children[0], surface.RootElement.Children[1].Children[0]);
    }

    /// <summary>Checks declaration preparation and Signal adoption do not execute layout.</summary>
    [Fact]
    public void TreeUpdatesInvalidateWithoutMeasuringDuringPrepare()
    {
        var weight = Signal.Create(1f);
        var probe = new Probe(new(5));
        using var surface = new UiSurface(Flex(orientation: UiOrientation.Horizontal)[Expanded(weight: weight)[probe], Expanded()]);
        surface.SetViewport(new(100));
        surface.UpdateLayout();
        Assert.Equal(1, probe.Calls);
        weight.Value = 3;
        surface.UpdateTree();
        Assert.Equal(1, probe.Calls);
        surface.UpdateLayout();
        Assert.Equal(2, probe.Calls);
        Assert.Equal(75, probe.Element.Size.X);
    }

    /// <summary>Checks slot projection rejects foreign slots without weakening existing widget-query signatures.</summary>
    [Fact]
    public void SlotProjectionRejectsForeignOwnersBeforePublication()
    {
        Definitions.Panel foreign = Panel();
        var declaration = new SlotObserver(foreign.Children);
        Assert.Throws<ArgumentException>(() => new UiSurface(declaration));
    }

    /// <summary>Checks row spans include internal gaps and adjacent rectangles do not overlap.</summary>
    [Fact]
    public void GridRowSpansAndAdjacencyResolveNumerically()
    {
        using var surface = new UiSurface(Grid(rows: new UiGridTrack[] { new(20), new(1, true), new(3, true) }, rowSpacing: 5, columns: new UiGridTrack[] { new(40), new(60) })[
            GridCell(rowSpan: 3), GridCell(row: 1, column: 1, rowSpan: 2)]);
        surface.SetViewport(new(100, 110));
        surface.UpdateLayout();
        Assert.Equal(new Rect2D(0, 0, 40, 110), surface.RootElement.Children[0].Bounds);
        Assert.Equal(new Rect2D(40, 25, 60, 85), surface.RootElement.Children[1].Bounds);
    }

    /// <summary>Checks presence-hidden Expanded consumes neither spacing nor weight.</summary>
    [Fact]
    public void PresenceHiddenExpandedDoesNotConsumeAllocation()
    {
        using var surface = new UiSurface(Flex(orientation: UiOrientation.Horizontal, spacing: 10)[Expanded(isPresent: false), Expanded()]);
        surface.SetViewport(new(100));
        surface.UpdateLayout();
        Assert.Equal(0, surface.RootElement.Children[1].Bounds.X);
        Assert.Equal(100, surface.RootElement.Children[1].Size.X);
    }

    /// <summary>Checks presence-hidden wrap children do not consume a gap or create a run.</summary>
    [Fact]
    public void PresenceHiddenWrapChildDoesNotConsumeGap()
    {
        using var surface = new UiSurface(Wrap(spacing: 4, runSpacing: 6)[SizedBox(width: 48, height: 20), SizedBox(isPresent: false), SizedBox(width: 48, height: 20)]);
        surface.SetViewport(new(100));
        surface.UpdateLayout();
        Assert.Equal(new Rect2D(52, 0, 48, 20), surface.RootElement.Children[2].Bounds);
    }

    /// <summary>Checks Signal presence changes reallocate retained flex siblings.</summary>
    [Fact]
    public void PresenceSignalReallocatesFlex()
    {
        var present = Signal.Create(true);
        using var surface = new UiSurface(Flex(orientation: UiOrientation.Horizontal, spacing: 10)[Expanded(isPresent: present), Expanded()]);
        surface.SetViewport(new(100));
        surface.UpdateLayout();
        UiElement sibling = surface.RootElement.Children[1];
        Assert.Equal(45, sibling.Size.X);
        present.Value = false;
        surface.UpdateLayout();
        Assert.Same(sibling, surface.RootElement.Children[1]);
        Assert.Equal(0, sibling.Bounds.X);
        Assert.Equal(100, sibling.Size.X);
    }

    /// <summary>Checks exit transitions retain allocation until the presence becomes Hidden.</summary>
    [Fact]
    public void ExitingExpandedRetainsAllocationUntilHidden()
    {
        var present = Signal.Create(true);
        var clock = new ManualClock();
        using var surface = new UiSurface(Flex(orientation: UiOrientation.Horizontal, spacing: 10)[
            Expanded(isPresent: present, transitions: new UiElementTransitions { AnimateInitial = false, Exit = FadeOut(Duration.FromSeconds(1)) }), Expanded()]);
        surface.VisualHost = new PresenceHost();
        surface.SetAnimationClock(clock);
        surface.SetViewport(new(100));
        surface.UpdateLayout();
        surface.ConfirmVisualPresentation(surface.CaptureVisualPresentation());
        surface.DeliverVisualNotifications();
        present.Value = false;
        surface.UpdateLayout();
        Assert.Equal(UiPresenceState.Exiting, surface.RootElement.Children[0].PresenceState);
        Assert.Equal(45, surface.RootElement.Children[1].Size.X);
        UiPresenceEntry entry = Assert.Single(surface.VisualEntries);
        entry.Begin(clock.Now);
        clock.Advance(Duration.FromSeconds(1));
        surface.AdvanceAnimations();
        surface.UpdateLayout();
        Assert.Equal(UiPresenceState.Hidden, surface.RootElement.Children[0].PresenceState);
        Assert.Equal(100, surface.RootElement.Children[1].Size.X);
        Assert.Equal(0, surface.RootElement.Children[1].Bounds.X);
    }

    private sealed class PresenceHost : IUiVisualHost
    {
        public bool CanPresent => true;

        public UiMotionOptions Motion { get; } = new();

        public bool Record(UiElement widget, DrawingCanvas2D canvas) => false;
    }

    private sealed class SlotObserver(WidgetChildren slot) : Definitions.Panel
    {
        protected override void ValidateDeclaration(UiWidgetValidationContext context) => context.GetSlotChildren(slot);
    }

    private sealed class WidthDependent : Definitions.Widget
    {
        internal int Calls { get; private set; }

        protected override UiElement CreateElement() => new WidthElement(this);

        private sealed class WidthElement(WidthDependent widget) : UiElement<WidthDependent>(widget)
        {
            protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
            {
                Widget.Calls++;
                return constraints.Constrain(new(constraints.MaxWidth, constraints.MaxWidth / 5));
            }
        }
    }

    private sealed class ExtraFlex : Definitions.Flex
    {
        public ExtraFlex()
        {
            Extra = CreateChildren();
        }

        internal WidgetChildren Extra { get; }
    }

    private sealed class ExtraGrid : Definitions.Grid
    {
        public ExtraGrid()
        {
            Extra = CreateChildren();
        }

        internal WidgetChildren Extra { get; }
    }

    private sealed class Probe(Vector2 desired, Action? duringLayout = null) : Definitions.Widget
    {
        private readonly Vector2 _desired = desired;
        private readonly Action? _duringLayout = duringLayout;

        internal UiElement Element { get; private set; } = null!;

        internal int Calls { get; private set; }

        internal UiBoxConstraints Constraints { get; private set; }

        protected override UiElement CreateElement() => Element = new ProbeElement(this);

        private sealed class ProbeElement(Probe widget) : UiElement<Probe>(widget)
        {
            protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
            {
                Widget.Calls++;
                Widget.Constraints = constraints;
                Widget._duringLayout?.Invoke();
                return constraints.Constrain(Widget._desired);
            }
        }
    }
}
