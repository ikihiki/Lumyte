namespace Lumyte.Animation.Tests;

/// <summary>Verifies instantaneous releases across nested timeline transformations.</summary>
public sealed class InstantTraversalRegressionTests
{
    /// <summary>Reversing preserves the start crossing even at the largest supported duration.</summary>
    /// <param name="reverseCount">The number of nested reversals.</param>
    /// <param name="length">The timeline length in ticks.</param>
    [Theory]
    [InlineData(1, 10)]
    [InlineData(2, 10)]
    [InlineData(3, 10)]
    [InlineData(4, 10)]
    [InlineData(1, long.MaxValue)]
    [InlineData(2, long.MaxValue)]
    public void ReversedStartPreservesInstantReleaseAndMarker(int reverseCount, long length)
    {
        var channel = AnimationChannel<float>.Create();
        var builder = new AnimationTimelineBuilder();
        Duration instantTime = Ticks(reverseCount % 2 == 0 ? 0 : length);
        builder.Add(instantTime, Instant(42), channel, AnimationFillMode.Release);
        builder.AddEvent(instantTime, "Instant");
        builder.SetDuration(Ticks(length));
        AnimationTimeline timeline = Reverse(builder.Build(), reverseCount);
        var clock = new ManualClock();
        var playback = new AnimationPlayback(clock, timeline);
        var output = new AnimationOutput();
        var events = new List<AnimationEventOccurrence>();

        playback.Play();
        clock.Advance(Ticks(1));
        Assert.False(playback.Update(output, events));
        Assert.True(output.TryGet(channel, out float value));
        Assert.Equal(42, value);
        Assert.Equal(Duration.Zero, Assert.Single(events).Event.Time);

        output.Clear();
        events.Clear();
        playback.Update(output, events);
        Assert.False(output.TryGet(channel, out _));
        Assert.Empty(events);
    }

    /// <summary>Nested reversals retain the terminal crossing without releasing before it.</summary>
    /// <param name="reverseCount">The number of nested reversals.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ReversedEndReleasesInstantOnlyAtItsBoundary(int reverseCount)
    {
        var channel = AnimationChannel<float>.Create();
        var builder = new AnimationTimelineBuilder();
        builder.Add(Ticks(reverseCount % 2 == 0 ? 10 : 0), Instant(42), channel, AnimationFillMode.Release);
        builder.SetDuration(Ticks(10));
        var clock = new ManualClock();
        var playback = new AnimationPlayback(clock, Reverse(builder.Build(), reverseCount));
        var output = new AnimationOutput();

        playback.Play();
        clock.Advance(Ticks(9));
        Assert.False(playback.Update(output, []));
        Assert.False(output.TryGet(channel, out _));
        clock.Advance(Ticks(1));
        Assert.True(playback.Update(output, []));
        Assert.True(output.TryGet(channel, out float value));
        Assert.Equal(42, value);
        output.Clear();
        playback.Update(output, []);
        Assert.False(output.TryGet(channel, out _));
    }

    /// <summary>A placed instantaneous reverse only releases when its own start is crossed.</summary>
    /// <param name="reverseCount">The number of nested reversals.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void PlacedInstantReverseDoesNotReleaseOutsideItsCrossing(int reverseCount)
    {
        var channel = AnimationChannel<float>.Create();
        var child = new AnimationTimelineBuilder();
        child.Add(Duration.Zero, Instant(42), channel, AnimationFillMode.Release);
        var builder = new AnimationTimelineBuilder();
        builder.AddTimeline(Ticks(5), Reverse(child.Build(), reverseCount));
        builder.SetDuration(Ticks(10));
        var clock = new ManualClock();
        var playback = new AnimationPlayback(clock, builder.Build());
        var output = new AnimationOutput();

        playback.Play();
        clock.Advance(Ticks(4));
        playback.Update(output, []);
        Assert.False(output.TryGet(channel, out _));
        clock.Advance(Ticks(2));
        playback.Update(output, []);
        Assert.True(output.TryGet(channel, out float value));
        Assert.Equal(42, value);
        output.Clear();
        clock.Advance(Ticks(1));
        playback.Update(output, []);
        Assert.False(output.TryGet(channel, out _));
    }

    /// <summary>Instantaneous repeat endpoints and samples share the same contribution priority.</summary>
    /// <param name="reverseCount">The number of nested repeat and reverse pairs.</param>
    /// <param name="holdLast">Whether the held track is registered after the released track.</param>
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    public void InstantRepeatAndReversePreserveTrackRegistrationPriority(int reverseCount, bool holdLast)
    {
        var channel = AnimationChannel<float>.Create();
        var builder = new AnimationTimelineBuilder();
        builder.Add(Duration.Zero, Instant(holdLast ? 42 : 99), channel, holdLast ? AnimationFillMode.Release : AnimationFillMode.Hold);
        builder.Add(Duration.Zero, Instant(holdLast ? 99 : 42), channel, holdLast ? AnimationFillMode.Hold : AnimationFillMode.Release);
        AnimationTimeline timeline = builder.Build();
        for (int index = 0; index < reverseCount; index++)
        {
            timeline = timeline.Repeat(2).Reverse();
        }

        var playback = new AnimationPlayback(new ManualClock(), timeline.Repeat(3));
        var output = new AnimationOutput();
        playback.Play();
        Assert.True(playback.Update(output, []));
        Assert.True(output.TryGet(channel, out float value));
        Assert.Equal(holdLast ? 99 : 42, value);
        output.Clear();
        playback.Update(output, []);
        Assert.True(output.TryGet(channel, out value));
        Assert.Equal(99, value);
    }

    private static Duration Ticks(long ticks) => Duration.FromTicks(ticks);

    private static Tween<float> Instant(float value) => new(value, value, Duration.Zero, AnimationInterpolators.Float, AnimationEasing.Linear);

    private static AnimationTimeline Reverse(AnimationTimeline timeline, int count)
    {
        for (int index = 0; index < count; index++)
        {
            timeline = timeline.Reverse();
        }

        return timeline;
    }
}
