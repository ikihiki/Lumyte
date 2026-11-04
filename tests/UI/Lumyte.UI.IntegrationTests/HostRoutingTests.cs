using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Input;
using Lumyte.Input.Abstractions;
using Lumyte.Platform.Tests;
using Lumyte.UI.SampleSupport;
using Lumyte.Windowing;
using Xunit;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.IntegrationTests;

/// <summary>Checks surface ownership and logical/physical coordinate boundaries.</summary>
public sealed class HostRoutingTests
{
    /// <summary>Checks native capture delivers outside coordinates and consumes the terminal.</summary>
    [Fact]
    public void OutsideReleaseCancelsClickAndReturningInsideClicksOnce()
    {
        using var host = new Host();
        Assert.True(host.Mouse(true, new(5, 5)));
        Assert.True(host.Mouse(false, new(120, 120)));
        Assert.Equal(0, host.Clicks);
        Assert.False(host.Button.IsPressed);
        host.Mouse(true, new(5, 5));
        host.Move(new(120, 120));
        host.Move(new(5, 5));
        host.Mouse(false, new(5, 5));
        Assert.Equal(1, host.Clicks);
    }

    /// <summary>Checks missing event-time coordinates never reuse a cached inside position.</summary>
    [Fact]
    public void PositionlessUpAndCaptureLossNeverClick()
    {
        using var host = new Host();
        host.Mouse(true, new(5, 5));
        Assert.True(host.Send(new MouseButtonData(MouseButton.Left, false)));
        Assert.Equal(0, host.Clicks);
        host.Mouse(true, new(5, 5));
        Assert.True(host.Send(new MouseButtonCanceledData(MouseButton.Left)));
        Assert.False(host.Button.IsPressed);
        Assert.Equal(0, host.Clicks);
    }

    /// <summary>Checks detach preserves physical-series consumption through Up.</summary>
    [Fact]
    public void DetachingAndUnknownOriginCannotLeakUpToTheGame()
    {
        using var host = new Host();
        host.Mouse(true, new(5, 5));
        host.Router.Detach(host.Surface);
        Assert.False(host.Button.IsPressed);
        Assert.True(host.Send(new MouseButtonData(MouseButton.Left, false), origin: new(Guid.NewGuid())));
        Assert.False(host.Mouse(true, new(5, 5)));
        Assert.Equal(0, host.Clicks);
    }

    /// <summary>Checks a physical series cannot switch native coordinate spaces.</summary>
    [Fact]
    public void CrossOriginMoveCancelsExistingOwner()
    {
        using var host = new Host();
        host.Mouse(true, new(5, 5));
        Assert.True(host.Send(new MouseMoveData(new(5, 5)), origin: new(Guid.NewGuid())));
        Assert.False(host.Button.IsPressed);
        Assert.True(host.Mouse(false, new(5, 5)));
        Assert.Equal(0, host.Clicks);
    }

    /// <summary>Checks UI key dispatch uses the acquisition root, and reset is window-local.</summary>
    [Fact]
    public void KeysUseTheAcquiredRootAndReleaseTheOriginalOwner()
    {
        using var host = new Host();
        host.Send(new KeyData(Key.Tab, true, false));
        host.Send(new KeyData(Key.Tab, false, false));
        Assert.Same(host.Button, host.Surface.FocusedElement);
        Assert.True(host.Send(new KeyData(Key.Space, true, false)));
        Assert.True(host.Button.IsPressed);
        using var other = new UiSurface(Rectangle());
        var window = (TestWindow)host.Platform.CreateWindow(new());
        window.Show();
        host.Platform.PumpEvents();
        InputWindowId b = new(Guid.NewGuid());
        host.Source.Windows.Add(window.Id, b);
        host.Router.Attach(window, other);
        Assert.True(host.Send(new KeyData(Key.Space, false, false), origin: b));
        Assert.Equal(1, host.Clicks);
        host.Router.Detach(other);
        window.Dispose();
    }

    /// <summary>Checks record replay is not dispatched twice and reverse traversal is rejected.</summary>
    [Fact]
    public void DuplicateAndReverseRecordSequencesAreHandledExplicitly()
    {
        using var host = new Host();
        var record = new InputRecord(new(1), 100, default, new MouseMoveData(new(5, 5))) { WindowId = host.Origin };
        host.Router.Dispatch(record);
        Assert.False(host.Router.Dispatch(record));
        Assert.Throws<InvalidOperationException>(() => host.Router.Dispatch(record with { Sequence = 99 }));
    }

