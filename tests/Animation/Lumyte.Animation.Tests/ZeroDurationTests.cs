using Lumyte.Core.Time;
using Xunit;
using static Lumyte.Animation.ComposeAnimation;

namespace Lumyte.Animation.Tests;

/// <summary>Verifies instantaneous sources, timelines and playback boundaries.</summary>
public sealed class ZeroDurationTests
{
    /// <summary>All built-in value sources accept zero duration and expose their value at time zero.</summary>
    [Fact]
    public void SourcesAcceptZeroDuration()
    {
        var tween = new Tween<float>(1, 2, Duration.Zero, AnimationInterpolators.Float, AnimationEasing.Linear);
        var curve = new AnimationCurve<float>(Duration.Zero, [new(Duration.Zero, 3)], AnimationInterpolators.Float);
        var hermite = new AnimationHermiteCurve<float>(Duration.Zero, [new(Duration.Zero, 4, 0, 0)], AnimationHermiteInterpolators.Float);

        Assert.Equal(2, tween.Sample(Duration.Zero));
        Assert.Equal(3, curve.Sample(Duration.Zero));
        Assert.Equal(4, hermite.Sample(Duration.Zero));
    }

    /// <summary>The first update evaluates an instant once, reports its marker and completes.</summary>
    [Fact]
    public void OncePlaybackEvaluatesInstantOnFirstUpdate()
    {
        var channel = AnimationChannel<float>.Create();
        var builder = new AnimationTimelineBuilder();
        builder.Add(Duration.Zero, new Tween<float>(1, 2, Duration.Zero, AnimationInterpolators.Float, AnimationEasing.Linear), channel);
        builder.AddEvent(Duration.Zero, "Instant");
        var playback = new AnimationPlayback(new ManualClock(), builder.Build());
        var output = new AnimationOutput();
        var events = new List<AnimationEventOccurrence>();

        playback.Play();
        Assert.True(playback.Update(output, events));
        Assert.Equal(AnimationPlaybackState.Completed, playback.State);
        Assert.Equal("Instant", Assert.Single(events).Event.Name);
        Assert.True(output.TryGet(channel, out float value));
        Assert.Equal(2, value);

        events.Clear();
        output.Clear();
        Assert.False(playback.Update(output, events));
        Assert.Empty(events);
        Assert.True(output.TryGet(channel, out value));
        Assert.Equal(2, value);
    }

    /// <summary>Release, Repeat and Reverse retain their boundary rules for instantaneous children.</summary>
    [Fact]
    public void InstantOperatorsPreserveReleaseAndEventMultiplicity()
    {
        var channel = AnimationChannel<float>.Create();
        var child = new AnimationTimelineBuilder();
        child.Add(Duration.Zero, new Tween<float>(1, 2, Duration.Zero, AnimationInterpolators.Float, AnimationEasing.Linear), channel, AnimationFillMode.Release);
        child.AddEvent(Duration.Zero, "Instant");
        AnimationTimeline timeline = child.Build().Reverse().Repeat(3);
        var playback = new AnimationPlayback(new ManualClock(), timeline);
        var output = new AnimationOutput();
        var events = new List<AnimationEventOccurrence>();

        playback.Play();
        Assert.True(playback.Update(output, events));
        Assert.Equal(3, events.Count);
        Assert.All(events, occurrence => Assert.Equal("Instant", occurrence.Event.Name));
        Assert.True(output.TryGet(channel, out float value));
        Assert.Equal(2, value);

        output.Clear();
        events.Clear();
        Assert.False(playback.Update(output, events));
        Assert.Empty(events);
        Assert.False(output.TryGet(channel, out _));
    }

    /// <summary>Looping an instant holds position zero and emits its start marker only once per Play.</summary>
    [Fact]
    public void InstantLoopDoesNotDivideByZeroOrRepeatWithoutTime()
    {
        var builder = new AnimationTimelineBuilder();
        builder.AddEvent(Duration.Zero, "Instant");
        var playback = new AnimationPlayback(new ManualClock(), builder.Build(), AnimationWrapMode.Loop);
        var events = new List<AnimationEventOccurrence>();

        playback.Play();
        Assert.False(playback.Update(new AnimationOutput(), events));
        Assert.Equal(AnimationPlaybackState.Playing, playback.State);
        Assert.Equal(Duration.Zero, playback.Position);
        Assert.Single(events);
        events.Clear();
        Assert.False(playback.Update(new AnimationOutput(), events));
        Assert.Empty(events);
    }

    /// <summary>Composition and animation state machines accept an empty instantaneous timeline.</summary>
    [Fact]
    public void CompositionAndStateMachineAcceptZeroDuration()
    {
        AnimationTimeline timeline = Timeline().Build();
        var builder = new AnimationStateMachineBuilder<string, bool>();
        builder.AddState("Instant", timeline);
        AnimationStateMachine<string, bool> machine = builder.Build(new ManualClock(), "Instant", false);

        Assert.False(machine.Update(false, new AnimationOutput(), [], []));
        Assert.Equal(AnimationStateMachineStatus.Running, machine.Status);
        Assert.Equal(Duration.Zero, machine.Position);
    }
}
