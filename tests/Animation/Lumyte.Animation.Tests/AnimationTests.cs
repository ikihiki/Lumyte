using System.Numerics;
using Lumyte.Core.Time;
using Xunit;
using static Lumyte.Animation.ComposeAnimation;

namespace Lumyte.Animation.Tests;

/// <summary>Represents animation tests.</summary>
public sealed class AnimationTests
{
    /// <summary>Performs composition builds parallel and delayed values from another assembly.</summary>
    [Fact]
    public void CompositionBuildsParallelAndDelayedValuesFromAnotherAssembly()
    {
        var panel = AnimationChannel<float>.Create();
        var button = AnimationChannel<float>.Create();
        AnimationTimeline timeline = Timeline()[Sequence()[Parallel()[Track<float>(panel, Linear(0, 1, 20)), Sequence()[Delay(Ticks(20)), Track<float>(button, Linear(0, 1, 10))]], Marker("Opened")]].Build();
        var clock = new ManualClock();
        var playback = new AnimationPlayback(clock, timeline);
        var output = new AnimationOutput();
        var events = new List<AnimationEventOccurrence>();
        playback.Play();
        clock.Advance(Ticks(25));
        Assert.False(playback.Update(output, events));
        Assert.Equal(1, Get(output, panel));
        Assert.Equal(0.5f, Get(output, button));
        Assert.Empty(events);
        clock.Advance(Ticks(5));
        Assert.True(playback.Update(output, events));
        Assert.Equal("Opened", Assert.Single(events).Event.Name);
        Assert.Equal(Ticks(5), events[0].UpdateOffset);
        events.Clear();
        Assert.False(playback.Update(output, events));
        Assert.Empty(events);
    }

    /// <summary>Performs repeat reverse composition preserves finite endpoint.</summary>
    [Fact]
    public void RepeatReverseCompositionPreservesFiniteEndpoint()
    {
        var channel = AnimationChannel<float>.Create();
        ComposeAnimation.Definitions.Timeline forward = Timeline()[Track<float>(channel, Linear(0, 10, 10))];
        AnimationTimeline timeline = Timeline()[Repeat(3)[Sequence()[forward, Reverse()[forward]]]].Build();
        Assert.Equal(Ticks(60), timeline.Duration);
        var clock = new ManualClock();
        var playback = new AnimationPlayback(clock, timeline);
        var output = new AnimationOutput();
        playback.Play();
        clock.Advance(Ticks(15));
        playback.Update(output, []);
        Assert.Equal(5, Get(output, channel));
        clock.Advance(Ticks(45));
        Assert.True(playback.Update(output, []));
        Assert.Equal(0, Get(output, channel));
    }

    /// <summary>Performs repeat maps intermediate and final boundaries.</summary>
    /// <param name="ticks">The ticks.</param>
    /// <param name="expected">The expected.</param>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(10, 0)]
    [InlineData(29, 9)]
    [InlineData(30, 10)]
    public void RepeatMapsIntermediateAndFinalBoundaries(long ticks, float expected)
    {
        var channel = AnimationChannel<float>.Create();
        AnimationTimeline timeline = Clip(channel, 10).Repeat(3);
        Assert.Equal(expected, Evaluate(timeline, channel, ticks));
    }

    /// <summary>Performs reverse and double reverse map typed positions.</summary>
    /// <param name="ticks">The ticks.</param>
    /// <param name="expected">The expected.</param>
    [Theory]
    [InlineData(0, 10)]
    [InlineData(4, 6)]
    [InlineData(10, 0)]
    public void ReverseAndDoubleReverseMapTypedPositions(long ticks, float expected)
    {
        var channel = AnimationChannel<float>.Create();
        AnimationTimeline child = Clip(channel, 10);
        Assert.Equal(expected, Evaluate(child.Reverse(), channel, ticks));
        Assert.Equal((float)ticks, Evaluate(child.Reverse().Reverse(), channel, ticks));
    }

