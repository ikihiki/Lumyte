using System.Numerics;

using Lumyte.Core.Time;

using Xunit;

using static Lumyte.Interaction.InteractionKit;

namespace Lumyte.Interaction.Tests;

public sealed class GestureRuntimeTests
{
    [Fact]
    public void EmulatedTapRecognizesTheBoundCommand()
    {
        var select = new Command("editor.select");
        GestureMap map = GestureMap("Viewport")[
            new TapGesture(select)
        ];
        var touchscreen = new VirtualTouchscreen();
        using var runtime = CreateRuntime(touchscreen, map);
        GestureRecognizedEventArgs? recognized = null;
        runtime.Recognized += (_, eventArgs) => recognized = eventArgs;

        touchscreen.Begin(1, new(20, 30));
        touchscreen.End(1, new(22, 31));

        Assert.NotNull(recognized);
        Assert.Same(select, recognized.Intent);
        Assert.Equal(GestureKind.Tap, recognized.Gesture);
    }

    [Fact]
    public void EmulatedDragReportsItsTotalDelta()
    {
        var pan = new Command("editor.pan");
        GestureMap map = GestureMap("Viewport")[
            new DragGesture(pan, minimumDistance: 5)
        ];
        var touchscreen = new VirtualTouchscreen();
        using var runtime = CreateRuntime(touchscreen, map);
        GestureRecognizedEventArgs? recognized = null;
        runtime.Recognized += (_, eventArgs) => recognized = eventArgs;

        touchscreen.Begin(1, new(10, 10));
        touchscreen.Move(1, new(14, 12));
        touchscreen.End(1, new(20, 15));

        Assert.NotNull(recognized);
        Assert.Equal(GestureKind.Drag, recognized.Gesture);
        Assert.Equal(new Vector2(10, 5), recognized.Delta);
    }

    [Fact]
    public void EmulatedPinchReportsScaleFromTwoTouches()
    {
        var zoom = new Command("editor.zoom");
        GestureMap map = GestureMap("Viewport")[
            new PinchGesture(zoom)
        ];
        var touchscreen = new VirtualTouchscreen();
        using var runtime = CreateRuntime(touchscreen, map);
        GestureRecognizedEventArgs? recognized = null;
        runtime.Recognized += (_, eventArgs) => recognized = eventArgs;

        touchscreen.Begin(1, new(0, 0));
        touchscreen.Begin(2, new(10, 0));
        touchscreen.Move(2, new(20, 0));

        Assert.NotNull(recognized);
        Assert.Equal(GestureKind.Pinch, recognized.Gesture);
        Assert.Equal(2, recognized.Scale);
    }

    [Fact]
    public void DoubleTapUsesTheInjectedMonotonicClock()
    {
        var open = new Command("editor.open");
        GestureMap map = GestureMap("Viewport")[
            new DoubleTapGesture(
                open,
                maximumMovement: 10,
                maximumInterval: TimeSpan.FromMilliseconds(300))
        ];
        var touchscreen = new VirtualTouchscreen();
        var clock = new ManualClock();
        using var runtime = new GestureRuntime(
            touchscreen,
            new InteractionContext(),
            clock,
            map);
        var recognized = new List<GestureRecognizedEventArgs>();
        runtime.Recognized += (_, eventArgs) => recognized.Add(eventArgs);

        touchscreen.Begin(1, new(10, 10));
        touchscreen.End(1, new(10, 10));
        clock.Advance(Duration.FromTimeSpan(TimeSpan.FromMilliseconds(200)));
        touchscreen.Begin(2, new(12, 10));
        touchscreen.End(2, new(12, 10));

        GestureRecognizedEventArgs actual = Assert.Single(recognized);
        Assert.Same(open, actual.Intent);
        Assert.Equal(GestureKind.DoubleTap, actual.Gesture);
    }

