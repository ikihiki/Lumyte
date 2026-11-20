using System.Numerics;
using Lumyte.Input.Abstractions;
using Lumyte.Signals;
using Xunit;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Tests;

/// <summary>Verifies finite allocation and real divider input.</summary>
public sealed class SplitViewTests
{
    /// <summary>Checks bounded horizontal and vertical allocation.</summary>
    /// <param name="vertical">Whether to exchange the axes.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AllocatesOrdinaryPanes(bool vertical)
    {
        using var surface = new UiSurface(SplitView(orientation: vertical ? UiOrientation.Vertical : UiOrientation.Horizontal)[
            SizedBox(with: [SplitView.Pane(minimum: 20)]),
            SizedBox(with: [SplitView.Pane(weight: 2, minimum: 40)])]);
        surface.SetViewport(new(304));
        surface.UpdateLayout();
        Assert.Equal(new float[] { 100, 200 }, ((SplitViewElement)surface.RootElement).PaneSizes);
        Assert.Equal(3, surface.RootElement.Children.Count);
        Assert.Equal(new Vector2(vertical ? 304 : 100, vertical ? 100 : 304), surface.RootElement.Children[0].Size);
    }

    /// <summary>Checks undersized and zero viewports, including extreme finite minima.</summary>
    /// <param name="length">The finite main-axis viewport.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(float.MaxValue)]
    public void MinimumOverflowStaysFinite(float length)
    {
        using var surface = new UiSurface(SplitView()[
            SizedBox(with: [SplitView.Pane(weight: float.MaxValue, minimum: float.MaxValue)]),
            SizedBox(with: [SplitView.Pane(weight: float.MaxValue, minimum: float.MaxValue)])]);
        surface.SetViewport(new(length, 20));
        surface.UpdateLayout();
        foreach (UiElement child in surface.RootElement.Children)
        {
            Assert.True(float.IsFinite(child.Size.X));
            Assert.InRange(child.Size.X, 0, length);
            Assert.InRange(child.Bounds.X, 0, length);
            Assert.InRange((double)child.Bounds.X + child.Size.X, 0, length);
        }
    }

    /// <summary>Checks empty and singleton topologies.</summary>
    [Fact]
    public void EmptyAndSinglePanesAreLegal()
    {
        using var empty = new UiSurface(SplitView());
        empty.SetViewport(new(200, 100));
        empty.UpdateLayout();
        Assert.Empty(empty.RootElement.Children);
        using var single = new UiSurface(SplitView()[SizedBox()]);
        single.SetViewport(new(200, 100));
        single.UpdateLayout();
        Assert.Equal(new Vector2(200, 100), Assert.Single(single.RootElement.Children).Size);
    }

