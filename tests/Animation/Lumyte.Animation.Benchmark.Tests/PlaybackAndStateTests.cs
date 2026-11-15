using Lumyte.Core.Time;
using Lumyte.StateMachines;
using Xunit;

namespace Lumyte.Animation.Benchmark.Tests;

/// <summary>Analytic workload oracles, repeated-call stability and boundary operation counters.</summary>
public sealed class PlaybackAndStateTests
{
    /// <summary>Every finite playback wave remains active and deterministic over repeated calls.</summary>
    /// <param name="count">The independent instance count.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(32)]
    [InlineData(256)]
    public void PlaybackWavesRemainNonvacuous(int count)
    {
        var benchmark = new PlaybackBenchmarks { Count = count };
        benchmark.Setup();
        for (int repeat = 0; repeat < 100; repeat++)
        {
            Assert.Equal(15d * count, benchmark.IndependentLoopWave(), 5);
            Assert.Equal(26d, benchmark.OnceCompletionWave(), 5);
            Assert.Equal(40d, benchmark.PingPongWave(), 5);
            Assert.Equal(13.5d, benchmark.PlaybackControlsCycle(), 5);
        }
    }

    /// <summary>Marker traversal has analytic occurrence counts, offsets and loop-index sums.</summary>
    /// <param name="count">The marker count.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(16)]
    public void TraversalProfilesHaveExpectedChecksums(int count)
    {
        long positions = (count * 10L) + (5L * count * (count - 1) / 2);
        foreach (string profile in new[] { "Forward", "Reverse", "Wrap" })
        {
            var benchmark = new EventTraversalBenchmarks { MarkerCount = count, Profile = profile };
            benchmark.Setup();
            long expected = profile switch
            {
                "Reverse" => count + (100L * count) - positions,
                "Wrap" => (2 * positions) + (103L * count),
                _ => count + positions,
            };
            for (int repeat = 0; repeat < 100; repeat++)
            {
                Assert.Equal(expected, benchmark.TraverseWave());
            }
        }
    }

    /// <summary>Composition construction reports its exact compact repeated duration.</summary>
    [Fact]
    public void CompositionAndTypedOutputAreStable()
    {
        var timeline = new TimelineBenchmarks();
        timeline.Setup();
        Assert.Equal(Duration.FromTicks(880), timeline.BuildComposition().Duration);

        // Each cycle samples 3.5, 9, 5.5 and zero, plus two crossed markers.
        Assert.Equal(80d, timeline.SampleCompositionWave(), 5);
        Assert.Equal(80d, timeline.SampleCompositionWave(), 5);
        var output = new OutputBenchmarks();
        output.Setup();
        for (int repeat = 0; repeat < 100; repeat++)
        {
            Assert.Equal(215d, output.MergeAndClear(), 5);
        }
    }

    /// <summary>Machine benchmark cycles consume two transitions, two markers and a midpoint sample.</summary>
    [Fact]
    public void MachineCyclesRemainReusable()
    {
        var benchmark = new AnimationStateMachineBenchmarks();
        benchmark.Setup();
        for (int repeat = 0; repeat < 100; repeat++)
        {
            Assert.Equal(0d, benchmark.RetainedNoTransition());
            Assert.Equal(11d, benchmark.TriggerCompletionCycle());
            Assert.Equal(0d, benchmark.LifecycleCycle());
            AnimationStateMachine<int, bool> machine = benchmark.ConstructAndStart();
            Assert.Equal(AnimationStateMachineStatus.Running, machine.Status);
            Assert.Equal(0, machine.CurrentState);
            Assert.Equal(Duration.Zero, machine.Position);
        }
    }

    /// <summary>Actual samples, completion flags and clock reads distinguish zero, initial, boundary and dormant updates.</summary>
    /// <param name="length">The timeline duration in ticks.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    public void PlaybackCountersAndCompletionBoundaries(long length)
    {
        var clock = new CountingClock();
        var source = new CountingSource(Duration.FromTicks(length));
        var channel = AnimationChannel<float>.Create();
        var builder = new AnimationTimelineBuilder();
        builder.Add(Duration.Zero, source, channel);
        builder.AddEvent(Duration.FromTicks(length), "End");
        var playback = new AnimationPlayback(clock, builder.Build());
        var output = new AnimationOutput();
        var events = new List<AnimationEventOccurrence>();
        Assert.False(playback.Update(output, events));
        Assert.Equal(0, source.Samples);
        playback.Play();
        int reads = clock.Reads;
        bool completed = playback.Update(output, events);
        Assert.Equal(reads + 1, clock.Reads);
        Assert.Equal(length == 0, completed);
        Assert.Equal(length == 0 ? 1 : 0, events.Count);
        Assert.True(output.TryGet(channel, out float initial));
        Assert.Equal(0, initial);
        Assert.Equal(1, source.Samples);
        if (length > 0)
        {
            events.Clear();
            clock.Advance(Duration.FromTicks(length));
            Assert.True(playback.Update(output, events));
            Assert.Equal("End", Assert.Single(events).Event.Name);
            Assert.True(output.TryGet(channel, out float final));
            Assert.Equal(length, final);
            Assert.Equal(2, source.Samples);
        }

        Assert.Equal(AnimationPlaybackState.Completed, playback.State);
        Assert.False(playback.Update(output, []));
        playback.Cancel();
        output.Clear();
        int samples = source.Samples;
        Assert.False(playback.Update(output, []));
        Assert.False(output.TryGet(channel, out _));
        Assert.Equal(samples, source.Samples);
    }

