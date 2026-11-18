using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Signals;
using Xunit;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Tests;

/// <summary>Verifies viewport geometry, nested residuals and controller lifetime.</summary>
public sealed class C5ScrollTests
{
    /// <summary>Callback movement cannot contribute to the wheel residual.</summary>
    /// <param name="callbackOffset">The independently requested callback offset.</param>
    /// <param name="pan">Whether touch panning delivers the delta.</param>
    [Theory]
    [InlineData(100, false)]
    [InlineData(0, false)]
    [InlineData(100, true)]
    [InlineData(0, true)]
    public void CallbackMovementCannotReverseWheelResidual(float callbackOffset, bool pan)
    {
        using var outer = new ScrollController();
        using var inner = new ScrollController();
        using var surface = new UiSurface(ScrollView(controller: outer)[Stack()[SizedBox(height: 100), ScrollView(controller: inner, width: 100, height: 100)[SizedBox(width: 100, height: 300)], SizedBox(height: 300)]]);
        surface.SetViewport(new(100));
        surface.UpdateLayout();
        outer.ScrollTo(100);
        surface.UpdateLayout();
        inner.OnChanged = value =>
        {
            if (value == 50)
            {
                inner.ScrollTo(callbackOffset);
            }
        };
        if (pan)
        {
            surface.Dispatch(new UiPointerInput(77, UiPointerPhase.Down, new(10, 70)) { Device = UiPointerDevice.Touch });
            surface.Dispatch(new UiPointerInput(77, UiPointerPhase.Move, new(10, 20)) { Device = UiPointerDevice.Touch });
            surface.Dispatch(new UiPointerInput(77, UiPointerPhase.Up, new(10, 20)) { Device = UiPointerDevice.Touch });
        }
        else
        {
            surface.Dispatch(new UiWheelInput(new(10), new(0, 50)));
        }

        Assert.Equal(100, outer.Offset);
        Assert.Equal(callbackOffset, inner.Offset);
    }

    /// <summary>Earlier lifecycle callbacks cannot steal a later controller.</summary>
    [Fact]
    public void MountReentryCannotStealPreparedController()
    {
        using var controller = new ScrollController();
        UiSurface? intruder = null;
        Exception? rejected = null;
        using var surface = new UiSurface(Panel()[new ReentryWidget(() => rejected = Record.Exception(() => intruder = new UiSurface(ScrollView(controller: controller)))), ScrollView(controller: controller)[SizedBox(height: 300)]]);
        intruder?.Dispose();
        Assert.IsType<InvalidOperationException>(rejected);
        Assert.True(controller.IsAttached);
    }

    /// <summary>Checks viewport clipping, hit geometry and clamping after extent shrink.</summary>
    [Fact]
    public void OffsetMovesContentWithoutResizingAndClamps()
    {
        using var controller = new ScrollController();
        using var surface = new UiSurface(ScrollView(controller: controller)[Stack()[Button(width: 100, height: 100), Button(width: 100, height: 100), Button(width: 100, height: 100)]]);
        surface.SetViewport(new(100));
        surface.UpdateLayout();
        Assert.Equal(200, controller.Maximum);
        controller.ScrollTo(125);
        surface.UpdateLayout();
        Assert.Equal(-125, surface.RootElement.Children[0].Bounds.Y);
        Assert.Equal(100, surface.RootElement.Children[0].Children[1].Size.Y);
        Assert.Same(surface.RootElement.Children[0].Children[1], surface.HitTest(new(10)));
        surface.Rebuild(ScrollView(controller: controller)[SizedBox(height: 20)]);
        surface.UpdateLayout();
        Assert.Equal(0, controller.Offset);
    }

    /// <summary>Checks exact-edge handoff applies only the remaining delta to the outer viewport.</summary>
    [Fact]
    public void NestedWheelConsumesResidualOnce()
    {
        using var outer = new ScrollController();
        using var inner = new ScrollController();
        using var surface = new UiSurface(ScrollView(controller: outer)[Stack()[ScrollView(controller: inner, height: 80)[SizedBox(height: 100)], SizedBox(height: 300)]]);
        surface.SetViewport(new(100));
        surface.UpdateLayout();
        Assert.True(surface.Dispatch(new UiWheelInput(new(10), new(0, 50))));
        Assert.Equal(20, inner.Offset);
        Assert.Equal(30, outer.Offset);
    }

