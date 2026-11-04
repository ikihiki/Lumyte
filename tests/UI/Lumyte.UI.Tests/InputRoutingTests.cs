using System.Numerics;
using Xunit;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Tests;

/// <summary>Checks component-defined input handling without a concrete control dependency in the surface.</summary>
public sealed class InputRoutingTests : UiElementTestBase
{
    /// <summary>Checks an editor can consume Down, capture selection drag and receive terminal Up before its containing Button.</summary>
    [Fact]
    public void EditorInsideButtonOwnsItsDragAndReceivesLocalCoordinates()
    {
        int clicks = 0;
        var events = new List<(UiPointerPhase Phase, bool Captured, Vector2? Position)>();
        var states = new List<UiPointerCaptureState>();
        var editor = new Control { Width = 20, Height = 10, CaptureChanged = states.Add };
        editor.Handle = context =>
        {
            if (context.Input is not UiPointerInput pointer)
            {
                return UiInputResult.Unhandled;
            }

            events.Add((pointer.Phase, context.IsCaptured, context.PointerPosition));
            if (pointer.Phase == UiPointerPhase.Down)
            {
                Assert.True(context.CapturePointer());
            }

            return UiInputResult.Handled;
        };
        Definitions.Button button = Button(onClick: () => clicks++)[editor];
        using UiSurface surface = Surface(Stack()[Panel(height: 10), button]);
        Assert.True(Send(surface, UiPointerPhase.Down, new(2, 12)));
        Assert.False(Pressed(button));
        Assert.True(Send(surface, UiPointerPhase.Move, new(70, 80)));
        Assert.True(Send(surface, UiPointerPhase.Up, new(70, 80)));
        Assert.Equal(3, events.Count);
        Assert.Equal((UiPointerPhase.Down, false, (Vector2?)new Vector2(2, 2)), events[0]);
        Assert.Equal((UiPointerPhase.Move, true, (Vector2?)new Vector2(70, 70)), events[1]);
        Assert.True(events[2].Captured);
        Assert.False(states[^1].IsCaptured);
        Assert.Equal(0, clicks);
        Assert.False(Send(surface, UiPointerPhase.Cancel, Vector2.Zero));
    }

    /// <summary>Checks normal bubbling and explicit blocking are separate from consumption.</summary>
    /// <param name="result">The child's propagation decision.</param>
    /// <param name="clicks">The expected parent Button activations.</param>
    [Theory]
    [InlineData(UiInputResult.Unhandled, 1)]
    [InlineData(UiInputResult.Handled, 0)]
    [InlineData(UiInputResult.StopPropagation, 0)]
    public void ChildControlsConsumptionAndBubbling(UiInputResult result, int clicks)
    {
        int actual = 0;
        var child = new Control { Width = 20, Height = 10, Handle = _ => result };
        using UiSurface surface = Surface(Panel()[Button(onClick: () => actual++)[child]]);
        Assert.Equal(result != UiInputResult.StopPropagation, Send(surface, UiPointerPhase.Down, new(1, 1)));
        Send(surface, UiPointerPhase.Up, new(1, 1));
        Assert.Equal(clicks, actual);
    }

    /// <summary>Checks generic controls can capture multiple pointers; Button's single-owner rule is not imposed by the surface.</summary>
    [Fact]
    public void MultiplePointersCanBeCapturedByTheSameCustomControl()
    {
        var cancelled = new List<long>();
        var control = new Control { Width = 20, Height = 10 };
        control.Handle = context =>
        {
            if (context.Input is UiPointerInput pointer)
            {
                if (pointer.Phase == UiPointerPhase.Down)
                {
                    Assert.True(context.CapturePointer());
                }
                else if (pointer.Phase == UiPointerPhase.Cancel)
                {
                    Assert.True(context.IsCaptured);
                    cancelled.Add(pointer.PointerId);
                }

                return UiInputResult.Handled;
            }

            return UiInputResult.Unhandled;
        };
        using UiSurface surface = Surface(control);
        Assert.True(Send(surface, UiPointerPhase.Down, new(1, 1), 1));
        Assert.True(Send(surface, UiPointerPhase.Down, new(1, 1), 2));
        Assert.True(surface.Dispatch(new UiFocusLostInput()));
        Assert.Equal(new long[] { 1, 2 }, cancelled.Order());
        Assert.False(Send(surface, UiPointerPhase.Cancel, Vector2.Zero, 1));
        Assert.False(Send(surface, UiPointerPhase.Cancel, Vector2.Zero, 2));
    }

