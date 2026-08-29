using System.Diagnostics;

using Xunit;

using static Lumyte.StateMachine.StateMachineKit;

namespace Lumyte.StateMachine.Tests;

public sealed class StateMachineTests
{
    [Fact]
    public void TypedTriggerTransitionsToTheConfiguredState()
    {
        State<TestContext> idle = State<TestContext>("Idle");
        State<TestContext> active = State<TestContext>("Active");
        StateMachine<TestContext, TestTrigger> definition =
            Machine<TestContext, TestTrigger>(idle)[
                Transition(idle, active, TestTrigger.Activate)
            ];
        StateMachineInstance<TestContext, TestTrigger> machine =
            definition.CreateInstance(new TestContext());

        bool transitioned = machine.Fire(TestTrigger.Activate);

        Assert.True(transitioned);
        Assert.Same(active, machine.CurrentState);
    }

    [Fact]
    public void GuardSelectsTheEligibleTransition()
    {
        State<TestContext> idle = State<TestContext>("Idle");
        State<TestContext> allowed = State<TestContext>("Allowed");
        State<TestContext> denied = State<TestContext>("Denied");
        StateMachine<TestContext, TestTrigger> definition =
            Machine<TestContext, TestTrigger>(idle)[
                Transition(idle, allowed, TestTrigger.Activate)
                    .When(context => context.IsAllowed),
                Transition(idle, denied, TestTrigger.Activate)
                    .When(context => !context.IsAllowed)
            ];
        StateMachineInstance<TestContext, TestTrigger> machine =
            definition.CreateInstance(new TestContext { IsAllowed = true });

        machine.Fire(TestTrigger.Activate);

        Assert.Same(allowed, machine.CurrentState);
    }

    [Fact]
    public void HigherPriorityTransitionWinsBeforeDeclarationOrder()
    {
        State<TestContext> idle = State<TestContext>("Idle");
        State<TestContext> fallback = State<TestContext>("Fallback");
        State<TestContext> preferred = State<TestContext>("Preferred");
        StateMachine<TestContext, TestTrigger> definition =
            Machine<TestContext, TestTrigger>(idle)[
                Transition(idle, fallback, TestTrigger.Activate),
                Transition(idle, preferred, TestTrigger.Activate)
                    .WithPriority(10)
            ];
        StateMachineInstance<TestContext, TestTrigger> machine =
            definition.CreateInstance(new TestContext());

        machine.Fire(TestTrigger.Activate);

        Assert.Same(preferred, machine.CurrentState);
    }

    [Fact]
    public void TransitionRunsExitEffectAndEnterInOrder()
    {
        State<TestContext> idle = State<TestContext>("Idle")
            .OnExit(context => context.Actions.Add("exit"));
        State<TestContext> active = State<TestContext>("Active")
            .OnEnter(context => context.Actions.Add("enter"));
        StateMachine<TestContext, TestTrigger> definition =
            Machine<TestContext, TestTrigger>(idle)[
                Transition(idle, active, TestTrigger.Activate)
                    .Effect(context => context.Actions.Add("effect"))
            ];
        var context = new TestContext();
        StateMachineInstance<TestContext, TestTrigger> machine =
            definition.CreateInstance(context);

        machine.Fire(TestTrigger.Activate);

        Assert.Equal(["exit", "effect", "enter"], context.Actions);
    }

    [Fact]
    public void DefinitionCreatesIndependentInstances()
    {
        State<TestContext> idle = State<TestContext>("Idle");
        State<TestContext> active = State<TestContext>("Active");
        StateMachine<TestContext, TestTrigger> definition =
            Machine<TestContext, TestTrigger>(idle)[
                Transition(idle, active, TestTrigger.Activate)
            ];
        StateMachineInstance<TestContext, TestTrigger> first =
            definition.CreateInstance(new TestContext());
        StateMachineInstance<TestContext, TestTrigger> second =
            definition.CreateInstance(new TestContext());

        first.Fire(TestTrigger.Activate);

        Assert.Same(active, first.CurrentState);
        Assert.Same(idle, second.CurrentState);
    }

    [Fact]
    public void UnknownTriggerLeavesTheStateUnchanged()
    {
        State<TestContext> idle = State<TestContext>("Idle");
        StateMachine<TestContext, TestTrigger> definition =
            Machine<TestContext, TestTrigger>(idle);
        StateMachineInstance<TestContext, TestTrigger> machine =
            definition.CreateInstance(new TestContext());

        bool transitioned = machine.Fire(TestTrigger.Activate);

        Assert.False(transitioned);
        Assert.Same(idle, machine.CurrentState);
    }

    [Fact]
    public void DefinitionFreezesItsStatesAndTransitions()
    {
        State<TestContext> idle = State<TestContext>("Idle");
        State<TestContext> active = State<TestContext>("Active");
        Transition<TestContext, TestTrigger> transition =
            Transition(idle, active, TestTrigger.Activate);
        _ = Machine<TestContext, TestTrigger>(idle)[transition];

        Assert.Throws<InvalidOperationException>(() => idle.OnEnter(_ => { }));
        Assert.Throws<InvalidOperationException>(() => transition.WithPriority(1));
    }

    [Fact]
    public void FireEmitsATaggedTransitionActivity()
    {
        State<TestContext> idle = State<TestContext>("Idle");
        State<TestContext> active = State<TestContext>("Active");
        StateMachine<TestContext, TestTrigger> definition =
            Machine<TestContext, TestTrigger>(idle)[
                Transition(idle, active, TestTrigger.Activate)
                    .WithPriority(10)
            ];
        StateMachineInstance<TestContext, TestTrigger> machine =
            definition.CreateInstance(new TestContext());
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source =>
                source.Name == StateMachineDiagnostics.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);

        machine.Fire(TestTrigger.Activate);

        Activity activity = Assert.Single(stopped);
        var actual = new
        {
            activity.OperationName,
            State = activity.GetTagItem("state_machine.state"),
            Trigger = activity.GetTagItem("state_machine.trigger"),
            Transitioned = activity.GetTagItem("state_machine.transitioned"),
            Target = activity.GetTagItem("state_machine.target"),
            Priority = activity.GetTagItem("state_machine.priority"),
        };
        var expected = new
        {
            OperationName = "StateMachine.Fire",
            State = (object?)"Idle",
            Trigger = (object?)"Activate",
            Transitioned = (object?)true,
            Target = (object?)"Active",
            Priority = (object?)10,
        };
        Assert.Equal(expected, actual);
    }

    private sealed class TestContext
    {
        public bool IsAllowed { get; init; }

        public List<string> Actions { get; } = [];
    }

    private enum TestTrigger
    {
        Activate,
    }
}
