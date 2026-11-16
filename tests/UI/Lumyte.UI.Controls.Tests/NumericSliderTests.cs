using Lumyte.Graphics.Drawing2D;
using Lumyte.Input.Abstractions;
using Lumyte.Signals;
using Xunit;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Controls.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Controls.Tests;

/// <summary>Exercises the actual numeric controls through normal surface input.</summary>
public sealed class NumericSliderTests
{
    /// <summary>Shared passive declarations do not share accepted values.</summary>
    [Fact]
    public void SharedDeclarationAndExplicitAdoption()
    {
        Compose.Definitions.Slider declaration = Slider(value: 0.2);
        using UiSurface first = Create(declaration);
        using UiSurface second = Create(declaration);
        Drag(first, 80);
        Assert.Equal(0.8, ((SliderElement)first.RootElement).Value, 12);
        Assert.Equal(0.2, ((SliderElement)second.RootElement).Value);
        Assert.Equal(0.2, declaration.Value);
        first.UpdateLayout();
        Assert.Equal(0.8, ((SliderElement)first.RootElement).Value, 12);
        UiElement old = first.RootElement;
        first.Rebuild(Slider(value: 0.2));
        first.UpdateTree();
        Assert.Same(old, first.RootElement);
        Assert.Equal(0.2, ((SliderElement)first.RootElement).Value);
    }

