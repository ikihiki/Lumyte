using Lumyte.Core.Time;
using Xunit;

namespace Lumyte.Animation.Tests;

/// <summary>Checks event clocks and traversal at fractional and maximum tick positions.</summary>
public sealed class PlaybackBoundaryRegressionTests
{
    /// <summary>Repeated updates preserve the original fractional playback anchor.</summary>
    [Fact]
    public void FractionalSpeedRetainsEventTimesAcrossZeroAndAdvancingUpdates()
    {
        var builder = new AnimationTimelineBuilder();
        builder.AddEvent(Ticks(0), "Start");
        builder.AddEvent(Ticks(1), "First");
        builder.AddEvent(Ticks(2), "Second");
        builder.AddEvent(Ticks(3), "Third");
        var clock = new ManualClock();
        var playback = new AnimationPlayback(clock, builder.Build()) { Speed = 0.5 };
        var output = new AnimationOutput();
        var events = new List<AnimationEventOccurrence>();
        var occurred = new List<long>();
        playback.Play();
        for (long time = 1; time <= 6; time++)
        {
            clock.Advance(Ticks(1));
            playback.Update(output, events);
            occurred.AddRange(events.Select(item => time - 1 + item.UpdateOffset.Ticks));
            events.Clear();
            playback.Update(output, events);
            Assert.Empty(events);
        }

        Assert.Equal(new long[] { 0, 2, 4, 6 }, occurred);
    }

    /// <summary>Pausing preserves pending fractional crossings and resuming establishes a new anchor.</summary>
    [Fact]
    public void FractionalSpeedPausePreservesPendingEventClock()
    {
        var builder = new AnimationTimelineBuilder();
        builder.AddEvent(Ticks(1), "BeforePause");
        builder.AddEvent(Ticks(2), "AfterResume");
        var clock = new ManualClock();
        var playback = new AnimationPlayback(clock, builder.Build()) { Speed = 0.5 };
        var output = new AnimationOutput();
        var events = new List<AnimationEventOccurrence>();
        playback.Play();
        clock.Advance(Ticks(1));
        playback.Update(output, events);
        clock.Advance(Ticks(1));
        playback.Pause();
        clock.Advance(Ticks(100));
        playback.Update(output, events);
        Assert.Empty(events);
        playback.Resume();
        playback.Update(output, events);
        Assert.Equal(Ticks(1), Assert.Single(events).UpdateOffset);
        events.Clear();
        clock.Advance(Ticks(1));
        playback.Update(output, events);
        Assert.Empty(events);
        clock.Advance(Ticks(1));
        playback.Update(output, events);
        Assert.Equal(Ticks(1), Assert.Single(events).UpdateOffset);
    }

    /// <summary>Speed changes and zero speed retain the clock segment that produced each crossing.</summary>
    [Fact]
    public void FractionalSpeedChangesKeepEventOffsetsWithinTheirClockSegments()
    {
        var builder = new AnimationTimelineBuilder();
        builder.AddEvent(Ticks(1), "First");
        builder.AddEvent(Ticks(3), "Faster");
        builder.AddEvent(Ticks(4), "Restarted");
        var clock = new ManualClock();
        var playback = new AnimationPlayback(clock, builder.Build()) { Speed = 0.5 };
        var output = new AnimationOutput();
        var events = new List<AnimationEventOccurrence>();
        playback.Play();
        clock.Advance(Ticks(1));
        playback.Update(output, events);
        clock.Advance(Ticks(2));
        playback.Speed = 2;
        clock.Advance(Ticks(1));
        playback.Speed = 0;
        clock.Advance(Ticks(5));
        playback.Update(output, events);
        Assert.Equal(new long[] { 1, 3 }, events.Select(item => item.UpdateOffset.Ticks));
        events.Clear();
        playback.Speed = 0.5;
        clock.Advance(Ticks(1));
        playback.Update(output, events);
        Assert.Empty(events);
        clock.Advance(Ticks(1));
        playback.Update(output, events);
        Assert.Equal(Ticks(1), Assert.Single(events).UpdateOffset);
    }

    /// <summary>Animation state events use the same preserved absolute clock as playback events.</summary>
    [Fact]
    public void FractionalStateMarkerReportsItsActualOccurrenceTime()
    {
        var clock = new ManualClock();
        clock.Advance(Ticks(10));
        var timeline = new AnimationTimelineBuilder();
        timeline.AddEvent(Ticks(1), "Crossed");
        var builder = new AnimationStateMachineBuilder<string, bool>();
        builder.AddState("Active", timeline.Build());
        AnimationStateMachine<string, bool> machine = builder.Build(clock, "Active", false);
        machine.Speed = 0.5;
        var events = new List<AnimationStateEvent<string>>();
        clock.Advance(Ticks(1));
        machine.Update(false, new AnimationOutput(), events, []);
        Assert.Empty(events);
        clock.Advance(Ticks(1));
        machine.Update(false, new AnimationOutput(), events, []);
        Assert.Equal(TimePoint.FromTicks(12), Assert.Single(events).OccurredAt);
    }

