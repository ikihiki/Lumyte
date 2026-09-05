using Lumyte.Core.Time;
using Lumyte.StateMachine;

using Xunit;

using static Lumyte.Animation.AnimationKit;
using static Lumyte.StateMachine.StateMachineKit;

namespace Lumyte.Animation.Tests;

public sealed class AnimationMachineTests
{
    [Fact]
    public void FacadePlaysStateTimelinesAndCrossfadesTransitions()
    {
        var clock = new ManualClock();
        AnimationChannel<float> opacity = Channel<float>("Opacity");
        State<TestContext> hidden = State<TestContext>("Hidden");
        State<TestContext> visible = State<TestContext>("Visible");
        Transition<TestContext, TestTrigger> show =
            Transition(hidden, visible, TestTrigger.Show);
        var blend = new AnimationBlend().Use(opacity, Interpolators.Float);
        AnimationMachine<TestContext, TestTrigger> definition =
            AnimationMachine<TestContext, TestTrigger>(hidden)[
                Animate(hidden, CreateClip("Hidden", opacity, 0f)),
                Animate(visible, CreateClip("Visible", opacity, 1f)),
                Animate(show).Crossfade(Duration.FromSeconds(0.5), blend)
            ];
        var values = new List<float>();
        var player = new AnimationPlayer(clock);
        AnimationMachineInstance<TestContext, TestTrigger> machine =
            definition.CreateInstance(
                new TestContext(),
                player,
                new AnimationTarget().Bind(opacity, values.Add));
        machine.Start();

        machine.Fire(TestTrigger.Show);
        clock.Advance(Duration.FromSeconds(0.25));
        player.Update();

        Assert.Same(visible, machine.CurrentState);
        Assert.Equal(0.5f, values[^1]);
    }

    [Fact]
    public void FacadeUsesImmediateTransitionsByDefault()
    {
        var clock = new ManualClock();
        AnimationChannel<float> opacity = Channel<float>("Opacity");
        State<TestContext> hidden = State<TestContext>("Hidden");
        State<TestContext> visible = State<TestContext>("Visible");
        Transition<TestContext, TestTrigger> show =
            Transition(hidden, visible, TestTrigger.Show);
        AnimationMachine<TestContext, TestTrigger> definition =
            AnimationMachine<TestContext, TestTrigger>(hidden)[
                Animate(hidden, CreateClip("Hidden", opacity, 0f)),
                Animate(visible, CreateClip("Visible", opacity, 1f)),
                Animate(show)
            ];
        var values = new List<float>();
        AnimationMachineInstance<TestContext, TestTrigger> machine =
            definition.CreateInstance(
                new TestContext(),
                new AnimationPlayer(clock),
                new AnimationTarget().Bind(opacity, values.Add));
        machine.Start();

        machine.Fire(TestTrigger.Show);

        Assert.Equal(1f, values[^1]);
    }

    [Fact]
    public void FacadeCrossfadesMultipleChannelsTogether()
    {
        var clock = new ManualClock();
        AnimationChannel<float> opacity = Channel<float>("Opacity");
        AnimationChannel<float> scale = Channel<float>("Scale");
        State<TestContext> hidden = State<TestContext>("Hidden");
        State<TestContext> visible = State<TestContext>("Visible");
        Transition<TestContext, TestTrigger> show =
            Transition(hidden, visible, TestTrigger.Show);
        var blend = new AnimationBlend()
            .Use(opacity, Interpolators.Float)
            .Use(scale, Interpolators.Float);
        AnimationMachine<TestContext, TestTrigger> definition =
            AnimationMachine<TestContext, TestTrigger>(hidden)[
                Animate(hidden, CreateClip("Hidden", opacity, 0f, scale, 0.5f)),
                Animate(visible, CreateClip("Visible", opacity, 1f, scale, 1.5f)),
                Animate(show).Crossfade(Duration.FromSeconds(0.5), blend)
            ];
        var opacityValues = new List<float>();
        var scaleValues = new List<float>();
        var player = new AnimationPlayer(clock);
        AnimationMachineInstance<TestContext, TestTrigger> machine =
            definition.CreateInstance(
                new TestContext(),
                player,
                new AnimationTarget()
                    .Bind(opacity, opacityValues.Add)
                    .Bind(scale, scaleValues.Add));
        machine.Start();

        machine.Fire(TestTrigger.Show);
        clock.Advance(Duration.FromSeconds(0.25));
        player.Update();

        var actual = new
        {
            Opacity = opacityValues[^1],
            Scale = scaleValues[^1],
        };
        var expected = new
        {
            Opacity = 0.5f,
            Scale = 1f,
        };
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void FacadeRejectsAStateWithoutATimeline()
    {
        AnimationChannel<float> opacity = Channel<float>("Opacity");
        State<TestContext> hidden = State<TestContext>("Hidden");
        State<TestContext> visible = State<TestContext>("Visible");
        Transition<TestContext, TestTrigger> show =
            Transition(hidden, visible, TestTrigger.Show);
        AnimationMachine<TestContext, TestTrigger> machine =
            AnimationMachine<TestContext, TestTrigger>(hidden);

        var exception = Assert.Throws<ArgumentException>(() =>
            _ = machine[
                Animate(hidden, CreateClip("Hidden", opacity, 0f)),
                Animate(show)
            ]);

        Assert.Contains("Visible", exception.Message, StringComparison.Ordinal);
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

    private static AnimationClip CreateClip(
        string name,
        AnimationChannel<float> firstChannel,
        float firstValue,
        AnimationChannel<float> secondChannel,
        float secondValue)
    {
        AnimationTrack<float> firstTrack = Track(firstChannel, Interpolators.Float)[
            Keyframe(Duration.Zero, firstValue),
            Keyframe(Duration.FromSeconds(1), firstValue)
        ];
        AnimationTrack<float> secondTrack = Track(secondChannel, Interpolators.Float)[
            Keyframe(Duration.Zero, secondValue),
            Keyframe(Duration.FromSeconds(1), secondValue)
        ];
        return Clip(name)[firstTrack, secondTrack];
    }

    private sealed class TestContext;

    private enum TestTrigger
    {
        Show,
    }
}
