using Lumyte.Core.Time;
using Xunit;

namespace Lumyte.Animation.Tests;

/// <summary>Verifies paused traversal and contribution priority across repetition boundaries.</summary>
public sealed class PlaybackTraversalTests
{
    /// <summary>Paused sampling preserves unreported events, release endpoints, and their clock offsets.</summary>
    [Fact]
    public void PausedUpdatesPreservePendingCrossingsUntilResume()
    {
        var channel = AnimationChannel<float>.Create();
        var builder = new AnimationTimelineBuilder();
        builder.Add(Duration.Zero, Linear(0, 10, 5), channel, AnimationFillMode.Release);
        builder.AddEvent(Duration.Zero, "Start");
        builder.AddEvent(Ticks(5), "BeforePause");
        builder.AddEvent(Ticks(12), "AfterResume");
        builder.SetDuration(Ticks(20));
        var clock = new ManualClock();
        var playback = new AnimationPlayback(clock, builder.Build());
        var output = new AnimationOutput();
        var events = new List<AnimationEventOccurrence>();
        playback.Play();
        clock.Advance(Ticks(10));
        playback.Pause();
        clock.Advance(Ticks(20));
        Assert.False(playback.Update(output, events));
        Assert.Empty(events);
        Assert.False(output.TryGet(channel, out _));
        clock.Advance(Ticks(10));
        Assert.False(playback.Update(output, events));
        Assert.Empty(events);
        playback.Resume();
        clock.Advance(Ticks(2));
        Assert.False(playback.Update(output, events));
        Assert.Equal(new[] { "Start", "BeforePause", "AfterResume" }, events.Select(e => e.Event.Name));
        Assert.Equal(new long[] { 0, 5, 42 }, events.Select(e => e.UpdateOffset.Ticks));
        Assert.True(output.TryGet(channel, out float value));
        Assert.Equal(10, value);
        events.Clear();
        output.Clear();
        playback.Update(output, events);
        Assert.Empty(events);
        Assert.False(output.TryGet(channel, out _));
    }

    /// <summary>A paused final position retains completion crossings for the resumed update.</summary>
    [Fact]
    public void PauseAtCompletionRetainsFinalEventAndReleaseValue()
    {
        var channel = AnimationChannel<float>.Create();
        var builder = new AnimationTimelineBuilder();
        builder.Add(Duration.Zero, Linear(0, 10, 10), channel, AnimationFillMode.Release);
        builder.AddEvent(Ticks(10), "Completed");
        var clock = new ManualClock();
        var playback = new AnimationPlayback(clock, builder.Build());
        var output = new AnimationOutput();
        var events = new List<AnimationEventOccurrence>();
        playback.Play();
        clock.Advance(Ticks(15));
        playback.Pause();
        Assert.False(playback.Update(output, events));
        Assert.Empty(events);
        playback.Resume();
        Assert.True(playback.Update(output, events));
        Assert.Equal(Ticks(10), Assert.Single(events).UpdateOffset);
        Assert.True(output.TryGet(channel, out float value));
        Assert.Equal(10, value);
    }

    /// <summary>New cycle values take precedence over prior cycle release endpoints.</summary>
    /// <param name="loop">Whether to use an outer playback loop instead of finite repetition.</param>
    /// <param name="time">The evaluation time in ticks.</param>
    /// <param name="expected">The expected current cycle value.</param>
    [Theory]
    [InlineData(false, 10, 0)]
    [InlineData(false, 11, 2)]
    [InlineData(false, 20, 0)]
    [InlineData(true, 10, 0)]
    [InlineData(true, 11, 2)]
    [InlineData(true, 20, 0)]
    public void NewCycleValuesOverrideEarlierCycleRelease(bool loop, long time, float expected)
    {
        var channel = AnimationChannel<float>.Create();
        AnimationTimeline child = TwoReleasedTracks(channel);
        AnimationTimeline timeline = loop ? child : child.Repeat(3);
        var clock = new ManualClock();
        var playback = new AnimationPlayback(clock, timeline, loop ? AnimationWrapMode.Loop : AnimationWrapMode.Once);
        var output = new AnimationOutput();
        playback.Play();
        clock.Advance(Ticks(time));
        playback.Update(output, []);
        Assert.True(output.TryGet(channel, out float value));
        Assert.Equal(expected, value);
        output.Clear();
        playback.Update(output, []);
        Assert.True(output.TryGet(channel, out value));
        Assert.Equal(expected, value);
    }

