using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Signals;
using Lumyte.UI.Application.Headless;
using Xunit;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Application.Tests;

/// <summary>Checks live retained drawings and transform-only paths without a GPU allocation.</summary>
public sealed class RetainedLayerTests
{
    /// <summary>Checks commands survive pose updates and refresh after a model reconstruction without a bitmap.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task MovementRetainsCommandsAndRefreshesContentWithoutAllocatingAnImageAsync()
    {
        var green = Signal.Create(false);
        int recordings = 0;
        await using UiApplication application = UiApplication.CreateBuilder().UseHeadless().UseRoot(_ => Panel()).Configure(new() { Motion = new() { MaximumSnapshotPixels = 1, MaximumSnapshotBytes = 8 } }).Build();
        using var surface = new UiSurface(() => new CountingDrawing(() => recordings++, green.Value));
        surface.SetViewport(new(20, 10));
        surface.UpdateLayout();
        var coordinator = new UiVisualCoordinator(application, surface, static () => null);
        using var layer = new UiVisualLayer(application, coordinator, () => surface.RootElement, false);
        layer.Advance();
        Draw(layer, new(10, 0));
        Draw(layer, new(20, 0));
        Assert.Equal(1, recordings);
        Assert.True(layer.Ready);
        Assert.Null(layer.Image);
        Assert.Empty(coordinator.Recorded);
        green.Value = true;
        surface.UpdateLayout();
        Draw(layer, new(30, 0));
        Assert.Equal(2, recordings);
        Assert.Null(layer.Image);
        Assert.False(layer.Failed);
    }

    /// <summary>Checks a new declaration identity with the same local revision cannot reuse the previous drawing.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task ReplacingTheIdentityRefreshesEvenWhenLocalRevisionAndSizeMatchAsync()
    {
        var replace = Signal.Create(false);
        int recordings = 0;
        await using UiApplication application = UiApplication.CreateBuilder().UseHeadless().UseRoot(_ => Panel()).Build();
        using var surface = new UiSurface(() => new CountingDrawing(() => recordings++, replace.Value) { Key = replace.Value ? "next" : "first" });
        surface.SetViewport(new(20, 10));
        surface.UpdateLayout();
        var coordinator = new UiVisualCoordinator(application, surface, static () => null);
        using var layer = new UiVisualLayer(application, coordinator, () => surface.RootElement, false);
        Draw(layer, Vector2.Zero);
        replace.Value = true;
        surface.UpdateLayout();
        Draw(layer, Vector2.Zero);
        Assert.Equal(2, recordings);
        Assert.Null(layer.Image);
    }

    /// <summary>Checks a custom component's live drawing does not require snapshot opt-in.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task UncachedCustomContentIsRecordedLiveWithoutCaptureOptInAsync()
    {
        int recordings = 0;
        await using UiApplication application = UiApplication.CreateBuilder().UseHeadless().UseRoot(_ => Panel()).Build();
        using var surface = new UiSurface(new LiveDrawing(() => recordings++));
        surface.SetViewport(new(20, 10));
        surface.UpdateLayout();
        var coordinator = new UiVisualCoordinator(application, surface, static () => null);
        using var layer = new UiVisualLayer(application, coordinator, () => surface.RootElement, false);
        Draw(layer, Vector2.Zero);
        Draw(layer, new(5, 0));
        Assert.Equal(2, recordings);
        Assert.Null(layer.Image);
    }

    /// <summary>Checks hiding content invalidates a fixed-size ancestor's retained commands.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task HiddenPresenceDoesNotRemainInThePageLayerAsync()
    {
        var visible = Signal.Create(true);
        await using UiApplication app = UiApplication.CreateBuilder().UseHeadless().UseRoot(_ => Panel()).Build();
        using var surface = new UiSurface(Panel(width: 20, height: 10)[Rectangle(fill: new Color2D(1, 0, 0, 1), isPresent: visible)]);
        surface.SetViewport(new(20, 10));
        surface.UpdateLayout();
        var coordinator = new UiVisualCoordinator(app, surface, static () => null);
        using var layer = new UiVisualLayer(app, coordinator, () => surface.RootElement, false);
        var before = new Canvas2D();
        before.Begin();
        layer.Draw(before, new(0, 0, 20, 10), new() { Offset = new(4, 0) });
        Assert.Single(before.End().Commands);
        ulong revision = surface.RootElement.VisualRevision;
        visible.Value = false;
        surface.UpdateLayout();
        Assert.True(surface.RootElement.VisualRevision > revision);
        Assert.Equal(new Vector2(20, 10), surface.RootElement.Size);
        var after = new Canvas2D();
        after.Begin();
        layer.Draw(after, new(0, 0, 20, 10), new() { Offset = new(6, 0) });
        Assert.Empty(after.End().Commands);
    }

    private static void Draw(UiVisualLayer layer, Vector2 offset)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        layer.Draw(canvas, new(0, 0, 20, 10), new() { Offset = offset });
        _ = canvas.End();
    }

    private sealed class CountingDrawing(Action record, bool green) : Definitions.Widget
    {
        internal Action Record { get; } = record;

        internal bool Green { get; } = green;

        protected override UiElement CreateElement() => new CountingElement(this);
    }

    private sealed class CountingElement(CountingDrawing widget) : UiElement<CountingDrawing>(widget)
    {
        protected override bool SupportsTransitionCapture => true;

        protected override void OnRecord(DrawingCanvas2D canvas)
        {
            Widget.Record();
            canvas.FillRect(new(0, 0, Size.X, Size.Y), Widget.Green ? new Color2D(0, 1, 0, 1) : new Color2D(1, 0, 0, 1));
        }
    }

    private sealed class LiveDrawing(Action record) : Definitions.Widget
    {
        internal Action Record { get; } = record;

        protected override UiElement CreateElement() => new LiveElement(this);
    }

    private sealed class LiveElement(LiveDrawing widget) : UiElement<LiveDrawing>(widget)
    {
        protected override void OnRecord(DrawingCanvas2D canvas)
        {
            Widget.Record();
            canvas.FillRect(new(0, 0, Size.X, Size.Y), new Color2D(1, 0, 0, 1));
        }
    }
}
