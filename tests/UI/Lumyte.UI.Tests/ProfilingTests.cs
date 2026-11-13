using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Signals;
using Xunit;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Tests;

/// <summary>Checks the standard UI profiling contract without an external collector.</summary>
[Collection("UI profiling")]
public sealed class ProfilingTests
{
    /// <summary>Checks typed metadata, layout skips, recording reuse and input counters.</summary>
    [Fact]
    public void RealOperationsEmitBranchCountersAndPairedStages()
    {
        using var listener = new Capture((EventKeywords)6);
        using var surface = new UiSurface(Panel()[Rectangle(fill: new Color2D(1, 0, 0, 1))]);
        surface.SetViewport(new(100, 80));
        using var drawing = new UiDrawingScene2D(surface);
        listener.Events.Clear();
        drawing.Update();
        Assert.Equal(2, Counter(listener, 601));
        Assert.Equal(0, Counter(listener, 602));
        Assert.Equal(2, Counter(listener, 201));
        Assert.Equal(2, Counter(listener, 301));
        listener.Events.Clear();
        drawing.Update();
        Assert.Equal(0, Counter(listener, 601));
        Assert.Equal(2, Counter(listener, 602));
        Assert.Equal(0, Counter(listener, 201));
        Assert.Contains(listener.Events, e => e.EventId == 2 && (int)e.Payload![4]! == 2 && (int)e.Payload[5]! == 3);
        listener.Events.Clear();
        Assert.False(surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(1, 1))));
        Assert.Equal(2, Counter(listener, 401));
        Assert.Equal(2, Counter(listener, 402));
        Assert.Equal(0, Counter(listener, 403));
        foreach (EventWrittenEventArgs begin in listener.Events.Where(e => e.EventId == 1))
        {
            Assert.Equal(EventLevel.Informational, begin.Level);
            Assert.Equal((EventKeywords)2, begin.Keywords & (EventKeywords)15);
            Assert.IsType<Guid>(begin.Payload![0]);
            Assert.IsType<long>(begin.Payload[1]);
            Assert.IsType<long>(begin.Payload[2]);
            Assert.Single(listener.Events, e => e.EventId == 2 && Equals(e.Payload![2], begin.Payload[2]));
        }

        Assert.DoesNotContain(listener.Events, e => e.EventId == 0);
    }

    /// <summary>Checks no-listener scopes return null without changing the current activity.</summary>
    [Fact]
    public void DisabledScopesReturnNullWithoutChangingActivities()
    {
        Activity? current = Activity.Current;
        Assert.Null(UiProfiling.Begin(1));
        Assert.Same(current, Activity.Current);
    }

    /// <summary>Checks nearest typed pairs, nesting, cancellation and keyword selection.</summary>
    [Fact]
    public void CorrelationAndKeywordsRemainExplicit()
    {
        using var listener = new Capture((EventKeywords)2);
        using Activity outer = new Activity("frame").Start();
        var run = Guid.NewGuid();
        outer.SetCustomProperty("Lumyte.Profiling.RunId", run);
        outer.SetCustomProperty("Lumyte.Profiling.FrameId", 42L);
        using Activity inner = new Activity("user").Start();
        inner.SetCustomProperty("Lumyte.Profiling.RunId", Guid.NewGuid());
        inner.SetCustomProperty("Lumyte.Profiling.FrameId", 99);
        using (UiProfiling.Scope? first = UiProfiling.Begin(1))
        {
            using UiProfiling.Scope? second = UiProfiling.Begin(1);
            second?.Fail(new OperationCanceledException());
        }

        EventWrittenEventArgs[] begins = listener.Events.Where(e => e.EventId == 1).ToArray();
        Assert.Equal(2, begins.Length);
        Assert.Equal(run, begins[0].Payload![0]);
        Assert.Equal(42L, begins[0].Payload![1]);
        Assert.Equal(begins[0].Payload![2], begins[1].Payload![3]);
        Assert.Contains(listener.Events, e => e.EventId == 2 && (int)e.Payload![5]! == 1);
        Assert.DoesNotContain(listener.Events, e => e.EventId == 3);
        listener.EnableEvents(UiProfiling.Log, EventLevel.Informational, (EventKeywords)4);
        listener.Events.Clear();
        using (UiProfiling.Scope? scope = UiProfiling.Begin(1))
        {
            UiProfiling.Add(1, 101, 7);
            using (UiProfiling.Scope? nested = UiProfiling.Begin(1))
            {
                UiProfiling.Add(1, 101, 3);
            }
        }

        Assert.All(listener.Events, e => Assert.Equal(3, e.EventId));
        Assert.Equal(new long[] { 3, 7 }, listener.Events.Where(e => (int)e.Payload![5]! == 101).Select(e => (long)e.Payload![6]!).ToArray());
    }

    /// <summary>Checks missing context and validation failure retain balanced events.</summary>
    [Fact]
    public void MissingContextAndFailureAreNotHidden()
    {
        using var listener = new Capture((EventKeywords)6);
        Activity? previous = Activity.Current;
        Activity.Current = null;
        try
        {
            using var surface = new UiSurface(Panel());
            surface.SetViewport(new(10, 10));
            using var drawing = new UiDrawingScene2D(surface);
            using DrawingSceneReadLease2D lease = drawing.Borrow();
            listener.Events.Clear();
            Assert.Throws<InvalidOperationException>(drawing.Update);
            EventWrittenEventArgs end = Assert.Single(listener.Events, e => e.EventId == 2);
            Assert.Equal(Guid.Empty, end.Payload![0]);
            Assert.Equal(0L, end.Payload[1]);
            Assert.Equal(0L, end.Payload[3]);
            Assert.Equal(6, end.Payload[4]);
            Assert.Equal(2, end.Payload[5]);
            Assert.Equal(0, Counter(listener, 601));
        }
        finally
        {
            Activity.Current = previous;
        }
    }

    /// <summary>Checks effective-constraint reuse counts subtree roots, not skipped descendants.</summary>
    [Fact]
    public void LayoutCacheAndHiddenCountersFollowActualBranches()
    {
        using var listener = new Capture((EventKeywords)6);
        using var surface = new UiSurface(Panel()[Panel(width: 20, height: 15)[Rectangle()], Rectangle()]);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        listener.Events.Clear();
        surface.SetViewport(new(200, 200));
        surface.UpdateLayout();
        Assert.Equal(2, Counter(listener, 201));
        Assert.Equal(1, Counter(listener, 202));
        Assert.Equal(0, Counter(listener, 203));
        surface.Rebuild(Panel()[Panel(isVisible: false)[Rectangle()], Rectangle()]);
        listener.Events.Clear();
        surface.UpdateLayout();
        Assert.Equal(2, Counter(listener, 203));
        Assert.True(Counter(listener, 101) > 0);
        Assert.Equal(0, Counter(listener, 102));
    }

    /// <summary>Checks traversal, opaque recordings and animation skips use distinct spans.</summary>
    [Fact]
    public void DrawingAndAnimationBoundariesRemainDistinct()
    {
        using var listener = new Capture((EventKeywords)6);
        using var surface = new UiSurface(Panel()[Rectangle()]);
        surface.SetViewport(new(10, 10));
        surface.SetPageTransitioning(true);
        using var drawing = new UiDrawingScene2D(surface);
        listener.Events.Clear();
        drawing.Update();
        Assert.Equal(1, Counter(listener, 601));
        Assert.Equal(0, Counter(listener, 602));
        Assert.True(Counter(listener, 501) > 0);
        EventWrittenEventArgs retained = Assert.Single(listener.Events, e => e.EventId == 1 && (int)e.Payload![4]! == 6);
        EventWrittenEventArgs traversal = Assert.Single(listener.Events, e => e.EventId == 1 && (int)e.Payload![4]! == 5);
        Assert.Equal(retained.Payload![2], traversal.Payload![3]);
        listener.Events.Clear();
        surface.SetAnimationUpdatesEnabled(false);
        surface.AdvanceAnimations();
        Assert.Equal(0, Counter(listener, 701));
        Assert.Contains(listener.Events, e => e.EventId == 2 && (int)e.Payload![4]! == 7 && (int)e.Payload[5]! == 3);
        surface.SetViewport(new(0, 0));
        var canvas = new Canvas2D();
        canvas.Begin();
        listener.Events.Clear();
        surface.Record(canvas);
        _ = canvas.End();
        Assert.Equal(0, Counter(listener, 501));
        Assert.Contains(listener.Events, e => e.EventId == 2 && (int)e.Payload![4]! == 5 && (int)e.Payload[5]! == 3);
    }

    /// <summary>Checks compute failures count attempts and refresh failures cannot change layout success.</summary>
    [Fact]
    public void LayoutAndRefreshFailuresHaveIndependentOutcomes()
    {
        using var listener = new Capture((EventKeywords)6);
        var widget = new FailureWidget { LayoutFailure = new OperationCanceledException() };
        using var surface = new UiSurface(widget);
        surface.SetViewport(new(10, 10));
        listener.Events.Clear();
        Assert.Throws<OperationCanceledException>(surface.UpdateLayout);
        Assert.Equal(1, Counter(listener, 201));
        Assert.Contains(listener.Events, e => e.EventId == 2 && (int)e.Payload![4]! == 2 && (int)e.Payload[5]! == 1);
        widget.LayoutFailure = null;
        widget.RefreshFailure = true;
        listener.Events.Clear();
        Assert.Throws<InvalidOperationException>(surface.UpdateLayout);
        Assert.Contains(listener.Events, e => e.EventId == 2 && (int)e.Payload![4]! == 2 && (int)e.Payload[5]! == 0);
        Assert.Contains(listener.Events, e => e.EventId == 2 && (int)e.Payload![4]! == 3 && (int)e.Payload[5]! == 2);
        widget.RefreshFailure = false;
        surface.UpdateLayout();
        widget.InputFailure = true;
        listener.Events.Clear();
        Assert.Throws<InvalidOperationException>(() => surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(1, 1))));
        Assert.Equal(1, Counter(listener, 402));
        Assert.DoesNotContain(listener.Events, e => e.EventId == 3 && (int)e.Payload![5]! == 403);
        Assert.Contains(listener.Events, e => e.EventId == 2 && (int)e.Payload![4]! == 4 && (int)e.Payload[5]! == 2);
    }

    /// <summary>Checks signal counters count queued jobs and sequential threads inherit no synchronous parent.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task SignalsAndSequentialThreadsDoNotRetainParentsAsync()
    {
        using var listener = new Capture((EventKeywords)6);
        var width = Signal.Create<float?>(20);
        using var surface = new UiSurface(Rectangle(width: width));
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        width.Value = 30;
        listener.Events.Clear();
        surface.UpdateTree();
        Assert.True(Counter(listener, 801) > 0);
        listener.Events.Clear();
        await Task.Run(surface.UpdateTree);
        EventWrittenEventArgs begin = Assert.Single(listener.Events, e => e.EventId == 1 && (int)e.Payload![4]! == 1);
        Assert.Equal(0L, begin.Payload![3]);
    }

    private static long Counter(Capture listener, int id)
    {
        EventWrittenEventArgs[] counters = listener.Events.Where(e => e.EventId == 3 && (int)e.Payload![5]! == id).ToArray();
        Assert.NotEmpty(counters);
        return counters.Sum(e => (long)e.Payload![6]!);
    }

    private sealed class FailureWidget : Definitions.Widget
    {
        internal Exception? LayoutFailure { get; set; }

        internal bool RefreshFailure { get; set; }

        internal bool InputFailure { get; set; }

        protected override UiElement CreateElement() => new FailureElement(this);
    }

    private sealed class FailureElement(FailureWidget widget) : UiElement<FailureWidget>(widget)
    {
        protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
        {
            if (Widget.LayoutFailure is { } failure)
            {
                throw failure;
            }

            return constraints.Constrain(Vector2.Zero);
        }

        protected override IEnumerable<UiElement> GetInputChildren() =>
            Widget.RefreshFailure ? throw new InvalidOperationException("refresh") : Array.Empty<UiElement>();

        protected override UiInputResult OnInput(in UiInputContext context) =>
            Widget.InputFailure ? throw new InvalidOperationException("input") : UiInputResult.Unhandled;
    }

    private sealed class Capture : EventListener
    {
        internal Capture(EventKeywords keywords)
        {
            EnableEvents(UiProfiling.Log, EventLevel.Informational, keywords);
        }

        internal ConcurrentQueue<EventWrittenEventArgs> Events { get; } = new();

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (eventData.EventSource.Name == "Lumyte-UI-Profiling")
            {
                Events.Enqueue(eventData);
            }
        }
    }
}