    /// <summary>Performs repeat does not leak held values into next cycles delay.</summary>
    [Fact]
    public void RepeatDoesNotLeakHeldValuesIntoNextCyclesDelay()
    {
        var channel = AnimationChannel<float>.Create();
        var builder = new AnimationTimelineBuilder();
        builder.Add(Ticks(5), Linear(0, 10, 5), channel);
        AnimationTimeline repeated = builder.Build().Repeat(3);
        var clock = new ManualClock();
        var playback = new AnimationPlayback(clock, repeated);
        var output = new AnimationOutput();
        playback.Play();
        clock.Advance(Ticks(9));
        playback.Update(output, []);
        Assert.True(output.TryGet(channel, out _));
        output.Clear();
        clock.Advance(Ticks(3));
        playback.Update(output, []);
        Assert.False(output.TryGet(channel, out _));
    }

    /// <summary>Performs release produces final value even when skipped and then disappears.</summary>
    [Fact]
    public void ReleaseProducesFinalValueEvenWhenSkippedAndThenDisappears()
    {
        var channel = AnimationChannel<float>.Create();
        var builder = new AnimationTimelineBuilder();
        builder.Add(Ticks(5), Linear(0, 1, 5), channel, AnimationFillMode.Release);
        builder.SetDuration(Ticks(20));
        var clock = new ManualClock();
        var playback = new AnimationPlayback(clock, builder.Build());
        var output = new AnimationOutput();
        playback.Play();
        clock.Advance(Ticks(15));
        playback.Update(output, []);
        Assert.Equal(1, Get(output, channel));
        output.Clear();
        playback.Update(output, []);
        Assert.False(output.TryGet(channel, out _));
    }

    /// <summary>Performs reverse release produces its leaving value once.</summary>
    [Fact]
    public void ReverseReleaseProducesItsLeavingValueOnce()
    {
        var channel = AnimationChannel<float>.Create();
        var builder = new AnimationTimelineBuilder();
        builder.Add(Ticks(5), Linear(2, 10, 5), channel, AnimationFillMode.Release);
        builder.SetDuration(Ticks(20));
        var clock = new ManualClock();
        var playback = new AnimationPlayback(clock, builder.Build().Reverse());
        var output = new AnimationOutput();
        playback.Play();
        clock.Advance(Ticks(12));
        playback.Update(output, []);
        Assert.Equal(6.8f, Get(output, channel), 4);
        output.Clear();
        clock.Advance(Ticks(5));
        playback.Update(output, []);
        Assert.Equal(2, Get(output, channel));
        output.Clear();
        playback.Update(output, []);
        Assert.False(output.TryGet(channel, out _));
    }

    /// <summary>Performs pause resume and speed changes preserve anchored positions.</summary>
    [Fact]
    public void PauseResumeAndSpeedChangesPreserveAnchoredPositions()
    {
        var channel = AnimationChannel<float>.Create();
        var clock = new ManualClock();
        var playback = new AnimationPlayback(clock, Clip(channel, 100));
        var output = new AnimationOutput();
        playback.Play();
        clock.Advance(Ticks(10));
        playback.Pause();
        clock.Advance(Ticks(80));
        playback.Update(output, []);
        Assert.Equal(10, Get(output, channel));
        playback.Resume();
        clock.Advance(Ticks(5));
        playback.Speed = 2;
        clock.Advance(Ticks(5));
        playback.Update(output, []);
        Assert.Equal(25, Get(output, channel));
        playback.Speed = 0;
        clock.Advance(Ticks(10));
        playback.Update(output, []);
        Assert.Equal(25, Get(output, channel));
        playback.Speed = 1;
        clock.Advance(Ticks(5));
        playback.Update(output, []);
        Assert.Equal(30, Get(output, channel), 4);
    }

    /// <summary>Performs speed changes retain event clock offsets including zero speed.</summary>
    [Fact]
    public void SpeedChangesRetainEventClockOffsetsIncludingZeroSpeed()
    {
        var clock = new ManualClock();
        var builder = new AnimationTimelineBuilder();
        builder.AddEvent(Ticks(5), "A");
        builder.AddEvent(Ticks(20), "B");
        builder.SetDuration(Ticks(100));
        var playback = new AnimationPlayback(clock, builder.Build());
        var events = new List<AnimationEventOccurrence>();
        playback.Play();
        clock.Advance(Ticks(10));
        playback.Speed = 2;
        clock.Advance(Ticks(5));
        playback.Speed = 0;
        clock.Advance(Ticks(10));
        playback.Update(new AnimationOutput(), events);
        Assert.Equal(new long[] { 5, 15 }, events.Select(e => e.UpdateOffset.Ticks));
    }

