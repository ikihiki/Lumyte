using Lumyte.Core.Time;

using Xunit;

using static Lumyte.Animation.AnimationKit;

namespace Lumyte.Animation.Tests;

public sealed class AnimationStateMachineTests
{
    [Fact]
    public void TriggerTransitionsToTheConfiguredState()
    {
        var clock = new ManualClock();
        AnimationChannel<float> opacity = Channel<float>("Opacity");
        AnimationState hidden = new("Hidden", CreateClip("Hidden", opacity, 0f));
        AnimationState visible = new("Visible", CreateClip("Visible", opacity, 1f));
        var machine = new AnimationStateMachine(
            hidden,
            [hidden, visible],
            [new AnimationTransition(hidden, visible, "Show", Duration.Zero)]);
        var values = new List<float>();
        var player = new AnimationPlayer(clock);
        var controller = new AnimationStateMachineController(
            machine,
            player,
            new AnimationTarget().Bind(opacity, values.Add));
        controller.Start();

        bool transitioned = controller.Fire("Show");

        Assert.True(transitioned);
        Assert.Same(visible, controller.CurrentState);
        Assert.Equal(1f, values[^1]);
    }

    [Fact]
    public void TimedTransitionBlendsAndContinuesTheDestination()
    {
        var clock = new ManualClock();
        AnimationChannel<float> opacity = Channel<float>("Opacity");
        AnimationState hidden = new("Hidden", CreateClip("Hidden", opacity, 0f));
        AnimationState visible = new("Visible", CreateClip("Visible", opacity, 1f));
        var blend = new AnimationBlend().Use(opacity, Interpolators.Float);
        var machine = new AnimationStateMachine(
            hidden,
            [hidden, visible],
            [new AnimationTransition(
                hidden,
                visible,
                "Show",
                Duration.FromSeconds(0.5),
                blend)]);
        var values = new List<float>();
        var player = new AnimationPlayer(clock);
        var controller = new AnimationStateMachineController(
            machine,
            player,
            new AnimationTarget().Bind(opacity, values.Add));
        controller.Start();

        controller.Fire("Show");
        clock.Advance(Duration.FromSeconds(0.25));
        player.Update();
        float midpoint = values[^1];
        clock.Advance(Duration.FromSeconds(0.25));
        player.Update();

        Assert.Equal(0.5f, midpoint);
        Assert.Same(visible, controller.CurrentState);
        Assert.Equal(1f, values[^1]);
        Assert.Equal(1, player.ActiveCount);
    }

    [Fact]
    public void UnknownTriggerLeavesTheCurrentStateUnchanged()
    {
        var clock = new ManualClock();
        AnimationChannel<float> opacity = Channel<float>("Opacity");
        AnimationState hidden = new("Hidden", CreateClip("Hidden", opacity, 0f));
        var machine = new AnimationStateMachine(hidden, [hidden], []);
        var controller = new AnimationStateMachineController(
            machine,
            new AnimationPlayer(clock),
            new AnimationTarget().Bind(opacity, _ => { }));

        bool transitioned = controller.Fire("Unknown");

        Assert.False(transitioned);
        Assert.Same(hidden, controller.CurrentState);
    }

    private static AnimationClip CreateClip(
        string name,
        AnimationChannel<float> channel,
        float value)
    {
        AnimationTrack<float> track = Track(channel, Interpolators.Float)[
            Keyframe(Duration.Zero, value),
            Keyframe(Duration.FromSeconds(1), value)
        ];
        return Clip(name)[track];
    }
}