    [Fact]
    public void EmulatedFastMovementRecognizesDirectionalSwipe()
    {
        var navigateBack = new Command("editor.navigateBack");
        GestureMap map = GestureMap("Editor")[
            new SwipeGesture(
                navigateBack,
                direction: SwipeDirection.Left,
                minimumDistance: 50,
                minimumVelocity: 500,
                maximumDuration: TimeSpan.FromMilliseconds(300))
        ];
        var touchscreen = new VirtualTouchscreen();
        var clock = new ManualClock();
        using var runtime = new GestureRuntime(
            touchscreen,
            new InteractionContext(),
            clock,
            map);
        GestureRecognizedEventArgs? recognized = null;
        runtime.Recognized += (_, eventArgs) => recognized = eventArgs;

        touchscreen.Begin(1, new(100, 20));
        clock.Advance(Duration.FromTimeSpan(TimeSpan.FromMilliseconds(100)));
        touchscreen.End(1, new(20, 20));

        Assert.NotNull(recognized);
        Assert.Same(navigateBack, recognized.Intent);
        Assert.Equal(GestureKind.Swipe, recognized.Gesture);
        Assert.Equal(new Vector2(-80, 0), recognized.Delta);
        Assert.Equal(new Vector2(-800, 0), recognized.Velocity);
        Assert.Equal(Duration.FromTimeSpan(TimeSpan.FromMilliseconds(100)), recognized.Duration);
    }

    [Fact]
    public void SlowMovementDoesNotRecognizeSwipe()
    {
        var navigateBack = new Command("editor.navigateBack");
        GestureMap map = GestureMap("Editor")[
            new SwipeGesture(
                navigateBack,
                direction: SwipeDirection.Left,
                minimumDistance: 50,
                minimumVelocity: 500,
                maximumDuration: TimeSpan.FromMilliseconds(300))
        ];
        var touchscreen = new VirtualTouchscreen();
        var clock = new ManualClock();
        using var runtime = new GestureRuntime(
            touchscreen,
            new InteractionContext(),
            clock,
            map);
        var recognized = new List<GestureRecognizedEventArgs>();
        runtime.Recognized += (_, eventArgs) => recognized.Add(eventArgs);

        touchscreen.Begin(1, new(100, 20));
        clock.Advance(Duration.FromSeconds(1));
        touchscreen.End(1, new(20, 20));

        Assert.Empty(recognized);
    }

    [Fact]
    public void CustomRecognizerParticipatesWithoutChangingTheRuntime()
    {
        var inspect = new Command("editor.inspect");
        GestureMap map = GestureMap("Editor")[
            new TouchBeginGesture(inspect)
        ];
        var touchscreen = new VirtualTouchscreen();
        using var runtime = CreateRuntime(touchscreen, map);
        GestureRecognizedEventArgs? recognized = null;
        runtime.Recognized += (_, eventArgs) => recognized = eventArgs;

        touchscreen.Begin(1, new(10, 20));

        Assert.NotNull(recognized);
        Assert.Same(inspect, recognized.Intent);
        Assert.Equal(TouchBeginGesture.GestureType, recognized.Gesture);
    }

    private static GestureRuntime CreateRuntime(
        VirtualTouchscreen touchscreen,
        GestureMap map) =>
        new(touchscreen, new InteractionContext(), new ManualClock(), map);

    private sealed record TouchBeginGesture : GestureBinding
    {
        public static GestureKind GestureType { get; } = new("TouchBegin");

        public TouchBeginGesture(InteractionIntent intent)
            : base(intent, GestureType)
        {
        }

        public override GestureRecognizer CreateRecognizer() => new TouchBeginRecognizer();
    }

    private sealed class TouchBeginRecognizer : GestureRecognizer
    {
        public override GestureRecognition? Process(in GestureInput input) =>
            input.Touch.Phase == Lumyte.Input.TouchPhase.Began
                ? new(TouchBeginGesture.GestureType, Specificity: 1)
                : null;
    }
}