    /// <summary>Performs separate clocks advance independently and update reads once.</summary>
    [Fact]
    public void SeparateClocksAdvanceIndependentlyAndUpdateReadsOnce()
    {
        var channel = AnimationChannel<float>.Create();
        var clock = new CountingClock();
        var gameClock = new ManualClock();
        var ui = new AnimationPlayback(clock, Clip(channel, 100));
        var game = new AnimationPlayback(gameClock, Clip(channel, 100));
        ui.Play();
        game.Play();
        clock.Advance(Ticks(25));
        int reads = clock.Reads;
        var output = new AnimationOutput();
        ui.Update(output, []);
        Assert.Equal(reads + 1, clock.Reads);
        Assert.Equal(25, Get(output, channel));
        output.Clear();
        game.Update(output, []);
        Assert.Equal(0, Get(output, channel));
        clock.Rewind();
        Assert.Throws<InvalidOperationException>(() => ui.Update(output, []));
    }

    /// <summary>Performs split and batched updates have identical nested reversed marker order.</summary>
    [Fact]
    public void SplitAndBatchedUpdatesHaveIdenticalNestedReversedMarkerOrder()
    {
        var builder = new AnimationTimelineBuilder();
        builder.AddEvent(Duration.Zero, "Start");
        builder.AddEvent(Ticks(2), "A");
        builder.AddEvent(Ticks(2), "B");
        builder.AddEvent(Ticks(10), "End");
        AnimationTimeline timeline = builder.Build().Repeat(3).Reverse();
        AnimationEventOccurrence[] one = Collect(timeline, 30);
        AnimationEventOccurrence[] split = Collect(timeline, 2, 8, 1, 9, 10);
        Assert.Equal(one.Select(e => (e.Event, e.LoopIndex)), split.Select(e => (e.Event, e.LoopIndex)));
        Assert.Equal(new[] { "End", "A", "B", "Start", "End", "A", "B", "Start", "End", "A", "B", "Start" }, one.Select(e => e.Event.Name));
    }

    /// <summary>Performs loop emits previous end before next start without duplicates.</summary>
    [Fact]
    public void LoopEmitsPreviousEndBeforeNextStartWithoutDuplicates()
    {
        var builder = new AnimationTimelineBuilder();
        builder.AddEvent(Duration.Zero, "Start");
        builder.AddEvent(Ticks(10), "End");
        AnimationEventOccurrence[] events = Collect(builder.Build(), AnimationWrapMode.Loop, 10, 10);
        Assert.Equal(new[] { "Start", "End", "Start", "End", "Start" }, events.Select(e => e.Event.Name));
        Assert.Equal(new long[] { 0, 0, 1, 1, 2 }, events.Select(e => e.LoopIndex));
    }

    /// <summary>Performs seek cancel stop and completion do not replay events.</summary>
    [Fact]
    public void SeekCancelStopAndCompletionDoNotReplayEvents()
    {
        var channel = AnimationChannel<float>.Create();
        var builder = new AnimationTimelineBuilder();
        builder.Add(Duration.Zero, Linear(0, 10, 10), channel);
        builder.AddEvent(Duration.Zero, "Start");
        builder.AddEvent(Ticks(5), "Middle");
        builder.AddEvent(Ticks(10), "End");
        var clock = new ManualClock();
        var playback = new AnimationPlayback(clock, builder.Build());
        var output = new AnimationOutput();
        var events = new List<AnimationEventOccurrence>();
        playback.Play();
        playback.Seek(Ticks(5));
        playback.Update(output, events);
        Assert.Equal(5, Get(output, channel));
        Assert.Empty(events);
        clock.Advance(Ticks(5));
        Assert.True(playback.Update(output, events));
        Assert.Equal("End", Assert.Single(events).Event.Name);
        playback.Play();
        playback.Cancel();
        output.Clear();
        events.Clear();
        clock.Advance(Ticks(10));
        playback.Update(output, events);
        Assert.False(output.TryGet(channel, out _));
        Assert.Empty(events);
        playback.Stop();
        Assert.Equal(Duration.Zero, playback.Position);
    }