    /// <summary>Checks nested bring-into-view uses the same published child offsets.</summary>
    [Fact]
    public void BringIntoViewRevealsTarget()
    {
        using var controller = new ScrollController();
        using var surface = new UiSurface(ScrollView(controller: controller)[Stack()[SizedBox(height: 200), Button(width: 100, height: 30)]]);
        surface.SetViewport(new(100));
        surface.UpdateLayout();
        UiElement target = surface.RootElement.Children[0].Children[1];
        Assert.True(surface.BringIntoView(target, new Rect2D(0, 0, 10, 30)));
        Assert.Equal(130, controller.Offset);
        Assert.Same(target, surface.HitTest(new(10, 90)));
    }

    /// <summary>Checks reuse of a passive declaration without a shared controller stays independent.</summary>
    [Fact]
    public void BorrowedControllerRejectsSecondSurfaceBeforePublication()
    {
        using var controller = new ScrollController();
        Definitions.ScrollView widget = ScrollView(controller: controller)[SizedBox(height: 300)];
        using var first = new UiSurface(widget);
        Assert.Throws<InvalidOperationException>(() => new UiSurface(widget));
        Assert.Same(widget, first.Root);
        first.SetViewport(new(100));
        first.UpdateLayout();
        controller.ScrollTo(80);
        Assert.Equal(80, controller.Offset);
    }

    /// <summary>Checks touch slop cancels a child click while mouse drag retains activation.</summary>
    [Fact]
    public void TouchPanTransfersButtonCaptureAndTerminates()
    {
        int clicks = 0;
        using var controller = new ScrollController();
        using var surface = new UiSurface(ScrollView(controller: controller)[Stack()[Button(width: 100, height: 100, onClick: () => clicks++), SizedBox(height: 300)]]);
        surface.SetViewport(new(100));
        surface.UpdateLayout();
        Assert.True(surface.Dispatch(new UiPointerInput(9, UiPointerPhase.Down, new(20, 80)) { Device = UiPointerDevice.Touch }));
        surface.Dispatch(new UiPointerInput(9, UiPointerPhase.Move, new(20, 30)) { Device = UiPointerDevice.Touch });
        Assert.Equal(50, controller.Offset);
        surface.Dispatch(new UiPointerInput(9, UiPointerPhase.Up, new(20, 30)) { Device = UiPointerDevice.Touch });
        Assert.Equal(0, clicks);
        controller.ScrollTo(0);
        surface.UpdateLayout();
        surface.Dispatch(new UiPointerInput(10, UiPointerPhase.Down, new(20, 80)));
        surface.Dispatch(new UiPointerInput(10, UiPointerPhase.Move, new(20, 30)));
        surface.Dispatch(new UiPointerInput(10, UiPointerPhase.Up, new(20, 30)));
        Assert.Equal(1, clicks);
        Assert.Equal(0, controller.Offset);
    }

    /// <summary>Checks callback exceptions occur after invalidation and duplicate bindings preserve the old tree.</summary>
    [Fact]
    public void CallbackAndFailedRebuildPreserveContracts()
    {
        using var controller = new ScrollController();
        using var surface = new UiSurface(ScrollView(controller: controller)[SizedBox(height: 300)]);
        surface.SetViewport(new(100));
        surface.UpdateLayout();
        controller.OnChanged = _ => throw new InvalidOperationException("callback");
        Assert.Throws<InvalidOperationException>(() => controller.ScrollTo(25));
        surface.UpdateLayout();
        Assert.Equal(-25, surface.RootElement.Children[0].Bounds.Y);
        Definitions.Widget old = surface.Root;
        surface.Rebuild(Panel()[ScrollView(controller: controller), ScrollView(controller: controller)]);
        Assert.Throws<InvalidOperationException>(() => surface.UpdateTree());
        Assert.Same(old, surface.Root);
    }

