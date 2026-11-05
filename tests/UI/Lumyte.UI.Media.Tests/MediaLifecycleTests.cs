using System.Numerics;
using Lumyte.Core.Time;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Signals;
using Xunit;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Media.Compose;

namespace Lumyte.UI.Media.Tests;

/// <summary>Verifies public media factories, semantics and explicit owner-time lifetime.</summary>
public sealed class MediaLifecycleTests
{
    /// <summary>Checks normalized value range rejection before publication.</summary>
    /// <param name="value">The invalid value.</param>
    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ProgressRejectsInvalidValues(double value) => Assert.Throws<ArgumentOutOfRangeException>(() => new UiSurface(ProgressBar(value: value)));

    /// <summary>Checks Signal changes preserve identity and do not create interaction capabilities.</summary>
    [Fact]
    public void ProgressSignalUpdatesSemanticValueWithoutLayoutOrActivation()
    {
        var value = Signal.Create(0.25);
        using var surface = new UiSurface(Panel()[ProgressBar(value: value, testId: "progress", focusable: true, with: [Padding.All(3)])]);
        surface.SetViewport(new(200, 100));
        surface.UpdateLayout();
        UiElement element = surface.RootElement.Children[0];
        UiAutomationNode previous = surface.CaptureAutomationSnapshot().Nodes.Single(node => node.TestId == "progress");
        Assert.Equal(0.25, previous.Value);
        Assert.Equal(UiAutomationActions.None, previous.Actions);
        Assert.False(previous.IsFocusable);
        Assert.Equal(new Vector2(126, 10), element.Size);
        value.Value = 1;
        surface.UpdateTree();
        Assert.Same(element, surface.RootElement.Children[0]);
        Assert.Equal(new Vector2(126, 10), element.Size);
        UiAutomationNode next = surface.CaptureAutomationSnapshot().Nodes.Single(node => node.TestId == "progress");
        Assert.Equal(1, next.Value);
        Assert.Equal(0.25, previous.Value);
        Assert.Equal(previous.Id, next.Id);
    }

    /// <summary>Checks shared declarations create independent clocks and unmount unregisters work.</summary>
    [Fact]
    public void BusyUsesOnlyExplicitFramesFreezesHiddenAndUnmounts()
    {
        var clock = new ManualClock();
        var visible = Signal.Create(true);
        Compose.Definitions.BusyIndicator declaration = BusyIndicator(isVisible: visible, testId: "busy");
        using var first = new UiSurface(declaration, new Dispatcher(), clock);
        using var second = new UiSurface(declaration, new Dispatcher(), clock);
        first.SetViewport(new(40));
        second.SetViewport(new(40));
        BusyIndicatorElement firstElement = Assert.IsType<BusyIndicatorElement>(first.RootElement);
        BusyIndicatorElement secondElement = Assert.IsType<BusyIndicatorElement>(second.RootElement);
        Assert.True(first.HasRunningAnimations);
        first.AdvanceAnimations();
        second.AdvanceAnimations();
        clock.Advance(Duration.FromSeconds(0.25));
        first.UpdateLayout();
        Assert.Equal(0, firstElement.Phase);
        first.AdvanceAnimations();
        Assert.Equal(0.25, firstElement.Phase);
        Assert.Equal(0, secondElement.Phase);
        visible.Value = false;
        first.UpdateTree();
        Assert.False(first.HasRunningAnimations);
        first.AdvanceAnimations();
        clock.Advance(Duration.FromSeconds(8));
        visible.Value = true;
        first.UpdateTree();
        first.AdvanceAnimations();
        Assert.Equal(0.25, firstElement.Phase);
        clock.Advance(Duration.FromSeconds(0.125));
        first.AdvanceAnimations();
        Assert.Equal(0.375, firstElement.Phase);
        first.SetAnimationUpdatesEnabled(false);
        clock.Advance(Duration.FromSeconds(4));
        Assert.False(first.HasRunningAnimations);
        first.SetAnimationUpdatesEnabled(true);
        first.AdvanceAnimations();
        Assert.Equal(0.375, firstElement.Phase);
        Assert.True(first.CaptureAutomationSnapshot().Nodes[0].IsIndeterminate);
        first.Rebuild(Panel());
        first.UpdateTree();
        Assert.False(firstElement.IsMounted);
        Assert.False(first.HasRunningAnimations);
    }

