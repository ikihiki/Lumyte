using System.Numerics;
using Lumyte.Input.Abstractions;
using Lumyte.Mathematics.Colors;
using Lumyte.Platform.Tests;
using Lumyte.UI.IntegrationTests;
using Lumyte.Windowing;
using Xunit;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Application.Tests;

/// <summary>Exercises the production application's ordered wheel routing, not the legacy sample router.</summary>
public sealed class WheelHostRoutingTests
{
    /// <summary>Captured drags preserve real outside coordinates and re-entry rather than using an inside-test sentinel.</summary>
    [Fact]
    public void CapturedMovesKeepOutsideAndReentryCoordinates()
    {
        var events = new List<UiPointerInput>();
        using var host = new Host(pointerEvents: events);
        Assert.True(host.Mouse(true, new(50, 40)));
        events.Clear();
        Assert.True(host.Move(new(130, 140)));
        Assert.True(host.Move(new(160, 150)));
        Assert.True(host.Move(new(70, 60)));
        Assert.Equal(new Vector2[] { new(130, 140), new(160, 150), new(70, 60) }, events.Select(input => input.Position));
        Assert.True(host.Mouse(false, new(70, 60)));
    }

    /// <summary>Wheel requires paired acquisition coordinates, not cached positions or DPI-scaled points.</summary>
    [Fact]
    public void WheelRequiresEventPositionAndMapsHostedCoordinatesOnce()
    {
        var events = new List<UiWheelInput>();
        using var host = new Host(attach: false, wheelEvents: events);
        host.Window.Change(host.Window.State with { Scale = new(2, 3), PixelSize = new(200, 300) });
        using var windows = new WindowSystem(host.Platform);
        WindowNode root = windows.Attach(host.Window, WindowRole.Normal, WindowOwnership.Borrowed);
        WindowNode child = windows.CreateHostedWindow(new() { Parent = root, Bounds = new(10, 20, 30, 40) });
        child.Show();
        windows.Update();
        host.Surface.SetViewport(child.ClientSize);
        host.Surface.UpdateLayout();
        host.Router.Attach(child, host.Surface);
        Assert.True(host.Surface.Dispatch(new UiWheelInput(new(5, 6), new(0, -20))), $"Local surface root size {host.Surface.RootElement.Size}; child viewport {child.ClientSize}");
        events.Clear();
        Assert.True(host.Wheel(new(15, 26), new(0, 0.5f)), $"Root {root.ClientSize}; child {child.ClientSize}; window {host.Window.State}");
        Assert.Equal(new Vector2(5, 6), Assert.Single(events).Position);
        Assert.Equal(new Vector2(0, -20), events[0].Delta);
        Assert.False(host.Send(new MouseWheelData(new(0, 1))));
        Assert.False(host.Wheel(new(5, 5), new(0, 1)));
        host.Send(new FocusData(false));
        Assert.False(host.Send(new MouseWheelData(new(0, 1))));
        Assert.Single(events);
    }

    /// <summary>Both initial and captured touch input preserve device metadata and outside coordinates.</summary>
    [Fact]
    public void CapturedTouchPreservesDeviceAndOutsideCoordinates()
    {
        var events = new List<UiPointerInput>();
        using var host = new Host(pointerEvents: events);
        Assert.True(host.Send(new TouchData(new(17), TouchPhase.Began, new(50, 40), null)));
        Assert.True(host.Send(new TouchData(new(17), TouchPhase.Moved, new(130, 140), null)));
        Assert.Equal(new Vector2(130, 140), events[^1].Position);
        Assert.All(events, input => Assert.Equal(UiPointerDevice.Touch, input.Device));
        Assert.True(host.Send(new TouchData(new(17), TouchPhase.Ended, new(70, 60), null)));
        Assert.Equal(UiPointerDevice.Touch, events[^1].Device);
    }

    private sealed class WheelWidget(List<UiWheelInput> events, List<UiPointerInput>? pointerEvents) : Definitions.Panel
    {
        internal List<UiWheelInput> Events { get; } = events;

        internal List<UiPointerInput>? PointerEvents { get; } = pointerEvents;

        protected override UiElement CreateElement() => new WheelElement(this);
    }

    private sealed class WheelElement(WheelWidget widget) : PanelElement(widget)
    {
        protected override UiInputResult OnInput(in UiInputContext context)
        {
            if (context.Input is UiPointerInput pointer && widget.PointerEvents is { } pointers)
            {
                pointers.Add(pointer);
                if (pointer.Phase == UiPointerPhase.Down)
                {
                    context.CapturePointer();
                }

                return UiInputResult.Handled;
            }

            if (context.Input is not UiWheelInput wheel)
            {
                return UiInputResult.Unhandled;
            }

            widget.Events.Add(wheel);
            return UiInputResult.Handled;
        }
    }

    private sealed class Host : IDisposable
    {
        private ulong _sequence;
        private ulong _capture;

        internal Host(bool attach = true, List<UiWheelInput>? wheelEvents = null, List<UiPointerInput>? pointerEvents = null)
        {
            Window = (TestWindow)Platform.CreateWindow(new() { ClientSize = new(100, 100) });
            Window.Show();
            Platform.PumpEvents();
            Source.Windows.Add(Window.Id, Origin);
            Router = new(Source, Source);
            Definitions.Button declaration = Lumyte.UI.Compose.Button(onClick: () => Clicks++)[Rectangle(width: 100, height: 100, fill: new SRgba<float>(1, 1, 1, 1))];
            if (wheelEvents is not null || pointerEvents is not null)
            {
                declaration = Lumyte.UI.Compose.Button(onClick: () => Clicks++)[new WheelWidget(wheelEvents ?? [], pointerEvents) { Width = 100, Height = 100 }];
            }

            Surface = new(declaration);
            Button = (ButtonElement)Surface.RootElement;
            Surface.SetViewport(new(100, 100));
            Surface.UpdateLayout();
            if (attach)
            {
                Router.Attach(Window, Surface);
            }
        }

        internal TestPlatform Platform { get; } = new();

        internal TestWindow Window { get; }

        internal OrderedFixture Source { get; } = new();

        internal UiHostInputRouter Router { get; }

        internal InputWindowId Origin { get; } = new(Guid.NewGuid());

        internal ButtonElement Button { get; }

        internal UiSurface Surface { get; }

        internal int Clicks { get; private set; }

        public void Dispose()
        {
            Router.Detach(Surface);
            Surface.Dispose();
            Window.Dispose();
            Platform.Dispose();
        }

        internal bool Mouse(bool down, Vector2 point)
        {
            ulong capture = ++_capture;
            Send(new MouseMoveData(point), capture: capture);
            return Send(new MouseButtonData(MouseButton.Left, down), capture: capture);
        }

        internal bool Wheel(Vector2 point, Vector2 delta)
        {
            ulong capture = ++_capture;
            Send(new MouseMoveData(point), capture: capture);
            return Send(new MouseWheelData(delta), capture: capture);
        }

        internal bool Move(Vector2 point) => Send(new MouseMoveData(point));

        internal bool Send(InputData data, ulong? capture = null, InputWindowId? origin = null)
        {
            ulong cause = capture ?? ++_capture;
            return Router.Dispatch(new(new(1), ++_sequence, default, data)
            {
                WindowId = origin ?? Origin,
                StateWindowId = Origin,
                Capture = new(Source.StreamId, cause),
            });
        }
    }
}