    /// <summary>Checks geometry refresh reaches the captured component without synthesizing a physical Move.</summary>
    [Fact]
    public void CaptureNotificationsRefreshGeometryWithoutMoveInput()
    {
        var states = new List<UiPointerCaptureState>();
        int moves = 0;
        var control = new Control { CaptureChanged = states.Add };
        control.Handle = context =>
        {
            if (context.Input is UiPointerInput { Phase: UiPointerPhase.Down })
            {
                context.CapturePointer();
            }
            else if (context.Input is UiPointerInput { Phase: UiPointerPhase.Move })
            {
                moves++;
            }

            return UiInputResult.Handled;
        };
        using UiSurface surface = Surface(control);
        Send(surface, UiPointerPhase.Down, new(90, 90));
        Assert.True(states[^1].IsInside);
        surface.SetViewport(new(10, 10));
        surface.UpdateLayout();
        Assert.True(states[^1].IsCaptured);
        Assert.False(states[^1].IsInside);
        Assert.Equal(0, moves);
    }

    /// <summary>Checks explicit release notifies cancellation and cannot leave a stale capture.</summary>
    [Fact]
    public void ComponentCanReleaseCaptureDuringInput()
    {
        int cancelled = 0;
        var control = new Control();
        control.Handle = context =>
        {
            switch (context.Input)
            {
                case UiPointerInput { Phase: UiPointerPhase.Down }:
                    context.CapturePointer();
                    return UiInputResult.Handled;
                case UiPointerInput { Phase: UiPointerPhase.Move }:
                    Assert.True(context.ReleasePointerCapture());
                    return UiInputResult.Handled;
                case UiPointerInput { Phase: UiPointerPhase.Cancel }:
                    cancelled++;
                    return UiInputResult.Handled;
                default:
                    return UiInputResult.Unhandled;
            }
        };
        using UiSurface surface = Surface(control);
        Send(surface, UiPointerPhase.Down, new(1, 1));
        Assert.True(Send(surface, UiPointerPhase.Move, new(1, 1)));
        Assert.Equal(1, cancelled);
        Assert.False(Send(surface, UiPointerPhase.Cancel, Vector2.Zero));
    }

    /// <summary>Checks future non-pointer input types can use the same component hook; focus targeting is added by the keyboard PR.</summary>
    [Fact]
    public void NonPointerInputUsesTheSameWidgetHook()
    {
        string text = string.Empty;
        var control = new Control
        {
            Handle = context =>
            {
                if (context.Input is CommitText commit)
                {
                    Assert.Null(context.PointerPosition);
                    text += commit.Text;
                    return UiInputResult.Handled;
                }

                return UiInputResult.Unhandled;
            },
        };
        using UiSurface surface = Surface(control);
        Assert.True(surface.Dispatch(new CommitText("日本語")));
        Assert.Equal("日本語", text);
    }

    /// <summary>Checks exceptions after capture acquisition remove ownership and notify the component.</summary>
    [Fact]
    public void ThrowingHandlerDoesNotLeaveCapture()
    {
        bool captured = false;
        var control = new Control { CaptureChanged = state => captured = state.IsCaptured };
        control.Handle = context =>
        {
            if (context.Input is UiPointerInput { Phase: UiPointerPhase.Down })
            {
                context.CapturePointer();
                throw new ArithmeticException("fixture");
            }

            return UiInputResult.Unhandled;
        };
        using UiSurface surface = Surface(control);
        Assert.Throws<ArithmeticException>(() => Send(surface, UiPointerPhase.Down, new(1, 1)));
        Assert.False(captured);
        Assert.False(Send(surface, UiPointerPhase.Cancel, Vector2.Zero));
    }

    /// <summary>Checks disposal disconnects all widgets and notifies every removed capture even if one handler throws.</summary>
    [Fact]
    public void DisposalCleansOtherCapturesAndDisconnectsDespiteNotificationFailure()
    {
        int lost = 0;
        var control = new Control();
        control.Handle = context =>
        {
            if (context.Input is UiPointerInput { Phase: UiPointerPhase.Down })
            {
                context.CapturePointer();
            }

            return UiInputResult.Handled;
        };
        control.CaptureChanged = state =>
        {
            if (!state.IsCaptured)
            {
                lost++;
                if (state.PointerId == 1)
                {
                    throw new ArithmeticException("fixture");
                }
            }
        };
        using UiSurface surface = Surface(control);
        Send(surface, UiPointerPhase.Down, new(1, 1), 1);
        Send(surface, UiPointerPhase.Down, new(1, 1), 2);
        Assert.Throws<ArithmeticException>(surface.Dispose);
        Assert.Equal(2, lost);
        using UiSurface next = Mount(control);
        Assert.Throws<ObjectDisposedException>(() => surface.Dispatch(new UiFocusLostInput()));
    }