    /// <summary>Checks anisotropic DPI is applied once in drawing and never applied to input.</summary>
    [Fact]
    public void DrawingUsesOnePerAxisScaleAndRestoresCanvasState()
    {
        using var host = new Host();
        host.Window.Change(host.Window.State with { Scale = new(2, 3), PixelSize = new(200, 300) });
        var canvas = new Canvas2D();
        canvas.Begin();
        UiHostDrawing.Record(canvas, host.Window, host.Surface);
        canvas.FillRect(new(1, 2, 3, 4), new Color2D(1, 0, 0, 1));
        DrawList2D list = canvas.End();
        Assert.Equal(new Vector2(100, 100), host.Surface.RootElement.Size);
        Assert.Equal(Matrix3x2.Identity, list.Commands[^1].State.Transform);
        Assert.Contains(list.Commands, command => command.State.Transform == Matrix3x2.CreateScale(2, 3));
        host.Mouse(true, new(5, 5));
        host.Mouse(false, new(5, 5));
        Assert.Equal(1, host.Clicks);
    }

    /// <summary>Checks hosted clipping prevents clicking invisible portions, and an unregistered front window blocks the back.</summary>
    [Fact]
    public void HostedAncestorClipAndFrontmostWindowControlInput()
    {
        using var host = new Host(attach: false);
        using var windows = new WindowSystem(host.Platform);
        WindowNode root = windows.Attach(host.Window, WindowRole.Normal, WindowOwnership.Borrowed);
        WindowNode parent = windows.CreateHostedWindow(new() { Parent = root, Bounds = new(10, 10, 20, 20) });
        WindowNode child = windows.CreateHostedWindow(new() { Parent = parent, Bounds = new(0, 0, 50, 50) });
        parent.Show();
        child.Show();
        windows.Update();
        host.Surface.SetViewport(child.ClientSize);
        host.Surface.UpdateLayout();
        host.Router.Attach(child, host.Surface);
        host.Mouse(true, new(15, 15));
        Assert.True(host.Button.IsPressed);
        host.Move(new(35, 15));
        Assert.False(host.Button.IsPressed);
        host.Mouse(false, new(35, 15));
        Assert.Equal(0, host.Clicks);
        WindowNode blocker = windows.CreateHostedWindow(new() { Parent = root, Bounds = new(10, 10, 20, 20) });
        blocker.Show();
        windows.Update();
        Assert.False(host.Mouse(true, new(15, 15)));
    }

    /// <summary>Checks an unknown-origin key terminal cancels activation instead of clicking.</summary>
    [Fact]
    public void UnknownKeyTerminalCancelsTheOwner()
    {
        using var host = new Host();
        host.Send(new KeyData(Key.Tab, true, false));
        host.Send(new KeyData(Key.Tab, false, false));
        host.Send(new KeyData(Key.Space, true, false));
        Assert.True(host.Button.IsPressed);
        Assert.True(host.Send(new KeyData(Key.Space, false, false), origin: new(Guid.NewGuid())));
        Assert.False(host.Button.IsPressed);
        Assert.Equal(0, host.Clicks);
    }

    /// <summary>Checks a mouse series already passed to the game cannot be acquired midway.</summary>
    [Fact]
    public void RightButtonStartingInTheGamePreventsMidSeriesUiAcquisition()
    {
        using var host = new Host();
        Assert.False(host.Send(new MouseButtonData(MouseButton.Right, true)));
        Assert.False(host.Mouse(true, new(5, 5)));
        Assert.False(host.Button.IsPressed);
        Assert.False(host.Mouse(false, new(5, 5)));
        Assert.False(host.Send(new MouseButtonData(MouseButton.Right, false)));
        Assert.Equal(0, host.Clicks);
    }

    private sealed class Host : IDisposable
    {
        private ulong _sequence;
        private ulong _capture;

        internal Host(bool attach = true)
        {
            Window = (TestWindow)Platform.CreateWindow(new() { ClientSize = new(100, 100) });
            Window.Show();
            Platform.PumpEvents();
            Source.Windows.Add(Window.Id, Origin);
            Router = new(Source, Source);
            Definitions.Button declaration = Compose.Button(onClick: () => Clicks++)[Rectangle(width: 100, height: 100, fill: new Color2D(1, 1, 1, 1))];
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