    /// <summary>Unit speed maps the maximum representable endpoint without floating point rounding.</summary>
    [Fact]
    public void MaximumEndpointMarkerRetainsItsExactClockOffset()
    {
        var builder = new AnimationTimelineBuilder();
        builder.AddEvent(Ticks(long.MaxValue), "End");
        var clock = new ManualClock();
        var playback = new AnimationPlayback(clock, builder.Build());
        var events = new List<AnimationEventOccurrence>();
        playback.Play();
        clock.Advance(Ticks(long.MaxValue));
        Assert.True(playback.Update(new AnimationOutput(), events));
        Assert.Equal(Ticks(long.MaxValue), Assert.Single(events).UpdateOffset);
    }

    /// <summary>The final outer loop completes before its maximum loop index can wrap around.</summary>
    [Fact]
    public void MaximumLoopIndexFinishesReleaseTraversal()
    {
        var channel = AnimationChannel<float>.Create();
        var builder = new AnimationTimelineBuilder();
        var source = new Tween<float>(0, 1, Ticks(1), AnimationInterpolators.Float, AnimationEasing.Linear);
        builder.Add(Duration.Zero, source, channel, AnimationFillMode.Release);
        var clock = new ManualClock();
        var playback = new AnimationPlayback(clock, builder.Build(), AnimationWrapMode.Loop);
        var output = new AnimationOutput();
        playback.Play();
        clock.Advance(Ticks(long.MaxValue));
        Assert.False(playback.Update(output, []));
        Assert.Equal(Duration.Zero, playback.Position);
        Assert.True(output.TryGet(channel, out float value));
        Assert.Equal(0, value);
    }

    /// <summary>Virtual reverse origins may exceed long while emitted event positions remain representable.</summary>
    /// <param name="reverseCount">The number of nested reverse transforms.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ReversedLoopAllowsWideIntermediateOrigins(int reverseCount)
    {
        long length = (long.MaxValue / 2) + 1;
        var builder = new AnimationTimelineBuilder();
        builder.AddEvent(Ticks(reverseCount % 2 == 0 ? 1 : length - 1), "NearStart");
        builder.SetDuration(Ticks(length));
        AnimationTimeline timeline = builder.Build();
        for (int index = 0; index < reverseCount; index++)
        {
            timeline = timeline.Reverse();
        }

        var clock = new ManualClock();
        var playback = new AnimationPlayback(clock, timeline, AnimationWrapMode.Loop);
        var events = new List<AnimationEventOccurrence>();
        playback.Play();
        clock.Advance(Ticks(length + 1));
        Assert.False(playback.Update(new AnimationOutput(), events));
        Assert.Equal(new long[] { 0, 1 }, events.Select(item => item.LoopIndex));
        Assert.Equal(new long[] { 1, 1 }, events.Select(item => item.Event.Time.Ticks));
        Assert.Equal(new long[] { 1, length + 1 }, events.Select(item => item.UpdateOffset.Ticks));
    }

    /// <summary>A large reverse transform cannot invent a release crossing before the next track starts.</summary>
    [Fact]
    public void ReversedLoopClampsReleaseTraversalBeforeReflectingTime()
    {
        long length = (long.MaxValue / 2) + 1;
        var channel = AnimationChannel<float>.Create();
        var builder = new AnimationTimelineBuilder();
        var source = new Tween<float>(0, 1, Ticks(1), AnimationInterpolators.Float, AnimationEasing.Linear);
        builder.Add(Duration.Zero, source, channel, AnimationFillMode.Release);
        builder.SetDuration(Ticks(length));
        var clock = new ManualClock();
        var playback = new AnimationPlayback(clock, builder.Build().Reverse(), AnimationWrapMode.Loop);
        var output = new AnimationOutput();
        playback.Play();
        clock.Advance(Ticks(length + 1));
        Assert.False(playback.Update(output, []));
        Assert.True(output.TryGet(channel, out float value));
        Assert.Equal(0, value);
        output.Clear();
        Assert.False(playback.Update(output, []));
        Assert.False(output.TryGet(channel, out _));
    }

    private static Duration Ticks(long ticks) => Duration.FromTicks(ticks);
}
