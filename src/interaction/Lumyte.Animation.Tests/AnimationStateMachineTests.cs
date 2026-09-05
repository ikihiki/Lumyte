using Lumyte.Core.Time;
using Lumyte.StateMachine;

using Xunit;

using static Lumyte.Animation.AnimationKit;
using static Lumyte.StateMachine.StateMachineKit;

namespace Lumyte.Animation.Tests;

public sealed class AnimationStateMachineTests
{
    [Fact]
    public void TriggerTransitionsToTheConfiguredState()
    {
        var clock = new ManualClock();
        AnimationChannel<float> opacity = Channel<float>("Opacity");
        State<TestContext> hidden = State<TestContext>("Hidden");
        State<TestContext> visible = State<TestContext>("Visible");
        Transition<TestContext, TestTrigger> show =
            Transition(hidden, visible, TestTrigger.Show);
        StateMachine<TestContext, TestTrigger> definition =
            Machine<TestContext, TestTrigger>(hidden)[show];
        StateMachineInstance<TestContext, TestTrigger> machine =
            definition.CreateInstance(new TestContext());
        var binding = new AnimationStateMachineBinding<TestContext, TestTrigger>()
            .Bind(hidden, CreateClip("Hidden", opacity, 0f))
            .Bind(visible, CreateClip("Visible", opacity, 1f));
        var values = new List<float>();
        var player = new AnimationPlayer(clock);
        var controller = new AnimationStateMachineController<TestContext, TestTrigger>(
            machine,
            binding,
            player,
            new AnimationTarget().Bind(opacity, values.Add));
        controller.Start();

        bool transitioned = controller.Fire(TestTrigger.Show);

        Assert.True(transitioned);
        Assert.Same(visible, controller.CurrentState);
        Assert.Equal(1f, values[^1]);
    }

    [Fact]
    public void TimedTransitionBlendsAndContinuesTheDestination()
    {
        var clock = new ManualClock();
        AnimationChannel<float> opacity = Channel<float>("Opacity");
        State<TestContext> hidden = State<TestContext>("Hidden");
        State<TestContext> visible = State<TestContext>("Visible");
        Transition<TestContext, TestTrigger> show =
            Transition(hidden, visible, TestTrigger.Show);
        StateMachine<TestContext, TestTrigger> definition =
            Machine<TestContext, TestTrigger>(hidden)[show];
        StateMachineInstance<TestContext, TestTrigger> machine =
            definition.CreateInstance(new TestContext());
        var blend = new AnimationBlend().Use(opacity, Interpolators.Float);
        var binding = new AnimationStateMachineBinding<TestContext, TestTrigger>()
            .Bind(hidden, CreateClip("Hidden", opacity, 0f))
            .Bind(visible, CreateClip("Visible", opacity, 1f))
            .Bind(show, new AnimationTransition(Duration.FromSeconds(0.5), blend));
        var values = new List<float>();
        var player = new AnimationPlayer(clock);
        var controller = new AnimationStateMachineController<TestContext, TestTrigger>(
            machine,
            binding,
            player,
            new AnimationTarget().Bind(opacity, values.Add));
        controller.Start();

        controller.Fire(TestTrigger.Show);
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
        State<TestContext> hidden = State<TestContext>("Hidden");
        StateMachine<TestContext, TestTrigger> definition =
            Machine<TestContext, TestTrigger>(hidden);
        StateMachineInstance<TestContext, TestTrigger> machine =
            definition.CreateInstance(new TestContext());
        var binding = new AnimationStateMachineBinding<TestContext, TestTrigger>()
            .Bind(hidden, CreateClip("Hidden", opacity, 0f));
        var controller = new AnimationStateMachineController<TestContext, TestTrigger>(
            machine,
            binding,
            new AnimationPlayer(clock),
            new AnimationTarget().Bind(opacity, _ => { }));

        bool transitioned = controller.Fire(TestTrigger.Show);

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

    private sealed class TestContext;

    private enum TestTrigger
    {
        Show,
    }
}