    /// <summary>Checks hidden viewports reset offsets and controller-free declarations remain independent.</summary>
    [Fact]
    public void HiddenAndIndependentSurfaceState()
    {
        Definitions.ScrollView widget = ScrollView()[SizedBox(height: 300)];
        using var first = new UiSurface(widget);
        using var second = new UiSurface(widget);
        first.SetViewport(new(100));
        second.SetViewport(new(100));
        first.UpdateLayout();
        second.UpdateLayout();
        first.Dispatch(new UiWheelInput(new(10), new(0, 30)));
        Assert.Equal(30, ((ScrollViewElement)first.RootElement).Controller.Offset);
        Assert.Equal(0, ((ScrollViewElement)second.RootElement).Controller.Offset);
        using var controller = new ScrollController();
        using var hidden = new UiSurface(ScrollView(controller: controller)[SizedBox(height: 300)]);
        hidden.SetViewport(new(100));
        hidden.UpdateLayout();
        controller.ScrollTo(50);
        hidden.Rebuild(ScrollView(controller: controller, isVisible: false)[SizedBox(height: 300)]);
        hidden.UpdateLayout();
        Assert.Equal(0, controller.Offset);
        Assert.Equal(0, controller.Extent);
    }

    /// <summary>Checks synchronous reentry, disposal and foreign-thread rejection.</summary>
    [Trait("Execution", "Integration")]
    [Fact]
    public void ControllerReentryDisposalAndOwnerThread()
    {
        using var controller = new ScrollController();
        using var surface = new UiSurface(ScrollView(controller: controller)[SizedBox(height: 300)]);
        surface.SetViewport(new(100));
        surface.UpdateLayout();
        int calls = 0;
        controller.OnChanged = value =>
        {
            calls++;
            if (value == 20)
            {
                controller.ScrollTo(40);
            }
        };
        controller.ScrollTo(20);
        Assert.Equal(2, calls);
        Assert.Equal(40, controller.Offset);
        Exception? foreignThreadError = null;
        var thread = new Thread(() => foreignThreadError = Record.Exception(() => controller.ScrollTo(50)));
        thread.Start();
        thread.Join();
        Assert.IsType<InvalidOperationException>(foreignThreadError);
        controller.OnChanged = _ => controller.Dispose();
        controller.ScrollTo(60);
        surface.UpdateLayout();
        Assert.False(controller.IsAttached);
        Assert.Throws<ObjectDisposedException>(() => controller.ScrollTo(0));
    }

    /// <summary>Checks finite cross constraints and one content layout per geometry change.</summary>
    [Fact]
    public void ContentIsLaidOutOnceWithUnboundedMainAxis()
    {
        int calls = 0;
        UiBoxConstraints observed = default;
        var child = new Probe(constraints =>
        {
            calls++;
            observed = constraints;
            return new(50, 300);
        });
        using var surface = new UiSurface(ScrollView()[child]);
        surface.SetViewport(new(100));
        surface.UpdateLayout();
        Assert.Equal(new UiBoxConstraints(0, 100, 0, float.PositiveInfinity), observed);
        Assert.Equal(1, calls);
        surface.Dispatch(new UiWheelInput(new(10), new(0, 40)));
        surface.UpdateLayout();
        Assert.Equal(1, calls);
    }

    /// <summary>Checks Signal rebuilds preserve a borrowed controller and do not write back declarations.</summary>
    [Fact]
    public void SignalExtentChangesClampWithoutImplicitSignalWrite()
    {
        var height = Signal.Create<float?>(300f);
        using var controller = new ScrollController();
        using var surface = new UiSurface(ScrollView(controller: controller)[SizedBox(width: 100, height: height)]);
        surface.SetViewport(new(100));
        surface.UpdateLayout();
        controller.ScrollTo(150);
        height.Value = 120;
        surface.UpdateLayout();
        Assert.Equal(20, controller.Offset);
        Assert.Equal(120, height.Value);
        UiAutomationNode content = surface.CaptureAutomationSnapshot().Nodes[1];
        Assert.Equal(-20, content.Bounds.Y);
        Assert.Equal(100, content.ClippedBounds.Height);
    }

    /// <summary>Checks nested reveal carries the clipped inner target to outer viewport.</summary>
    [Fact]
    public void NestedBringIntoViewUsesAppliedOffsets()
    {
        using var outer = new ScrollController();
        using var inner = new ScrollController();
        using var surface = new UiSurface(ScrollView(controller: outer)[Stack()[SizedBox(height: 100), ScrollView(height: 80, controller: inner)[Stack()[SizedBox(height: 200), Button(width: 50, height: 30)]]]]);
        surface.SetViewport(new(100));
        surface.UpdateLayout();
        UiElement target = surface.RootElement.Children[0].Children[1].Children[0].Children[1];
        surface.BringIntoView(target, new(0, 0, 50, 30));
        Assert.Equal(150, inner.Offset);
        Assert.Equal(80, outer.Offset);
        Assert.Same(target, surface.HitTest(new(10, 90)));
    }

