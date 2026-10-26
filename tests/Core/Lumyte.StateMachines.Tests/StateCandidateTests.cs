using Xunit;

namespace Lumyte.StateMachines.Tests;

/// <summary>Checks state-specific candidate selection and execution cache boundaries.</summary>
public sealed class StateCandidateTests
{
    /// <summary>Checks interleaved states retain candidate priority, registration order and guard short-circuiting.</summary>
    /// <param name="multipleInputs">Whether to select from a captured set of triggers.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InterleavedStatesPreserveCandidateOrder(bool multipleInputs)
    {
        var initial = new State<int>("Same");
        var other = new State<int>("Same");
        var selected = new State<int>("Selected");
        var skipped = new State<int>("Skipped");
        var guards = new List<string>();
        var builder = new StateMachineBuilder<int, int>(initial);
        builder.AddTransition(new Transition<int, int>(other, skipped, 1).WithPriority(100).When(_ => throw new InvalidOperationException("Other state")));
        builder.AddTransition(new Transition<int, int>(initial, skipped, 1).When(_ => throw new InvalidOperationException("Lower priority")));
        builder.AddTransition(new Transition<int, int>(initial, skipped, 1).WithPriority(20).When(_ =>
        {
            guards.Add("rejected");
            return false;
        }).When(_ => throw new InvalidOperationException("Short-circuited guard")));
        builder.AddTransition(new Transition<int, int>(other, skipped, 2).WithPriority(10).When(_ => throw new InvalidOperationException("Other state")));
        builder.AddTransition(new Transition<int, int>(initial, selected, 1).WithPriority(10).When(_ =>
        {
            guards.Add("selected");
            return true;
        }));
        builder.AddTransition(new Transition<int, int>(initial, skipped, 1).WithPriority(10).When(_ => throw new InvalidOperationException("Later registration")));
        StateMachineInstance<int, int> machine = builder.Build(0);

        Assert.True(multipleInputs ? machine.CanFireAny([2, 1, 1], 0) : machine.CanFire(1));
        Assert.Same(initial, machine.CurrentState);
        Assert.Equal(new[] { "rejected", "selected" }, guards);
        guards.Clear();
        Assert.True(multipleInputs ? machine.FireAny([2, 1, 1], 0) : machine.Fire(1));
        Assert.Same(selected, machine.CurrentState);
        Assert.Equal(new[] { "rejected", "selected" }, guards);
        Assert.False(machine.CanFire(1));
        Assert.False(machine.FireAny([1, 2], 0));
    }

    /// <summary>Checks cached candidates follow the documented state after a callback throws.</summary>
    /// <param name="stage">The callback stage that throws once.</param>
    /// <param name="changed">Whether the transition has already changed the current state.</param>
    [Theory]
    [InlineData("exit", false)]
    [InlineData("effect", false)]
    [InlineData("entry", true)]
    [InlineData("notification", true)]
    public void CallbackFailureKeepsCandidatesAlignedWithCurrentState(string stage, bool changed)
    {
        var initial = new State<int>("Initial");
        var target = new State<int>("Target");
        bool fail = true;
        Action<int> failOnce = _ =>
        {
            if (fail)
            {
                fail = false;
                throw new ApplicationException(stage);
            }
        };
        var transition = new Transition<int, int>(initial, target, 1);
        if (stage == "exit")
        {
            initial.OnExit(failOnce);
        }

        if (stage == "effect")
        {
            transition.Effect(failOnce);
        }

        if (stage == "entry")
        {
            target.OnEnter(failOnce);
        }

        var builder = new StateMachineBuilder<int, int>(initial);
        builder.AddTransition(transition);
        builder.AddTransition(new Transition<int, int>(target, initial, 2));
        StateMachineInstance<int, int> machine = builder.Build(0);
        if (stage == "notification")
        {
            machine.Transitioned += _ => failOnce(0);
        }

        Assert.Throws<ApplicationException>(() => machine.Fire(1));
        Assert.Same(changed ? target : initial, machine.CurrentState);
        Assert.Equal(!changed, machine.CanFire(1));
        Assert.Equal(changed, machine.CanFireAny([2], 0));
        Assert.True(machine.FireAny([changed ? 2 : 1], 0));
        Assert.Same(changed ? initial : target, machine.CurrentState);
    }

    /// <summary>Checks state objects shared across definition snapshots do not share mutable candidate caches.</summary>
    [Fact]
    public void SharedStatesKeepDefinitionAndInstanceCandidatesIndependent()
    {
        var initial = new State<int>("Same");
        var target = new State<int>("Same");
        var builder = new StateMachineBuilder<int, int>(initial);
        builder.AddTransition(new Transition<int, int>(initial, target, 1));
        StateMachine<int, int> original = builder.BuildDefinition();
        builder.AddTransition(new Transition<int, int>(target, initial, 2));
        StateMachine<int, int> extended = builder.BuildDefinition();
        var first = new StateMachineInstance<int, int>(original, 0);
        var second = new StateMachineInstance<int, int>(extended, 0);
        var third = new StateMachineInstance<int, int>(extended, 0);

        Assert.True(first.Fire(1));
        Assert.True(second.Fire(1));
        Assert.Same(initial, third.CurrentState);
        Assert.False(first.Fire(2));
        Assert.True(second.FireAny([2], 0));
        Assert.Same(initial, second.CurrentState);
        Assert.Same(target, first.CurrentState);
        Assert.False(third.CanFireAny([2], 0));
        Assert.True(third.FireAny([1], 0));
        Assert.True(third.CanFire(2));
        Assert.Single(original.Transitions);
        Assert.Equal(2, extended.Transitions.Count);
    }
}
