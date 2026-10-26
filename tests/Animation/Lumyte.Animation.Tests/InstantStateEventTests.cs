namespace Lumyte.Animation.Tests;

/// <summary>Checks the common event time base when entering an instantaneous animation state.</summary>
public sealed class InstantStateEventTests
{
    /// <summary>Incoming and outgoing markers retain the same update origin, including across a pause.</summary>
    /// <param name="pause">Whether a paused update separates the two running updates.</param>
    /// <param name="wrap">The incoming state's wrap mode.</param>
    [Theory]
    [InlineData(false, AnimationWrapMode.Once)]
    [InlineData(true, AnimationWrapMode.Once)]
    [InlineData(false, AnimationWrapMode.Loop)]
    [InlineData(true, AnimationWrapMode.Loop)]
    public void IncomingInstantMarkerUsesTheMachineEventTimeBase(bool pause, AnimationWrapMode wrap)
    {
        var clock = new ManualClock();
        clock.Advance(Duration.FromTicks(10));
        var outgoing = new AnimationTimelineBuilder();
        outgoing.SetDuration(Duration.FromTicks(10));
        outgoing.AddEvent(Duration.FromTicks(3), "Outgoing");
        var incoming = new AnimationTimelineBuilder();
        incoming.AddEvent(Duration.Zero, "Incoming");
        var trigger = StateMachineTrigger.Create();
        var builder = new AnimationStateMachineBuilder<string, bool>();
        builder.AddState("A", outgoing.Build());
        builder.AddState("B", incoming.Build(), wrap);
        builder.AddTransition("A", "B", trigger: trigger);
        AnimationStateMachine<string, bool> machine = builder.Build(clock, "A", false);
        var output = new AnimationOutput();
        var events = new List<AnimationStateEvent<string>>();
        var transitions = new List<AnimationStateTransition<string>>();

        clock.Advance(Duration.FromTicks(2));
        Assert.False(machine.Update(false, output, events, transitions));
        Assert.Empty(events);
        clock.Advance(Duration.FromTicks(1));
        if (pause)
        {
            machine.Pause();
        }

        clock.Advance(Duration.FromTicks(2));
        if (pause)
        {
            Assert.False(machine.Update(false, output, events, transitions));
            Assert.Empty(events);
            machine.Resume();
        }

        machine.SetTrigger(trigger);
        Assert.True(machine.Update(false, output, events, transitions));
        Assert.Equal(2, events.Count);
        Assert.Equal("A", events[0].State);
        Assert.Equal("Outgoing", events[0].Occurrence.Event.Name);
        Assert.Equal(TimePoint.FromTicks(13), events[0].OccurredAt);
        Assert.Equal(Duration.FromTicks(1), events[0].Occurrence.UpdateOffset);
        Assert.Equal("B", events[1].State);
        Assert.Equal("Incoming", events[1].Occurrence.Event.Name);
        Assert.Equal(TimePoint.FromTicks(15), events[1].OccurredAt);
        Assert.Equal(Duration.FromTicks(3), events[1].Occurrence.UpdateOffset);
        Assert.Equal(events[1].OccurredAt, Assert.Single(transitions).ObservedAt);
        Assert.All(events, item => Assert.Equal(item.OccurredAt, TimePoint.FromTicks(12) + item.Occurrence.UpdateOffset));

        events.Clear();
        transitions.Clear();
        clock.Advance(Duration.FromTicks(1));
        Assert.False(machine.Update(false, output, events, transitions));
        Assert.Empty(events);
        Assert.Empty(transitions);
    }
}