    /// <summary>Checks horizontal offsets and decoration are applied exactly once to input and semantics.</summary>
    [Fact]
    public void HorizontalViewportUsesSharedModifierGeometry()
    {
        using var controller = new ScrollController();
        using var surface = new UiSurface(ScrollView(controller: controller, orientation: UiOrientation.Horizontal, with: [Padding.All(5)])[Stack(orientation: UiOrientation.Horizontal)[SizedBox(width: 100), Button(width: 50, height: 50)]]);
        surface.SetViewport(new(110, 60));
        surface.UpdateLayout();
        controller.ScrollTo(50);
        surface.UpdateLayout();
        UiElement button = surface.RootElement.Children[0].Children[1];
        UiAutomationNode node = surface.CaptureAutomationSnapshot().Nodes.Single(item => item.Id == button.AutomationId);
        Assert.Equal(55, node.Bounds.X);
        Assert.Equal(5, node.Bounds.Y);
        Assert.Same(button, surface.HitTest(new(60, 10)));
    }

    /// <summary>Checks focus reveal waits for the geometry update boundary.</summary>
    [Fact]
    public void FocusRevealIsDeferredUntilUpdateLayout()
    {
        using var controller = new ScrollController();
        using var surface = new UiSurface(ScrollView(controller: controller)[Stack()[SizedBox(height: 200), Button(width: 50, height: 30)]]);
        surface.SetViewport(new(100));
        surface.UpdateLayout();
        UiElement target = surface.RootElement.Children[0].Children[1];
        Assert.True(surface.TryFocus(target));
        Assert.Equal(0, controller.Offset);
        surface.UpdateLayout();
        Assert.Equal(130, controller.Offset);
        Assert.Same(target, surface.FocusedElement);
    }

    /// <summary>Checks borrowed lifetime ends on removal without disposing the owner controller.</summary>
    [Fact]
    public void RemovalDetachesBorrowedController()
    {
        using var controller = new ScrollController();
        using var surface = new UiSurface(ScrollView(controller: controller)[SizedBox(height: 300)]);
        surface.SetViewport(new(100));
        surface.UpdateLayout();
        surface.Rebuild(SizedBox());
        surface.UpdateLayout();
        Assert.False(controller.IsAttached);
        Assert.False(controller.IsDisposed);
        using var replacement = new UiSurface(ScrollView(controller: controller)[SizedBox(height: 200)]);
        replacement.SetViewport(new(100));
        replacement.UpdateLayout();
        controller.ScrollTo(50);
        Assert.Equal(50, controller.Offset);
    }

    /// <summary>Checks ordinary recording shares the published child offset.</summary>
    [Fact]
    public void RecordedTransformUsesExactScrollOffset()
    {
        using var controller = new ScrollController();
        using var surface = new UiSurface(ScrollView(controller: controller)[Rectangle(width: 100, height: 300, fill: new Lumyte.Mathematics.Colors.SRgba<float>(1, 0, 0, 1))]);
        surface.SetViewport(new(100));
        surface.UpdateLayout();
        controller.ScrollTo(40);
        var canvas = new Canvas2D();
        canvas.Begin();
        surface.Record(canvas);
        DrawList2D drawing = canvas.End();
        Assert.Equal(1, drawing.Count);
        Assert.Equal(-40, surface.RootElement.Children[0].Bounds.Y);
        Assert.Equal(-40, surface.CaptureAutomationSnapshot().Nodes[1].Bounds.Y);
    }

    /// <summary>Checks finite validation, empty content and zero viewport.</summary>
    [Fact]
    public void FiniteAndZeroContracts()
    {
        using var controller = new ScrollController();
        using var surface = new UiSurface(ScrollView(controller: controller));
        surface.UpdateLayout();
        Assert.Equal(Vector2.Zero, surface.RootElement.Size);
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.ScrollTo(float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => surface.Dispatch(new UiWheelInput(Vector2.Zero, new(float.PositiveInfinity))));
        using var invalid = new UiSurface(Stack()[ScrollView()[SizedBox(height: 10)]]);
        invalid.SetViewport(new(100));
        Assert.Throws<InvalidOperationException>(() => invalid.UpdateLayout());
    }