    /// <summary>Performs builds snapshot collections and reject invalid graphs and durations.</summary>
    [Fact]
    public void BuildsSnapshotCollectionsAndRejectInvalidGraphsAndDurations()
    {
        var channel = AnimationChannel<float>.Create();
        ComposeAnimation.Definitions.TimelineItem[] children = [Track<float>(channel, Linear(0, 10, 10))];
        ComposeAnimation.Definitions.Timeline node = Timeline()[children];
        AnimationTimeline built = node.Build();
        children[0] = Delay(Ticks(100));
        Assert.Equal(Ticks(10), built.Duration);
        Assert.Equal(5, Evaluate(built, channel, 5));
        node.Children = [node];
        Assert.Throws<ArgumentException>(() => node.Build());
        Assert.Throws<ArgumentException>(() => Timeline()[Repeat(2)].Build());
        Assert.Throws<ArgumentException>(() => Timeline()[Reverse()[Delay(Ticks(1)), Delay(Ticks(1))]].Build());
        Assert.Throws<ArgumentOutOfRangeException>(() => built.Repeat(0));
        var large = new AnimationTimelineBuilder();
        large.SetDuration(Ticks(long.MaxValue));
        Assert.Throws<OverflowException>(() => large.Build().Repeat(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => Duration.FromSeconds(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ManualClock().Advance(Ticks(-1)));
    }

    /// <summary>Performs curves copy keys and validate finite and typed interpolation.</summary>
    [Fact]
    public void CurvesCopyKeysAndValidateFiniteAndTypedInterpolation()
    {
        AnimationKey<float>[] keys = [new(Ticks(2), 2), new(Ticks(8), 8)];
        var curve = new AnimationCurve<float>(Ticks(10), keys, AnimationInterpolators.Float);
        keys[0] = new(Ticks(2), 100);
        Assert.Equal(2, curve.Sample(Duration.Zero));
        Assert.Equal(5, curve.Sample(Ticks(5)));
        Assert.Equal(8, curve.Sample(Ticks(10)));
        Assert.Throws<ArgumentException>(() => new AnimationCurve<float>(Ticks(10), [new(Ticks(2), 1), new(Ticks(2), 2)], AnimationInterpolators.Float));
        Assert.Throws<ArgumentOutOfRangeException>(() => Linear(float.NaN, 1, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Tween<Quaternion>(default, Quaternion.Identity, Ticks(10), AnimationInterpolators.Quaternion, AnimationEasing.Linear));
        var discrete = new Tween<string>("old", "new", Ticks(10), AnimationInterpolators.Step<string>(), AnimationEasing.Linear);
        Assert.Equal("old", discrete.Sample(Ticks(5)));
        Assert.Equal("new", discrete.Sample(Ticks(10)));
        var rotation = new Tween<Quaternion>(Quaternion.Identity, -Quaternion.Identity, Ticks(10), AnimationInterpolators.Quaternion, AnimationEasing.Linear);
        Assert.Equal(1, MathF.Abs(Quaternion.Dot(Quaternion.Identity, rotation.Sample(Ticks(5)))), 5);
    }

    /// <summary>Performs validation does not partially update consumer results.</summary>
    [Fact]
    public void ValidationDoesNotPartiallyUpdateConsumerResults()
    {
        var channel = AnimationChannel<float>.Create();
        var clock = new ManualClock();
        var playback = new AnimationPlayback(clock, Clip(channel, 10));
        var output = new AnimationOutput();
        playback.Play();
        clock.Advance(Ticks(3));
        playback.Update(output, []);
        Assert.Throws<ArgumentOutOfRangeException>(() => playback.Seek(Ticks(11)));
        Assert.Equal(Ticks(3), playback.Position);
        Assert.Equal(3, Get(output, channel));
        Assert.Throws<ArgumentOutOfRangeException>(() => playback.Speed = double.NaN);
        Assert.Equal(1, playback.Speed);
    }

    /// <summary>Performs steady numeric playback does not allocate or box values.</summary>
    [Fact]
    public void SteadyNumericPlaybackDoesNotAllocateOrBoxValues()
    {
        var channel = AnimationChannel<float>.Create();
        var clock = new ManualClock();
        var playback = new AnimationPlayback(clock, Clip(channel, 100000), AnimationWrapMode.Loop);
        var output = new AnimationOutput();
        var events = new List<AnimationEventOccurrence>();
        playback.Play();
        for (int i = 0; i < 100; i++)
        {
            clock.Advance(Ticks(1));
            output.Clear();
            playback.Update(output, events);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            clock.Advance(Ticks(1));
            output.Clear();
            playback.Update(output, events);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    /// <summary>Verifies registration priority across skipped release endpoints and active values.</summary>
    [Fact]
    public void LaterReleaseEndpointWinsOverEarlierHeldValueInTheSameEvaluation()
    {
        var channel = AnimationChannel<float>.Create();
        var builder = new AnimationTimelineBuilder();
        builder.Add(Duration.Zero, Linear(0, 10, 100), channel);
        builder.Add(Duration.Zero, Linear(0, 50, 10), channel, AnimationFillMode.Release);
        var clock = new ManualClock();
        var playback = new AnimationPlayback(clock, builder.Build());
        var output = new AnimationOutput();
        playback.Play();
        clock.Advance(Ticks(20));
        playback.Update(output, []);
        Assert.Equal(50, Get(output, channel));
        output.Clear();
        playback.Update(output, []);
        Assert.Equal(2, Get(output, channel));
    }

    private static Duration Ticks(long ticks) => Duration.FromTicks(ticks);

    private static Tween<float> Linear(float from, float to, long ticks) => new(from, to, Ticks(ticks), AnimationInterpolators.Float, AnimationEasing.Linear);

    private static float Get(AnimationOutput output, AnimationChannel<float> channel)
    {
        Assert.True(output.TryGet(channel, out float value));
        return value;
    }

    private static AnimationTimeline Clip(AnimationChannel<float> channel, long ticks)
    {
        var builder = new AnimationTimelineBuilder();
        builder.Add(Duration.Zero, Linear(0, ticks, ticks), channel);
        return builder.Build();
    }

    private static float Evaluate(AnimationTimeline timeline, AnimationChannel<float> channel, long ticks)
    {
        var clock = new ManualClock();
        var playback = new AnimationPlayback(clock, timeline);
        var output = new AnimationOutput();
        playback.Play();
        clock.Advance(Ticks(ticks));
        playback.Update(output, []);
        return Get(output, channel);
    }

    private static AnimationEventOccurrence[] Collect(AnimationTimeline timeline, params long[] steps) => Collect(timeline, AnimationWrapMode.Once, steps);

    private static AnimationEventOccurrence[] Collect(AnimationTimeline timeline, AnimationWrapMode wrap, params long[] steps)
    {
        var clock = new ManualClock();
        var playback = new AnimationPlayback(clock, timeline, wrap);
        var events = new List<AnimationEventOccurrence>();
        playback.Play();
        foreach (long step in steps)
        {
            clock.Advance(Ticks(step));
            playback.Update(new AnimationOutput(), events);
        }

        return [.. events];
    }

    private sealed class CountingClock : IMonotonicClock
    {
        private long _ticks;

        /// <summary>Gets the reads.</summary>
        public int Reads { get; private set; }

        /// <summary>Gets the now.</summary>
        public TimePoint Now
        {
            get
            {
                Reads++;
                return TimePoint.FromTicks(_ticks);
            }
        }

        /// <summary>Advances the monotonic clock by a nonnegative duration.</summary>
        /// <param name="duration">The duration.</param>
        public void Advance(Duration duration) => _ticks += duration.Ticks;

        /// <summary>Performs rewind.</summary>
        public void Rewind() => _ticks = 0;
    }
}