    /// <summary>Reverse traversal gives newer cycles priority while preserving nested registration order.</summary>
    [Fact]
    public void ReversedNestedRepeatPreservesCycleAndSiblingPriority()
    {
        var channel = AnimationChannel<float>.Create();
        AnimationTimeline repeated = TwoReleasedTracks(channel).Repeat(2).Repeat(2).Reverse();
        var clock = new ManualClock();
        var playback = new AnimationPlayback(clock, repeated);
        var output = new AnimationOutput();
        playback.Play();
        clock.Advance(Ticks(31));
        playback.Update(output, []);
        Assert.True(output.TryGet(channel, out float value));
        Assert.Equal(18, value);

        var builder = new AnimationTimelineBuilder();
        builder.AddTimeline(Duration.Zero, repeated);
        builder.Add(Duration.Zero, Linear(100, 100, 40), channel);
        var siblingClock = new ManualClock();
        var siblingPlayback = new AnimationPlayback(siblingClock, builder.Build());
        siblingPlayback.Play();
        siblingClock.Advance(Ticks(31));
        output.Clear();
        siblingPlayback.Update(output, []);
        Assert.True(output.TryGet(channel, out value));
        Assert.Equal(100, value);
    }

    /// <summary>Priority ranges do not restrict repetitions whose durations remain representable.</summary>
    [Fact]
    public void LargeValidRepeatRetainsSiblingPriorityWithoutExpandingDefinitions()
    {
        var channel = AnimationChannel<float>.Create();
        var child = new AnimationTimelineBuilder();
        child.Add(Duration.Zero, Linear(0, 1, 1), channel);
        child.Add(Duration.Zero, Linear(1, 2, 1), channel);
        child.Add(Duration.Zero, Linear(2, 3, 1), channel);
        child.Add(Duration.Zero, Linear(3, 4, 1), channel);
        child.Add(Duration.Zero, Linear(4, 5, 1), channel);
        AnimationTimeline repeated = child.Build().Repeat(int.MaxValue).Repeat(int.MaxValue);
        var builder = new AnimationTimelineBuilder();
        builder.AddTimeline(Duration.Zero, repeated);
        builder.Add(Duration.Zero, Linear(42, 42, 1), channel);
        var clock = new ManualClock();
        var playback = new AnimationPlayback(clock, builder.Build());
        playback.Play();
        long midpoint = repeated.Duration.Ticks / 2;
        clock.Advance(Ticks(midpoint));
        var output = new AnimationOutput();

        // Five parallel tracks place the later sibling above ulong.MaxValue.
        // At the midpoint, truncating that rank would put it below the active repeat.
        Assert.False(playback.Update(output, []));
        Assert.True(output.TryGet(channel, out float value));
        Assert.Equal(42, value);

        // The repeat's own active ranks also exceed ulong.MaxValue at its endpoint.
        output.Clear();
        clock.Advance(Ticks(repeated.Duration.Ticks - midpoint));
        Assert.True(playback.Update(output, []));
        Assert.True(output.TryGet(channel, out value));
        Assert.Equal(42, value);
    }

    private static Duration Ticks(long ticks) => Duration.FromTicks(ticks);

    private static Tween<float> Linear(float from, float to, long ticks) => new(from, to, Ticks(ticks), AnimationInterpolators.Float, AnimationEasing.Linear);

    private static AnimationTimeline TwoReleasedTracks(AnimationChannel<float> channel)
    {
        var builder = new AnimationTimelineBuilder();
        builder.Add(Duration.Zero, Linear(0, 10, 5), channel, AnimationFillMode.Release);
        builder.Add(Ticks(5), Linear(10, 20, 5), channel, AnimationFillMode.Release);
        return builder.Build();
    }
}