    /// <summary>Checks a static retained frame is reused, while an explicit busy frame invalidates local drawing.</summary>
    [Fact]
    public void BusyRetainedDrawingInvalidatesWithoutRelayout()
    {
        var clock = new ManualClock();
        using var surface = new UiSurface(BusyIndicator(with: [Padding.All(2)]), new Dispatcher(), clock);
        surface.SetViewport(new(32));
        using UiDrawingScene2D scene = surface.CreateDrawingScene();
        scene.Update();
        scene.Update();
        Assert.Equal(0, scene.LastRecordingCount);
        surface.AdvanceAnimations();
        clock.Advance(Duration.FromSeconds(0.1));
        surface.AdvanceAnimations();
        Assert.Equal(new Vector2(28), surface.RootElement.ContentSize);
        scene.Update();
        Assert.True(scene.LastRecordingCount > 0);
    }

    /// <summary>Checks absent images have deterministic placeholder dimensions and no drawing.</summary>
    [Fact]
    public void UnavailableImageKeepsExplicitPlaceholderAndValidatesSettings()
    {
        using var surface = new UiSurface(Panel()[Image(placeholderSize: new Vector2(30, 12))]);
        surface.SetViewport(new(100));
        surface.UpdateLayout();
        Assert.Equal(new Vector2(30, 12), surface.RootElement.Children[0].Size);
        var canvas = new Canvas2D();
        canvas.Begin();
        surface.Record(canvas);
        Assert.Equal(0, canvas.End().Count);
        using var fixedPlaceholder = new UiSurface(Panel()[Image(placeholderSize: new Vector2(30, 12), width: 60)]);
        fixedPlaceholder.SetViewport(new(100));
        fixedPlaceholder.UpdateLayout();
        Assert.Equal(new Vector2(60, 12), fixedPlaceholder.RootElement.Children[0].Size);
        Assert.Throws<ArgumentOutOfRangeException>(() => new UiSurface(Image(placeholderSize: new Vector2(-1, 1))));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UiSurface(Image(stretch: (ImageStretch)99)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UiSurface(Icon(size: float.NaN)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UiSurface(Icon(size: 0)));
    }

    /// <summary>Checks invalid replacement preparation keeps the old mounted progress and snapshot.</summary>
    [Fact]
    public void PrepareFailureKeepsPublishedValueAndIdentity()
    {
        using var surface = new UiSurface(ProgressBar(value: 0.4));
        surface.SetViewport(new(120, 4));
        UiElement element = surface.RootElement;
        UiAutomationNode previous = Assert.Single(surface.CaptureAutomationSnapshot().Nodes);
        surface.Rebuild(ProgressBar(value: double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(surface.UpdateTree);
        Assert.Same(element, surface.RootElement);
        Assert.Equal(0.4, ((Compose.Definitions.ProgressBar)surface.Root).Value);
        Assert.Equal(0.4, previous.Value);
        surface.Rebuild(ProgressBar(value: 0.8));
        surface.UpdateTree();
        Assert.Equal(0.8, Assert.Single(surface.CaptureAutomationSnapshot().Nodes).Value);
    }

    /// <summary>Checks a hidden interval with no animation phase is not added on resumption.</summary>
    [Fact]
    public void HiddenWithoutFrameAndClockGettersDoNotCatchUp()
    {
        var clock = new CountingClock();
        var visible = Signal.Create(true);
        using var surface = new UiSurface(Panel(isVisible: visible)[BusyIndicator()], new Dispatcher(), clock);
        surface.SetViewport(new(32));
        BusyIndicatorElement busy = Assert.IsType<BusyIndicatorElement>(surface.RootElement.Children[0]);
        surface.AdvanceAnimations();
        clock.Now = TimePoint.FromTicks(TimeSpan.TicksPerSecond / 4);
        surface.AdvanceAnimations();
        int reads = clock.Reads;
        _ = surface.HasRunningAnimations;
        _ = surface.HasPendingAnimationUpdates;
        _ = busy.Phase;
        surface.UpdateLayout();
        Assert.Equal(reads, clock.Reads);
        visible.Value = false;
        surface.UpdateTree();
        surface.AdvanceAnimations();
        Assert.Equal(reads, clock.Reads);
        clock.Now = TimePoint.FromTicks(TimeSpan.TicksPerSecond * 100);
        visible.Value = true;
        surface.UpdateTree();
        surface.AdvanceAnimations();
        Assert.Equal(0.25, busy.Phase);
    }

    private sealed class CountingClock : IMonotonicClock
    {
        private TimePoint _now;

        public TimePoint Now
        {
            get
            {
                Reads++;
                return _now;
            }

            set => _now = value;
        }

        internal int Reads { get; private set; }
    }

    private sealed class Dispatcher : IUiDispatcher
    {
        public bool CheckAccess() => true;

        public ValueTask InvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            action();
            return ValueTask.CompletedTask;
        }

        public ValueTask<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default) => ValueTask.FromResult(action());
    }
}