    /// <summary>Checks capture, keyboard resize, and cancellation without completion.</summary>
    [Fact]
    public void RealDividerPointerAndKeyboardResize()
    {
        int changed = 0;
        int committed = 0;
        using var surface = new UiSurface(SplitView(onChanged: _ => changed++, onCommitted: _ => committed++)[SizedBox(), SizedBox()]);
        surface.SetViewport(new(204, 100));
        surface.UpdateLayout();
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(102, 30)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Move, new(122, 30)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(122, 30)));
        surface.UpdateLayout();
        Assert.Equal(120, ((SplitViewElement)surface.RootElement).PaneSizes[0]);
        Assert.Equal(1, changed);
        Assert.Equal(1, committed);
        surface.Dispatch(new UiKeyInput(Key.ArrowRight, true));
        surface.Dispatch(new UiKeyInput(Key.ArrowRight, false));
        surface.UpdateLayout();
        Assert.Equal(128, ((SplitViewElement)surface.RootElement).PaneSizes[0]);
        Assert.Equal(2, committed);
    }

    /// <summary>Rejects parent metadata outside its matching ordinary parent.</summary>
    [Fact]
    public void WrongParentRejected() => Assert.Throws<InvalidOperationException>(() => new UiSurface(Panel()[SizedBox(with: [SplitView.Pane()])]));

    /// <summary>Hidden interior content leaves an operable boundary between visible panes.</summary>
    [Fact]
    public void HiddenInteriorPaneHasUsableDivider()
    {
        using var surface = new UiSurface(SplitView()[SizedBox(key: "a"), SizedBox(key: "hidden", isVisible: false), SizedBox(key: "c")]);
        surface.SetViewport(new(204, 100));
        surface.UpdateLayout();
        Assert.True(surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(102, 30))));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Move, new(122, 30)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Cancel, new(122, 30)));
        surface.UpdateLayout();
        Assert.Equal(120, ((SplitViewElement)surface.RootElement).PaneSizes[0]);
        Assert.Equal(default, surface.RootElement.Children[1].Bounds);
        Assert.Single(surface.CaptureAutomationSnapshot().Nodes, node => node.Role == UiRole.Separator);
    }

    /// <summary>A child-local disable cancels capture before another command can resize it.</summary>
    [Fact]
    public void ChildLocalDisableCancelsGesture()
    {
        var enabled = Signal.Create(true);
        int commits = 0;
        using var surface = new UiSurface(SplitView(onCommitted: _ => commits++)[SizedBox(isEnabled: enabled), SizedBox()]);
        surface.SetViewport(new(204, 100));
        surface.UpdateLayout();
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(102, 30)));
        enabled.Value = false;
        surface.UpdateLayout();
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Move, new(122, 30)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(122, 30)));
        surface.UpdateLayout();
        Assert.Equal(0, commits);
        Assert.Equal(100, ((SplitViewElement)surface.RootElement).PaneSizes[0]);
    }

    /// <summary>Recreating only owner arguments retains both ordinary children and real dividers.</summary>
    [Fact]
    public void UnrelatedOwnerSignalPreservesAcceptedResize()
    {
        var step = Signal.Create(8f);
        using var surface = new UiSurface(SplitView(smallStep: step)[SizedBox(key: "a"), SizedBox(key: "b")]);
        surface.SetViewport(new(204, 100));
        surface.UpdateLayout();
        UiElement first = surface.RootElement.Children[0];
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(102, 30)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Move, new(122, 30)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(122, 30)));
        step.Value = 10;
        surface.UpdateLayout();
        Assert.Same(first, surface.RootElement.Children[0]);
        Assert.Equal(3, surface.RootElement.Children.Count);
        Assert.Equal(120, ((SplitViewElement)surface.RootElement).PaneSizes[0]);
    }

    /// <summary>A different actual pane type with the same key invalidates the captured boundary.</summary>
    [Fact]
    public void ReplacementWithSameKeyCancelsCapture()
    {
        int commits = 0;
        using var surface = new UiSurface(SplitView(onCommitted: _ => commits++)[SizedBox(key: "a"), SizedBox(key: "b")]);
        surface.SetViewport(new(204, 100));
        surface.UpdateLayout();
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(102, 30)));
        surface.Rebuild(SplitView(onCommitted: _ => commits++)[Panel(key: "a"), SizedBox(key: "b")]);
        surface.UpdateLayout();
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(102, 30)));
        surface.UpdateLayout();
        Assert.Equal(0, commits);
    }

    /// <summary>Explicit and positional key namespaces cannot collide on a later rebuild.</summary>
    [Fact]
    public void MixedPaneKeyNamespacesAreDistinct()
    {
        using var surface = new UiSurface(SplitView()[SizedBox(key: "#2"), SizedBox(key: "#3"), SizedBox(), SizedBox()]);
        surface.SetViewport(new(400, 100));
        surface.UpdateLayout();
        surface.Rebuild(SplitView()[SizedBox(key: "#2"), SizedBox(key: "#3"), SizedBox(), SizedBox()]);
        surface.UpdateLayout();
        Assert.Equal(7, surface.RootElement.Children.Count);
    }

    /// <summary>Every ordinary child receives one finite tight constraint, including nested owners.</summary>
    [Fact]
    public void NestedPanesAreLaidOutOnce()
    {
        var first = new CountedPane();
        var nested = new CountedPane();
        using var surface = new UiSurface(SplitView()[first, SplitView(orientation: UiOrientation.Vertical)[nested, SizedBox()]]);
        surface.SetViewport(new(400, 200));
        surface.UpdateLayout();
        surface.UpdateLayout();
        Assert.Equal(1, first.Calls);
        Assert.Equal(1, nested.Calls);
    }

    /// <summary>A failed generated pane leaves published identity, input focus and accepted sizes intact.</summary>
    [Fact]
    public void FailedFunctionPaneDoesNotPublish()
    {
        using var surface = new UiSurface(SplitView()[Button(key: "a", testId: "kept"), SizedBox(key: "b")]);
        surface.SetViewport(new(204, 100));
        surface.UpdateLayout();
        UiElement first = surface.RootElement.Children[0];
        Assert.True(surface.TryFocus(first));
        IReadOnlyList<float> before = ((SplitViewElement)surface.RootElement).PaneSizes;
        surface.Rebuild(SplitView()[Button(key: "a"), new FailingFunction()]);
        Assert.Throws<ArithmeticException>(surface.UpdateLayout);
        Assert.Same(first, surface.RootElement.Children[0]);
        Assert.Equal(before, ((SplitViewElement)surface.RootElement).PaneSizes);
    }

    /// <summary>Resizing one boundary leaves the third pane and its borrowed controller unchanged.</summary>
    [Fact]
    public void UnrelatedPaneAndControllerStayUnchanged()
    {
        using var controller = new ScrollController();
        using var surface = new UiSurface(SplitView()[SizedBox(), SizedBox(), ScrollView(controller: controller)[SizedBox(height: 600)]]);
        surface.SetViewport(new(608, 200));
        surface.UpdateLayout();
        surface.Dispatch(new UiWheelInput(new(500, 40), new(0, 40)));
        surface.UpdateLayout();
        float offset = controller.Offset;
        UiElement third = surface.RootElement.Children[2];
        Vector2 size = third.Size;
        Lumyte.Graphics.Drawing2D.Rect2D bounds = third.Bounds;
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(202, 40)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Move, new(222, 40)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(222, 40)));
        surface.UpdateLayout();
        Assert.Same(third, surface.RootElement.Children[2]);
        Assert.Equal(size, third.Size);
        Assert.Equal(bounds, third.Bounds);
        Assert.Equal(offset, controller.Offset);
    }

    /// <summary>Clean speculative owners keep one input version through observation and recording.</summary>
    [Fact]
    public void CleanLayoutDoesNotInvalidatePublishedInputVersion()
    {
        using var surface = new UiSurface(SplitView()[SizedBox(), SizedBox()]);
        surface.SetViewport(new(204, 100));
        surface.UpdateLayout();
        object? version = surface.InputVersionForWindowing;
        surface.UpdateLayout();
        surface.CaptureAutomationSnapshot();
        var canvas = new Lumyte.Graphics.Drawing2D.Canvas2D();
        canvas.Begin();
        surface.Record(canvas);
        canvas.End();
        Assert.Same(version, surface.InputVersionForWindowing);
    }

    /// <summary>An explicit declared-weight change silently resets the complete allocation snapshot.</summary>
    [Fact]
    public void DeclaredWeightChangeResetsSilently()
    {
        var weight = Signal.Create(1f);
        int changes = 0;
        using var surface = new UiSurface(SplitView(onChanged: _ => changes++)[SizedBox(with: [SplitView.Pane(weight)]), SizedBox()]);
        surface.SetViewport(new(204, 100));
        surface.UpdateLayout();
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(102, 30)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Move, new(122, 30)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(122, 30)));
        surface.UpdateLayout();
        Assert.Equal(1, changes);
        weight.Value = 2;
        surface.UpdateLayout();
        Assert.InRange(((SplitViewElement)surface.RootElement).PaneSizes[0], 133.33f, 133.34f);
        Assert.Equal(1, changes);
    }

    /// <summary>Adding a pane keeps retained resize weights in the original declared units.</summary>
    [Fact]
    public void AddedPaneUsesOriginalWeightUnits()
    {
        using var surface = new UiSurface(SplitView()[SizedBox(key: "a"), SizedBox(key: "b")]);
        surface.SetViewport(new(204, 100));
        surface.UpdateLayout();
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(102, 30)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Move, new(122, 30)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(122, 30)));
        surface.UpdateLayout();
        surface.SetViewport(new(304, 100));
        surface.Rebuild(SplitView()[SizedBox(key: "a"), SizedBox(key: "b"), SizedBox(key: "c")]);
        surface.UpdateLayout();
        Assert.InRange(((SplitViewElement)surface.RootElement).PaneSizes[2], 98.66f, 98.67f);
    }

    /// <summary>Throwing notifications keep accepted geometry and release the completed gesture owner.</summary>
    [Fact]
    public void CallbackFailureKeepsAcceptedGeometryAndReleasesOwnership()
    {
        int commits = 0;
        using var surface = new UiSurface(SplitView(onChanged: _ => throw new ArithmeticException("Expected callback failure."), onCommitted: _ => commits++)[SizedBox(), SizedBox()]);
        surface.SetViewport(new(204, 100));
        surface.UpdateLayout();
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(102, 30)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Move, new(122, 30)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(122, 30)));
        Assert.Throws<AggregateException>(surface.UpdateLayout);
        Assert.Equal(120, ((SplitViewElement)surface.RootElement).PaneSizes[0]);
        Assert.Equal(1, commits);
        surface.Rebuild(SplitView()[SizedBox(), SizedBox()]);
        surface.UpdateLayout();
        surface.Dispatch(new UiPointerInput(2, UiPointerPhase.Down, new(122, 30)));
        surface.Dispatch(new UiPointerInput(2, UiPointerPhase.Move, new(112, 30)));
        surface.Dispatch(new UiPointerInput(2, UiPointerPhase.Cancel, new(112, 30)));
        surface.UpdateLayout();
        Assert.Equal(110, ((SplitViewElement)surface.RootElement).PaneSizes[0]);
    }

    /// <summary>Disposal from a changed notification cannot emit a stale completion or retain pane owners.</summary>
    [Fact]
    public void CallbackDisposalClearsOwnerWithoutFalseCommit()
    {
        UiSurface? surface = null;
        int commits = 0;
        surface = new UiSurface(SplitView(onChanged: _ => surface!.Dispose(), onCommitted: _ => commits++)[SizedBox(), SizedBox()]);
        var split = (SplitViewElement)surface.RootElement;
        surface.SetViewport(new(204, 100));
        surface.UpdateLayout();
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(102, 30)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Move, new(122, 30)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(122, 30)));
        surface.UpdateLayout();
        Assert.Equal(0, commits);
        Assert.Empty(split.PaneSizes);
        surface.Dispose();
    }

    /// <summary>An already-disabled unrelated pane does not terminate another boundary's drag.</summary>
    [Fact]
    public void ExistingDisabledThirdPaneDoesNotCancelUnrelatedDrag()
    {
        int commits = 0;
        using var surface = new UiSurface(SplitView(onCommitted: _ => commits++)[SizedBox(), SizedBox(), SizedBox(isEnabled: false)]);
        surface.SetViewport(new(608, 200));
        surface.UpdateLayout();
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(202, 30)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Move, new(212, 30)));
        surface.UpdateLayout();
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Move, new(222, 30)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(222, 30)));
        surface.UpdateLayout();
        Assert.Equal(220, ((SplitViewElement)surface.RootElement).PaneSizes[0]);
        Assert.Equal(1, commits);
    }

    /// <summary>Requests returning to accepted extents before layout do not emit a false changed notification.</summary>
    [Fact]
    public void NetUnchangedResizeDoesNotNotifyChanged()
    {
        int changes = 0;
        int commits = 0;
        using var surface = new UiSurface(SplitView(onChanged: _ => changes++, onCommitted: _ => commits++)[SizedBox(), SizedBox()]);
        surface.SetViewport(new(204, 100));
        surface.UpdateLayout();
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(102, 30)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Move, new(122, 30)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Move, new(102, 30)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(102, 30)));
        surface.UpdateLayout();
        Assert.Equal(100, ((SplitViewElement)surface.RootElement).PaneSizes[0]);
        Assert.Equal(0, changes);
        Assert.Equal(1, commits);
    }

    /// <summary>One remaining zero-weight pane fills the viewport without losing its saved collapsed ratio.</summary>
    [Fact]
    public void HiddenPositiveSiblingDoesNotLeaveSingleZeroWeightPaneEmpty()
    {
        var visible = Signal.Create(true);
        using var surface = new UiSurface(SplitView()[SizedBox(), SizedBox(isVisible: visible)]);
        surface.SetViewport(new(204, 100));
        surface.UpdateLayout();
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(102, 30)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(102, 30)));
        surface.Dispatch(new UiKeyInput(Key.Home, true));
        surface.Dispatch(new UiKeyInput(Key.Home, false));
        surface.UpdateLayout();
        Assert.Equal(0, ((SplitViewElement)surface.RootElement).PaneSizes[0]);
        visible.Value = false;
        surface.UpdateLayout();
        Assert.Equal(204, Assert.Single(((SplitViewElement)surface.RootElement).PaneSizes));
        visible.Value = true;
        surface.UpdateLayout();
        Assert.Equal(0, ((SplitViewElement)surface.RootElement).PaneSizes[0]);
    }

    /// <summary>A new cancelled gesture cannot inherit an earlier unpublished completion notification.</summary>
    [Fact]
    public void PendingCompletionIsNotTransferredToCancelledNextGesture()
    {
        int commits = 0;
        using var surface = new UiSurface(SplitView(onCommitted: _ => commits++)[SizedBox(), SizedBox()]);
        surface.SetViewport(new(204, 100));
        surface.UpdateLayout();
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(102, 30)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(102, 30)));
        surface.Dispatch(new UiPointerInput(2, UiPointerPhase.Down, new(102, 30)));
        surface.Dispatch(new UiPointerInput(2, UiPointerPhase.Move, new(122, 30)));
        surface.Dispatch(new UiPointerInput(2, UiPointerPhase.Cancel, new(122, 30)));
        surface.UpdateLayout();
        Assert.Equal(120, ((SplitViewElement)surface.RootElement).PaneSizes[0]);
        Assert.Equal(0, commits);
    }

    private sealed class CountedPane : Definitions.Widget
    {
        internal int Calls { get; private set; }

        protected override UiElement CreateElement() => new CountedElement(this);

        private sealed class CountedElement(CountedPane widget) : UiElement<CountedPane>(widget)
        {
            protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
            {
                Assert.True(constraints.IsTight);
                Assert.True(float.IsFinite(constraints.MaxWidth));
                Assert.True(float.IsFinite(constraints.MaxHeight));
                Widget.Calls++;
                return new(constraints.MaxWidth, constraints.MaxHeight);
            }
        }
    }

    private sealed class FailingFunction : UiFunctionWidget
    {
        protected override Definitions.Widget Build() => throw new ArithmeticException("Expected factory rejection.");
    }
}
