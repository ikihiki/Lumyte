using Lumyte.Core.Time;
using Lumyte.StateMachines;
using Xunit;

namespace Lumyte.Animation.Tests;

/// <summary>Checks contribution lifetimes when outputs reuse slots across states and evaluations.</summary>
public sealed class AnimationOutputReuseTests
{
    /// <summary>Clears mixed typed contributions and reactivates slots without losing other producers' values.</summary>
    [Fact]
    public void ReusedSlotsKeepOnlyActiveStateContributions()
    {
        var clock = new ManualClock();
        var valueChannel = AnimationChannel<float>.Create();
        var textChannel = AnimationChannel<string>.Create();
        var externalChannel = AnimationChannel<float>.Create();
        var first = new AnimationTimelineBuilder();
        first.Add(Duration.Zero, Scalar(1), valueChannel);
        first.Add(Duration.Zero, new Tween<string>("first", "first", Duration.FromSeconds(1), AnimationInterpolators.Step<string>(), AnimationEasing.Linear), textChannel);
        var second = new AnimationTimelineBuilder();
        second.Add(Duration.Zero, Scalar(2), valueChannel);
        var external = new AnimationTimelineBuilder();
        external.Add(Duration.Zero, Scalar(9), externalChannel);
        var trigger = StateMachineTrigger.Create();
        var builder = new AnimationStateMachineBuilder<int, bool>();
        builder.AddState(0, first.Build());
        builder.AddState(1, second.Build());
        builder.AddTransition(0, 1, trigger: trigger);
        builder.AddTransition(1, 0, trigger: trigger);
        AnimationStateMachine<int, bool> machine = builder.Build(clock, 0, true);
        var other = new AnimationPlayback(clock, external.Build());
        other.Play();
        var output = new AnimationOutput();
        var otherEvents = new List<AnimationEventOccurrence>();
        var events = new List<AnimationStateEvent<int>>();
        var transitions = new List<AnimationStateTransition<int>>();
        for (int index = 0; index < 6; index++)
        {
            output.Clear();
            output.Clear();
            Assert.False(output.TryGet(valueChannel, out _));
            Assert.False(output.TryGet(textChannel, out _));
            other.Update(output, otherEvents);
            machine.Update(true, output, events, transitions);
            machine.Update(true, output, events, transitions);
            Assert.True(output.TryGet(externalChannel, out float untouched));
            Assert.Equal(9, untouched);
            Assert.True(output.TryGet(valueChannel, out float value));
            Assert.Equal(index % 2 == 0 ? 1 : 2, value);
            Assert.Equal(index % 2 == 0, output.TryGet(textChannel, out string? text));
            Assert.Equal(index % 2 == 0 ? "first" : null, text);
            machine.SetTrigger(trigger);
        }
    }

    /// <summary>Releasing a reused slot produces its endpoint once and leaves no stale contribution after clear.</summary>
    [Fact]
    public void ClearAndReactivationPreserveReleaseAndLaterEvaluationPriority()
    {
        var clock = new ManualClock();
        var channel = AnimationChannel<float>.Create();
        var released = new AnimationTimelineBuilder();
        released.Add(Duration.Zero, new Tween<float>(0, 5, Duration.FromTicks(5), AnimationInterpolators.Float, AnimationEasing.Linear), channel, AnimationFillMode.Release);
        released.SetDuration(Duration.FromTicks(10));
        var held = new AnimationTimelineBuilder();
        held.Add(Duration.Zero, Scalar(9), channel);
        var releasePlayback = new AnimationPlayback(clock, released.Build());
        var holdPlayback = new AnimationPlayback(clock, held.Build());
        releasePlayback.Play();
        holdPlayback.Play();
        var output = new AnimationOutput();
        var events = new List<AnimationEventOccurrence>();
        clock.Advance(Duration.FromTicks(6));
        releasePlayback.Update(output, events);
        Assert.True(output.TryGet(channel, out float endpoint));
        Assert.Equal(5, endpoint);
        holdPlayback.Update(output, events);
        Assert.True(output.TryGet(channel, out float heldValue));
        Assert.Equal(9, heldValue);
        output.Clear();
        releasePlayback.Update(output, events);
        Assert.False(output.TryGet(channel, out _));
        holdPlayback.Update(output, events);
        Assert.True(output.TryGet(channel, out heldValue));
        Assert.Equal(9, heldValue);
    }

    private static Tween<float> Scalar(float value) => new(value, value, Duration.FromSeconds(1), AnimationInterpolators.Float, AnimationEasing.Linear);
}