    /// <summary>Changes precede callbacks, completion happens once after central capture release.</summary>
    [Fact]
    public void DragSlopAndLatestCallbacks()
    {
        int changes = 0;
        int commits = 0;
        Compose.Definitions.Slider declaration = Slider();
        using UiSurface surface = Create(declaration);
        declaration.OnChanged = value =>
        {
            Assert.Equal(value, ((SliderElement)surface.RootElement).Value);
            changes++;
        };
        declaration.OnCommitted = _ => commits++;
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(10, 10)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Move, new(12, 10)));
        Assert.Equal(0, changes);
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Move, new(70, 10)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Move, new(70, 10)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(70, 10)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(70, 10)));
        Assert.Equal(1, changes);
        Assert.Equal(1, commits);
        Assert.False(surface.CaptureAutomationSnapshot().Nodes[0].HasActivePress);
        surface.Dispatch(new UiPointerInput(2, UiPointerPhase.Down, new(70, 10)));
        surface.Dispatch(new UiPointerInput(2, UiPointerPhase.Up, new(70, 10)));
        Assert.Equal(2, commits);
    }

    /// <summary>All cancellation paths retain the last accepted value without a false completion.</summary>
    /// <param name="mode">The cancellation path.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void CancelDoesNotCommit(int mode)
    {
        int commits = 0;
        using UiSurface surface = Create(Slider(onCommitted: _ => commits++));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(10, 10)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Move, new(80, 10)));
        if (mode == 0)
        {
            surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Cancel, default));
        }
        else if (mode == 1)
        {
            surface.Dispatch(new UiKeyInput(Key.Escape, true));
        }
        else if (mode == 2)
        {
            surface.Dispatch(new UiFocusLostInput());
        }
        else
        {
            surface.Rebuild(Slider(value: 0.8, isEnabled: false, onCommitted: _ => commits++));
            surface.UpdateLayout();
        }

        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(80, 10)));
        Assert.Equal(0, commits);
        Assert.Equal(0.8, ((SliderElement)surface.RootElement).Value, 12);
    }

    /// <summary>Keyboard repeat belongs to one gesture, and Escape cancels its completion.</summary>
    [Fact]
    public void KeyboardRepeatEndpointsAndCancel()
    {
        int commits = 0;
        using UiSurface surface = Create(Slider(step: 0.1, onCommitted: _ => commits++));
        surface.TryFocus(surface.RootElement);
        surface.Dispatch(new UiKeyInput(Key.ArrowRight, true));
        surface.Dispatch(new UiKeyInput(Key.ArrowRight, true, true));
        surface.Dispatch(new UiKeyInput(Key.ArrowRight, false));
        Assert.Equal(0.2, ((SliderElement)surface.RootElement).Value, 12);
        Assert.Equal(1, commits);
        surface.Dispatch(new UiKeyInput(Key.End, true));
        surface.Dispatch(new UiKeyInput(Key.Escape, true));
        surface.Dispatch(new UiKeyInput(Key.End, false));
        Assert.Equal(1, ((SliderElement)surface.RootElement).Value);
        Assert.Equal(1, commits);
        surface.Dispatch(new UiKeyInput(Key.Home, true));
        surface.Dispatch(new UiKeyInput(Key.Home, false));
        Assert.Equal(0, ((SliderElement)surface.RootElement).Value);
        Assert.Equal(2, commits);
    }

    /// <summary>Both thumbs remain reachable at the same position and never exchange identities.</summary>
    [Fact]
    public void RangeOwnerKeepsThumbIdentityAndAtomicValues()
    {
        var changes = new List<SliderRange>();
        using UiSurface surface = Create(RangeSlider(value: new SliderRange(0.5, 0.5), step: 0.1, onChanged: changes.Add));
        surface.TryFocus(surface.RootElement);
        surface.Dispatch(new UiKeyInput(Key.End, true));
        surface.Dispatch(new UiKeyInput(Key.End, false));
        Assert.Empty(changes);
        surface.Dispatch(new UiKeyInput(Key.Space, true));
        surface.Dispatch(new UiKeyInput(Key.End, true));
        surface.Dispatch(new UiKeyInput(Key.End, false));
        Assert.Equal(new SliderRange(0.5, 1), ((RangeSliderElement)surface.RootElement).Value);
        Assert.True(((RangeSliderElement)surface.RootElement).IsUpperThumbSelected);
        Assert.Single(surface.CaptureAutomationSnapshot().Nodes);
        Assert.Single(changes);
    }

    /// <summary>Huge ranges, equal endpoints, tiny steps and endpoint stability remain finite.</summary>
    /// <param name="minimum">The lower bound.</param>
    /// <param name="maximum">The upper bound.</param>
    /// <param name="step">The finite positive step.</param>
    [Theory]
    [InlineData(-double.MaxValue, double.MaxValue, double.Epsilon)]
    [InlineData(-double.MaxValue, double.MaxValue, double.MaxValue)]
    [InlineData(1, 1, 0.1)]
    [InlineData(0, double.Epsilon, double.Epsilon)]
    public void ExtremeNumbersRemainFinite(double minimum, double maximum, double step)
    {
        using UiSurface surface = Create(Slider(min: minimum, max: maximum, step: step));
        Drag(surface, 50);
        Assert.True(double.IsFinite(((SliderElement)surface.RootElement).Value));
        Assert.InRange(((SliderElement)surface.RootElement).Value, minimum, maximum);
        surface.Dispatch(new UiKeyInput(Key.End, true));
        surface.Dispatch(new UiKeyInput(Key.End, false));
        Assert.Equal(maximum, ((SliderElement)surface.RootElement).Value);
        surface.Dispatch(new UiKeyInput(Key.Home, true));
        surface.Dispatch(new UiKeyInput(Key.Home, false));
        Assert.Equal(minimum, ((SliderElement)surface.RootElement).Value);
        Assert.True(double.IsFinite(surface.CaptureAutomationSnapshot().Nodes[0].Value!.Value));
    }

    /// <summary>Midpoints round toward the greater lattice value and explicit endpoints remain reachable.</summary>
    [Fact]
    public void SnappingAndEndpointsAreExplicit()
    {
        using UiSurface surface = Create(Slider(max: 10, step: 3));
        Drag(surface, 45);
        Assert.Equal(6, ((SliderElement)surface.RootElement).Value);
        Drag(surface, 100);
        Assert.Equal(10, ((SliderElement)surface.RootElement).Value);
    }

    /// <summary>Pressing the drawn endpoint thumb never jumps and dragging retains its press offset.</summary>
    [Fact]
    public void DrawnThumbPressKeepsEndpointAndAnchor()
    {
        using UiSurface surface = Create(Slider(value: 1));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(95, 10)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(95, 10)));
        Assert.Equal(1, ((SliderElement)surface.RootElement).Value);
        surface.Dispatch(new UiPointerInput(2, UiPointerPhase.Down, new(95, 10)));
        surface.Dispatch(new UiPointerInput(2, UiPointerPhase.Move, new(85, 10)));
        surface.Dispatch(new UiPointerInput(2, UiPointerPhase.Up, new(85, 10)));
        Assert.Equal(0.9, ((SliderElement)surface.RootElement).Value, 12);
    }

    /// <summary>Actual numeric drawing uses the same retained body and clip without extra owned parts.</summary>
    [Fact]
    public void NumericDrawingReusesCleanContentAndInvalidatesAcceptedValues()
    {
        using UiSurface surface = Create(Slider(width: 80, height: 20, clipToBounds: true, with: [Padding.All(5)]));
        using UiDrawingScene2D scene = surface.CreateDrawingScene();
        scene.Update();
        Assert.Equal(1, scene.LastRecordingCount);
        scene.Update();
        Assert.Equal(0, scene.LastRecordingCount);
        var canvas = new Canvas2D();
        canvas.Begin();
        surface.Record(canvas);
        Assert.True(canvas.End().Count >= 2);
        Assert.True(surface.Dispatch(new UiSetNumericInput(surface.RootElement.AutomationId, 0.75, null, new(15, 15))));
        scene.Update();
        Assert.Equal(1, scene.LastRecordingCount);
        scene.Update();
        Assert.Equal(0, scene.LastRecordingCount);
        surface.SetViewport(new(40, 15));
        scene.Update();
        Assert.Equal(30, surface.RootElement.ContentSize.X);
        Assert.Equal(5, surface.RootElement.ContentSize.Y);
        Assert.Empty(surface.RootElement.Children);
        Assert.Single(surface.CaptureAutomationSnapshot().Nodes);
        Assert.False(surface.Dispatch(new UiSetNumericInput(surface.RootElement.AutomationId, 1, null, new(2))));
    }

    /// <summary>The smallest positive step does not advance an already exact lattice value.</summary>
    [Fact]
    public void DenormalExactLatticeDoesNotAdvance()
    {
        using UiSurface surface = Create(Slider(max: double.Epsilon * 4, step: double.Epsilon));
        Assert.True(surface.Dispatch(new UiSetNumericInput(surface.RootElement.AutomationId, double.Epsilon * 2, null, new(5))));
        Assert.Equal(double.Epsilon * 2, ((SliderElement)surface.RootElement).Value);
    }

    /// <summary>Subnormal half-step rounding must not choose a farther lattice point.</summary>
    [Fact]
    public void SubnormalMidpointComparisonDoesNotRoundTheHalfStep()
    {
        using UiSurface surface = Create(Slider(max: double.Epsilon * 10, step: double.Epsilon * 5));
        surface.Dispatch(new UiSetNumericInput(surface.RootElement.AutomationId, double.Epsilon * 2, null, new(5)));
        Assert.Equal(0, ((SliderElement)surface.RootElement).Value);
    }

    /// <summary>A captured key-up terminates its gesture even when modifier state changed while held.</summary>
    [Fact]
    public void ModifiedCapturedKeyUpAlwaysCompletesAndReleases()
    {
        int commits = 0;
        using UiSurface surface = Create(Slider(onCommitted: _ => commits++));
        surface.TryFocus(surface.RootElement);
        surface.Dispatch(new UiKeyInput(Key.ArrowRight, true));
        surface.Dispatch(new UiKeyInput(Key.ArrowRight, false, Modifiers: UiModifiers.Shift));
        Assert.Equal(1, commits);
        Assert.False(surface.CaptureAutomationSnapshot().Nodes[0].HasActivePress);
        surface.Dispatch(new UiKeyInput(Key.End, true));
        surface.Dispatch(new UiKeyInput(Key.End, false));
        Assert.Equal(1, ((SliderElement)surface.RootElement).Value);
        Assert.Equal(2, commits);
    }

    /// <summary>Escape releases the old pointer before a new series, even before its orphan Up.</summary>
    [Fact]
    public void EscapeReleasesPointerBeforeTheNextGesture()
    {
        int commits = 0;
        using UiSurface surface = Create(Slider(onCommitted: _ => commits++));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(10, 10)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Move, new(40, 10)));
        surface.Dispatch(new UiKeyInput(Key.Escape, true));
        surface.Dispatch(new UiPointerInput(2, UiPointerPhase.Down, new(40, 10)));
        surface.Dispatch(new UiPointerInput(2, UiPointerPhase.Move, new(70, 10)));
        surface.Dispatch(new UiPointerInput(2, UiPointerPhase.Up, new(70, 10)));
        Assert.Equal(1, commits);
        Assert.Equal(0.7, ((SliderElement)surface.RootElement).Value, 12);
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(40, 10)));
        Assert.Equal(1, commits);
    }

    /// <summary>Throwing callbacks release capture without rolling back the accepted state.</summary>
    [Fact]
    public void CallbackFailureAndRebuildDoNotLeakCapture()
    {
        Compose.Definitions.Slider declaration = Slider(onChanged: _ => throw new InvalidOperationException("user"));
        using UiSurface surface = Create(declaration);
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(10, 10)));
        Assert.Throws<InvalidOperationException>(() => surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Move, new(80, 10))));
        Assert.Equal(0.8, ((SliderElement)surface.RootElement).Value, 12);
        Assert.False(surface.CaptureAutomationSnapshot().Nodes[0].HasActivePress);
        declaration.OnChanged = _ => surface.Rebuild(Slider(value: 0.3));
        Drag(surface, 60);
        surface.UpdateTree();
        Assert.Equal(0.3, ((SliderElement)surface.RootElement).Value);
    }

    /// <summary>Reactive declarations silently adopt values without mutating either shared declaration.</summary>
    [Fact]
    public void SignalAdoptionAndRejectedProjection()
    {
        var value = Signal.Create(0.2);
        using UiSurface surface = Create(Slider(value: value));
        Drag(surface, 80);
        Assert.Equal(0.2, value.Value);
        value.Value = 0.4;
        surface.UpdateTree();
        Assert.Equal(0.4, ((SliderElement)surface.RootElement).Value);
        UiElement old = surface.RootElement;
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            surface.Rebuild(Slider(min: 2, max: 1));
            surface.UpdateTree();
        });
        Assert.Same(old, surface.RootElement);
        Assert.Equal(0.4, ((SliderElement)old).Value);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            surface.Rebuild(RangeSlider(value: new SliderRange(1, 0)));
            surface.UpdateTree();
        });
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            surface.Rebuild(Slider(value: double.NaN));
            surface.UpdateTree();
        });
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            surface.Rebuild(Slider(step: 0));
            surface.UpdateTree();
        });
    }

    /// <summary>Sliders retain touch gestures inside real scrolling content and have no hidden children.</summary>
    [Fact]
    public void TouchDragPreservesScrollAndModifierBody()
    {
        using var controller = new ScrollController();
        using UiSurface surface = Create(ScrollView(controller: controller)[Stack()[
            Slider(width: 80, height: 20, with: [Padding.All(5)]), SizedBox(height: 300)]]);
        surface.Dispatch(new UiPointerInput(2, UiPointerPhase.Down, new(15, 15)) { Device = UiPointerDevice.Touch });
        surface.Dispatch(new UiPointerInput(2, UiPointerPhase.Move, new(65, 40)) { Device = UiPointerDevice.Touch });
        surface.Dispatch(new UiPointerInput(2, UiPointerPhase.Up, new(65, 40)) { Device = UiPointerDevice.Touch });
        Assert.Equal(0, controller.Offset);
        SliderElement element = Assert.IsType<SliderElement>(surface.RootElement.Children[0].Children[0]);
        Assert.Empty(element.Children);
        Assert.Equal(0.75, element.Value, 12);
    }

    /// <summary>A prior editor's commit callback may dispose the surface before a numeric command obtains focus.</summary>
    /// <param name="range">Whether the target is the real range owner.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FocusTransferDisposalNeverWritesTheDisconnectedNumericOwner(bool range)
    {
        UiSurface? current = null;
        int changed = 0;
        var editor = new Lumyte.UI.TextEditing.Compose.Definitions.NumericBox { Value = 1, Width = 100, Height = 20, OnCommitted = _ => current!.Dispose() };
        Widget target = range ? RangeSlider(height: 20, onChanged: _ => changed++) : Slider(height: 20, onChanged: _ => changed++);
        using UiSurface surface = Create(Stack()[editor, target]);
        current = surface;
        var numeric = (Lumyte.UI.TextEditing.NumericBoxElement)surface.RootElement.Children[0];
        UiElement slider = surface.RootElement.Children[1];
        surface.TryFocus(numeric);
        Lumyte.TextEditing.TextEditingCommands.ReplaceAll(numeric.Document, "2", Lumyte.TextEditing.TextEditOrigin.Keyboard);
        Assert.False(surface.Dispatch(new UiSetNumericInput(slider.AutomationId, 0.5, range ? 0.8 : null, new(10, 25))));
        Assert.Equal(0, changed);
        Assert.False(slider.IsMounted);
    }

    /// <summary>Numeric commands cannot be granted by roles or redirected to sibling targets.</summary>
    [Fact]
    public void NumericDispatchChecksCapabilityAndLiveHit()
    {
        using UiSurface fake = Create(Rectangle(role: UiRole.Slider));
        Assert.False(fake.Dispatch(new UiSetNumericInput(fake.RootElement.AutomationId, 0.5, null, new(5))));
        using UiSurface surface = Create(Stack()[Slider(height: 20), Slider(height: 20)]);
        UiElement first = surface.RootElement.Children[0];
        Assert.False(surface.Dispatch(new UiSetNumericInput(first.AutomationId, 0.5, null, new(5, 25))));
        Assert.True(surface.Dispatch(new UiSetNumericInput(first.AutomationId, 0.5, null, new(5))));
        Assert.Equal(0.5, ((SliderElement)first).Value);
        Assert.Throws<ArgumentOutOfRangeException>(() => surface.Dispatch(new UiSetNumericInput(first.AutomationId, double.PositiveInfinity, null, new(5))));
    }

    private static UiSurface Create(Widget widget)
    {
        var surface = new UiSurface(widget);
        surface.SetViewport(new(100, 50));
        surface.UpdateLayout();
        return surface;
    }

    private static void Drag(UiSurface surface, float x)
    {
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(10, 10)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Move, new(x, 10)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(x, 10)));
    }
}