    /// <summary>Fully cross-clipped descendants cannot reveal an unrelated outer axis.</summary>
    [Fact]
    public void CrossClippedRectangleDoesNotScrollOuterViewport()
    {
        using var outer = new ScrollController();
        using var inner = new ScrollController();
        using var surface = new UiSurface(ScrollView(controller: outer, orientation: UiOrientation.Horizontal)[Stack(orientation: UiOrientation.Horizontal)[SizedBox(width: 120), ScrollView(controller: inner, width: 100)[SizedBox(height: 300)], SizedBox(width: 300)]]);
        surface.SetViewport(new(100));
        surface.UpdateLayout();
        UiElement target = surface.RootElement.Children[0].Children[1].Children[0];
        surface.BringIntoView(target, new Rect2D(150, 0, 10, 10));
        Assert.Equal(0, outer.Offset);
    }

    /// <summary>Update callbacks cannot steal new bindings and failed preparation releases reservations.</summary>
    [Fact]
    public void UpdateReentryAndPreparationRollbackPreserveOwnership()
    {
        using var first = new ScrollController();
        using var next = new ScrollController();
        Exception? rejected = null;
        using var surface = new UiSurface(Panel()[new ReentryWidget(() => GC.KeepAlive(first)), ScrollView(controller: first)]);
        surface.Rebuild(Panel()[new ReentryWidget(() => rejected = Record.Exception(() => new UiSurface(ScrollView(controller: next)))), ScrollView(controller: next)]);
        surface.UpdateTree();
        Assert.IsType<InvalidOperationException>(rejected);
        Assert.False(first.IsAttached);
        Assert.True(next.IsAttached);
        surface.Dispose();
        using var rebound = new UiSurface(ScrollView(controller: next));
        Assert.True(next.IsAttached);
    }

    /// <summary>Partial cross clipping reveals only the visible portion through padded orthogonal ancestors.</summary>
    /// <param name="padding">The inner modifier padding.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void PartialCrossClipRevealsOnlyVisibleRectangle(float padding)
    {
        using var outer = new ScrollController();
        using var inner = new ScrollController();
        using var surface = new UiSurface(ScrollView(controller: outer, orientation: UiOrientation.Horizontal)[Stack(orientation: UiOrientation.Horizontal)[SizedBox(width: 120), ScrollView(controller: inner, width: 100, with: [Padding.All(padding)])[SizedBox(height: 300)], SizedBox(width: 300)]]);
        surface.SetViewport(new(100));
        surface.UpdateLayout();
        UiElement target = surface.RootElement.Children[0].Children[1].Children[0];
        surface.BringIntoView(target, new Rect2D(85, 0, 50, 10));
        Assert.Equal(120 + (2 * padding), outer.Offset);
    }

    /// <summary>A later preparation error releases only the new reservation and leaves old bindings untouched.</summary>
    [Fact]
    public void FailedPreparationReleasesReservationWithoutChangingOldTree()
    {
        using var old = new ScrollController();
        using var candidate = new ScrollController();
        using var surface = new UiSurface(ScrollView(controller: old));
        Definitions.Widget previous = surface.Root;
        surface.Rebuild(Panel()[ScrollView(controller: candidate), new RejectPreparationWidget()]);
        Assert.Throws<InvalidOperationException>(surface.UpdateTree);
        Assert.Same(previous, surface.Root);
        Assert.True(old.IsAttached);
        Assert.False(candidate.IsAttached);
        using var other = new UiSurface(ScrollView(controller: candidate));
        Assert.True(candidate.IsAttached);
    }

    /// <summary>Reverse callback movement cannot create forward residual during negative wheel input.</summary>
    [Fact]
    public void NegativeWheelCallbackMovementKeepsResidualBounded()
    {
        using var outer = new ScrollController();
        using var inner = new ScrollController();
        using var surface = new UiSurface(ScrollView(controller: outer)[Stack()[SizedBox(height: 100), ScrollView(controller: inner, width: 100, height: 100)[SizedBox(width: 100, height: 300)], SizedBox(height: 300)]]);
        surface.SetViewport(new(100));
        surface.UpdateLayout();
        outer.ScrollTo(100);
        inner.ScrollTo(100);
        surface.UpdateLayout();
        inner.OnChanged = value =>
        {
            if (value == 50)
            {
                inner.ScrollTo(150);
            }
        };
        surface.Dispatch(new UiWheelInput(new(10), new(0, -50)));
        Assert.Equal(100, outer.Offset);
        Assert.Equal(150, inner.Offset);
    }