    /// <summary>Checks capture refresh allows a component to cancel another pointer without invalidating the iteration.</summary>
    [Fact]
    public void CaptureRefreshCanReenterCancellation()
    {
        UiSurface? surface = null;
        bool armed = false;
        var control = new Control();
        control.Handle = context =>
        {
            if (context.Input is UiPointerInput { Phase: UiPointerPhase.Down })
            {
                context.CapturePointer();
            }

            return UiInputResult.Handled;
        };
        control.CaptureChanged = state =>
        {
            if (armed && state is { PointerId: 1, IsCaptured: true })
            {
                Assert.True(Send(surface!, UiPointerPhase.Cancel, Vector2.Zero, 2));
            }
        };
        using UiSurface connected = Surface(control);
        surface = connected;
        Send(connected, UiPointerPhase.Down, new(1, 1), 1);
        Send(connected, UiPointerPhase.Down, new(1, 1), 2);
        armed = true;
        connected.SetViewport(new(90, 90));
        connected.UpdateLayout();
        Assert.False(Send(connected, UiPointerPhase.Cancel, Vector2.Zero, 2));
        Assert.True(Send(connected, UiPointerPhase.Up, new(1, 1), 1));
    }

    /// <summary>Checks a child can dispose the surface without subsequent delivery to its former parent.</summary>
    [Fact]
    public void DisposingDuringBubblingStopsParentDelivery()
    {
        var child = new Control { Width = 20, Height = 10 };
        using UiSurface surface = Surface(Button()[child]);
        child.Handle = _ =>
        {
            surface.Dispose();
            return UiInputResult.Unhandled;
        };
        Assert.False(Send(surface, UiPointerPhase.Down, new(1, 1)));
        Assert.Throws<ObjectDisposedException>(() => surface.Dispatch(new UiFocusLostInput()));
    }

    /// <summary>Checks cancellation failures are aggregated only after every capture has been removed and notified.</summary>
    [Fact]
    public void FocusLossCancelsAllPointersDespiteMultipleHandlerFailures()
    {
        int cancelled = 0;
        var control = new Control();
        control.Handle = context =>
        {
            if (context.Input is UiPointerInput { Phase: UiPointerPhase.Down })
            {
                context.CapturePointer();
            }
            else if (context.Input is UiPointerInput { Phase: UiPointerPhase.Cancel })
            {
                cancelled++;
                throw new ArithmeticException("fixture");
            }

            return UiInputResult.Handled;
        };
        using UiSurface surface = Surface(control);
        Send(surface, UiPointerPhase.Down, new(1, 1), 1);
        Send(surface, UiPointerPhase.Down, new(1, 1), 2);
        AggregateException failure = Assert.Throws<AggregateException>(() => surface.Dispatch(new UiFocusLostInput()));
        Assert.Equal(2, failure.InnerExceptions.Count);
        Assert.Equal(2, cancelled);
        Assert.False(Send(surface, UiPointerPhase.Cancel, Vector2.Zero, 1));
        Assert.False(Send(surface, UiPointerPhase.Cancel, Vector2.Zero, 2));
    }

    /// <summary>Checks a failed activation does not cancel a new pointer series started reentrantly after terminal release.</summary>
    [Fact]
    public void FailedClickDoesNotCancelReentrantNewPointerSeries()
    {
        Definitions.Button button = Button()[Panel(width: 20, height: 10)];
        using UiSurface surface = Surface(Panel()[button]);
        button.OnClick = () =>
        {
            button.OnClick = null;
            Assert.True(Send(surface, UiPointerPhase.Down, new(1, 1)));
            throw new ArithmeticException("fixture");
        };
        Send(surface, UiPointerPhase.Down, new(1, 1));
        Assert.Throws<ArithmeticException>(() => Send(surface, UiPointerPhase.Up, new(1, 1)));
        Assert.True(Pressed(button));
        Assert.True(Send(surface, UiPointerPhase.Cancel, Vector2.Zero));
        Assert.False(Pressed(button));
    }

    private static bool Send(UiSurface surface, UiPointerPhase phase, Vector2 position, long id = 0) =>
        surface.Dispatch(new UiPointerInput(id, phase, position));

    private UiSurface Surface(Definitions.Widget root)
    {
        UiSurface surface = Mount(root);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        return surface;
    }

    private sealed record CommitText(string Text) : UiInput;

    private sealed class Control : Definitions.Panel
    {
        public Func<UiInputContext, UiInputResult>? Handle { get; set; }

        public Action<UiPointerCaptureState>? CaptureChanged { get; set; }

        protected override UiElement CreateElement() => new ControlElement(this);
    }

    private sealed class ControlElement(Control widget) : PanelElement(widget)
    {
        protected override UiInputResult OnInput(in UiInputContext context) => ((Control)Widget).Handle?.Invoke(context) ?? UiInputResult.Unhandled;

        protected override void OnPointerCaptureChanged(UiPointerCaptureState state) => ((Control)Widget).CaptureChanged?.Invoke(state);
    }
}