    /// <summary>Reverse traversal emits boundary markers in reflected order and does not duplicate a completed crossing.</summary>
    [Fact]
    public void ReverseBoundaryMarkersHaveExactOrder()
    {
        var builder = new AnimationTimelineBuilder();
        builder.AddEvent(Duration.Zero, "Start");
        builder.AddEvent(Duration.FromTicks(100), "End");
        var clock = new ManualClock();
        var playback = new AnimationPlayback(clock, builder.Build().Reverse());
        var events = new List<AnimationEventOccurrence>();
        playback.Play();
        clock.Advance(Duration.FromTicks(100));
        Assert.True(playback.Update(new AnimationOutput(), events));
        Assert.Equal(new[] { "End", "Start" }, events.Select(item => item.Event.Name));
        Assert.Equal(new long[] { 0, 100 }, events.Select(item => item.UpdateOffset.Ticks));
        events.Clear();
        Assert.False(playback.Update(new AnimationOutput(), events));
        Assert.Empty(events);
    }

    /// <summary>Machine transitions, source sampling and marker times have independent operation-count oracles.</summary>
    [Fact]
    public void MachineCountersAndStateAreExact()
    {
        var clock = new CountingClock();
        var source = new CountingSource(Duration.FromTicks(100));
        var channel = AnimationChannel<float>.Create();
        var timeline = new AnimationTimelineBuilder();
        timeline.Add(Duration.Zero, source, channel);
        timeline.AddEvent(Duration.FromTicks(50), "Middle");
        var trigger = StateMachineTrigger.Create();
        var builder = new AnimationStateMachineBuilder<int, bool>();
        builder.AddState(0, timeline.Build(), AnimationWrapMode.Loop);
        builder.AddState(1, timeline.Build());
        builder.AddTransition(0, 1, trigger: trigger);
        builder.AddTransition(1, 0, onCompleted: true);
        AnimationStateMachine<int, bool> machine = builder.Build(clock, 0, true);
        Assert.Equal(0, source.Samples);
        var output = new AnimationOutput();
        var events = new List<AnimationStateEvent<int>>();
        var transitions = new List<AnimationStateTransition<int>>();
        int reads = clock.Reads;
        Assert.False(machine.Update(true, output, events, transitions));
        Assert.Equal(reads + 1, clock.Reads);
        Assert.Equal(1, source.Samples);
        machine.SetTrigger(trigger);
        machine.SetTrigger(trigger);
        Assert.True(machine.Update(true, output, events, transitions));
        Assert.Equal(1, machine.CurrentState);
        Assert.Equal(3, source.Samples);
        Assert.Single(transitions);
        transitions.Clear();
        clock.Advance(Duration.FromTicks(100));
        Assert.True(machine.Update(true, output, events, transitions));
        Assert.Equal(0, machine.CurrentState);
        Assert.Equal(5, source.Samples);
        Assert.Equal(1, Assert.Single(events).State);
        Assert.Equal(Duration.FromTicks(50), Assert.Single(events).OccurredAt - TimePoint.Zero);
        Assert.Single(transitions);
        Assert.True(output.TryGet(channel, out float value));
        Assert.Equal(0, value);
        machine.Stop();
        Assert.False(machine.Update(true, output, events, transitions));
        Assert.Equal(5, source.Samples);
    }

    private sealed class CountingClock : IMonotonicClock
    {
        private readonly ManualClock _clock = new();

        /// <summary>Gets observed public clock reads.</summary>
        public int Reads { get; private set; }

        /// <summary>Gets the counted monotonic clock time.</summary>
        public TimePoint Now
        {
            get
            {
                Reads++;
                return _clock.Now;
            }
        }

        /// <summary>Advances deterministic time.</summary>
        /// <param name="duration">The elapsed duration.</param>
        public void Advance(Duration duration) => _clock.Advance(duration);
    }

    private sealed class CountingSource(Duration duration) : IAnimationSource<float>
    {
        /// <summary>Gets the source duration.</summary>
        public Duration Duration { get; } = duration;

        /// <summary>Gets actual sampling calls.</summary>
        public int Samples { get; private set; }

        /// <summary>Samples elapsed ticks while counting calls.</summary>
        /// <param name="time">The local time.</param>
        /// <returns>The local ticks.</returns>
        public float Sample(Duration time)
        {
            Samples++;
            return time.Ticks;
        }
    }
}