    /// <summary>Disposal and geometry reentry do not change accepted wheel consumption.</summary>
    [Fact]
    public void WheelCallbackDisposalDoesNotCreateAncestorResidual()
    {
        using var outer = new ScrollController();
        using var inner = new ScrollController();
        using var surface = new UiSurface(ScrollView(controller: outer)[Stack()[ScrollView(controller: inner, width: 100, height: 100)[SizedBox(width: 100, height: 300)], SizedBox(height: 300)]]);
        surface.SetViewport(new(100));
        surface.UpdateLayout();
        inner.OnChanged = _ =>
        {
            inner.Dispose();
            surface.UpdateLayout();
        };
        surface.Dispatch(new UiWheelInput(new(10), new(0, 50)));
        Assert.Equal(0, outer.Offset);
        Assert.True(inner.IsDisposed);
    }

    /// <summary>BringIntoView uses accepted movement even when its callback makes an independent movement.</summary>
    [Fact]
    public void BringIntoViewCallbackMovementDoesNotMoveOuterBackwards()
    {
        using var outer = new ScrollController();
        using var inner = new ScrollController();
        using var surface = new UiSurface(ScrollView(controller: outer)[Stack()[SizedBox(height: 100), ScrollView(controller: inner, width: 100, height: 100)[SizedBox(width: 100, height: 300)], SizedBox(height: 300)]]);
        surface.SetViewport(new(100));
        surface.UpdateLayout();
        outer.ScrollTo(100);
        surface.UpdateLayout();
        inner.OnChanged = value =>
        {
            if (value == 50)
            {
                inner.ScrollTo(100);
            }
        };
        UiElement target = surface.RootElement.Children[0].Children[1].Children[0];
        surface.BringIntoView(target, new(0, 140, 10, 10));
        Assert.Equal(100, outer.Offset);
        Assert.Equal(100, inner.Offset);
    }

    /// <summary>A non-scroll clipping ancestor also blocks reveal propagation.</summary>
    [Fact]
    public void GeneralClipToBoundsStopsFullyHiddenReveal()
    {
        using var controller = new ScrollController();
        using var surface = new UiSurface(ScrollView(controller: controller, orientation: UiOrientation.Horizontal)[Stack(orientation: UiOrientation.Horizontal)[SizedBox(width: 120), Panel(width: 100, clipToBounds: true)[SizedBox(width: 100, height: 100)], SizedBox(width: 300)]]);
        surface.SetViewport(new(100));
        surface.UpdateLayout();
        UiElement target = surface.RootElement.Children[0].Children[1].Children[0];
        surface.BringIntoView(target, new(150, 0, 10, 10));
        Assert.Equal(0, controller.Offset);
    }

    private sealed class Probe(Func<UiBoxConstraints, Vector2> layout) : Definitions.Widget
    {
        protected override UiElement CreateElement() => new ProbeElement(this, layout);
    }

    private sealed class ProbeElement(Probe widget, Func<UiBoxConstraints, Vector2> layout) : UiElement<Probe>(widget)
    {
        protected override Vector2 LayoutOverride(UiBoxConstraints constraints) => layout(constraints);
    }

    private sealed class ReentryWidget(Action callback) : Definitions.Widget
    {
        internal Action Callback { get; } = callback;

        protected override UiElement CreateElement() => new ReentryElement(this);
    }

    private sealed class ReentryElement(ReentryWidget widget) : UiElement<ReentryWidget>(widget)
    {
        protected override void OnMounted() => Widget.Callback();

        protected override void OnWidgetUpdated(ReentryWidget previous) => Widget.Callback();
    }

    private sealed class RejectPreparationWidget : Definitions.Widget
    {
        protected override UiElement CreateElement() => new RejectPreparationElement(this);
    }

    private sealed class RejectPreparationElement(RejectPreparationWidget widget) : UiElement<RejectPreparationWidget>(widget)
    {
        protected override Action? PrepareUpdate(RejectPreparationWidget next, UiElementUpdateContext context) => throw new InvalidOperationException("prepare");
    }
}
