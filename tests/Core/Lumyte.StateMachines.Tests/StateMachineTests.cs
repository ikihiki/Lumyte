using System.Diagnostics;
using Xunit;
using static Lumyte.StateMachines.ComposeStateMachines;

namespace Lumyte.StateMachines.Tests;

/// <summary>Represents state machine tests.</summary>
public sealed class StateMachineTests
{
    private enum Trigger
    {
        /// <summary>Specifies go.</summary>
        Go,

        /// <summary>Specifies other.</summary>
        Other,
    }

    /// <summary>Performs build creates ready independent instances and runs initial entry once.</summary>
    [Fact]
    public void BuildCreatesReadyIndependentInstancesAndRunsInitialEntryOnce()
    {
        State<Context> initial = new State<Context>("Initial").OnEnter(context => context.Count++);
        var builder = new StateMachineBuilder<Context, Trigger>(initial);
        var firstContext = new Context();
        StateMachineInstance<Context, Trigger> first = builder.Build(firstContext);
        StateMachineInstance<Context, Trigger> second = builder.Build(new Context());
        Assert.Same(firstContext, first.Context);
        Assert.Same(initial, first.CurrentState);
        Assert.Equal(1, first.Context.Count);
        Assert.Equal(1, second.Context.Count);
        Assert.NotSame(first, second);
        Assert.False(first.Fire(Trigger.Go));
        Assert.Equal(1, first.Context.Count);
    }

    /// <summary>Performs definition compilation does not enter and lists cannot be mutated.</summary>
    [Fact]
    public void DefinitionCompilationDoesNotEnterAndListsCannotBeMutated()
    {
        int entered = 0;
        State<Context> initial = new State<Context>("Same").OnEnter(_ => entered++);
        var target = new State<Context>("Same");
        var unused = new State<Context>("Unused");
        var transition = new Transition<Context, Trigger>(initial, target, Trigger.Go);
        var builder = new StateMachineBuilder<Context, Trigger>(initial);
        builder.AddState(unused);
        builder.AddTransition(transition);
        StateMachine<Context, Trigger> definition = builder.BuildDefinition();
        Assert.Equal(0, entered);
        Assert.Equal(new[] { initial, target, unused }, definition.States);
        Assert.Throws<NotSupportedException>(() => ((IList<State<Context>>)definition.States)[0] = target);
        Assert.Throws<NotSupportedException>(() => ((IList<Transition<Context, Trigger>>)definition.Transitions).Clear());
        var first = new StateMachineInstance<Context, Trigger>(definition, new Context());
        var second = new StateMachineInstance<Context, Trigger>(definition, new Context());
        Assert.Equal(2, entered);
        Assert.True(first.Fire(Trigger.Go));
        Assert.Same(target, first.CurrentState);
        Assert.Same(initial, second.CurrentState);
    }

    /// <summary>Performs priority and stable registration order short circuit lower candidates.</summary>
    [Fact]
    public void PriorityAndStableRegistrationOrderShortCircuitLowerCandidates()
    {
        var start = new State<Context>("Start");
        var first = new State<Context>("First");
        var second = new State<Context>("Second");
        var builder = new StateMachineBuilder<Context, Trigger>(start);
        builder.AddTransition(new Transition<Context, Trigger>(start, second, Trigger.Go).When(_ => throw new InvalidOperationException("Skipped")));
        builder.AddTransition(new Transition<Context, Trigger>(start, first, Trigger.Go).WithPriority(10));
        builder.AddTransition(new Transition<Context, Trigger>(start, second, Trigger.Go).WithPriority(10));
        StateMachineInstance<Context, Trigger> machine = builder.Build(new Context());
        Assert.True(machine.Fire(Trigger.Go));
        Assert.Same(first, machine.CurrentState);
    }

    /// <summary>Performs guards use and and short circuit while context can change.</summary>
    [Fact]
    public void GuardsUseAndAndShortCircuitWhileContextCanChange()
    {
        var start = new State<Context>("Start");
        var target = new State<Context>("Target");
        int laterGuardCalls = 0;
        var builder = new StateMachineBuilder<Context, Trigger>(start);
        builder.AddTransition(new Transition<Context, Trigger>(start, target, Trigger.Go).When(context => context.Enabled).When(_ =>
        {
            laterGuardCalls++;
            return true;
        }));
        var context = new Context();
        StateMachineInstance<Context, Trigger> machine = builder.Build(context);
        Assert.False(machine.Fire(Trigger.Go));
        Assert.Equal(0, laterGuardCalls);
        context.Enabled = true;
        Assert.True(machine.Fire(Trigger.Go));
        Assert.Equal(1, laterGuardCalls);
    }

    /// <summary>Performs exit effects entry and notification run in order.</summary>
    [Fact]
    public void ExitEffectsEntryAndNotificationRunInOrder()
    {
        State<Context> start = new State<Context>("Start").OnExit(context => context.Log.Add("exit1")).OnExit(context => context.Log.Add("exit2"));
        State<Context> target = new State<Context>("Target").OnEnter(context => context.Log.Add("enter1")).OnEnter(context => context.Log.Add("enter2"));
        var builder = new StateMachineBuilder<Context, Trigger>(start);
        builder.AddTransition(new Transition<Context, Trigger>(start, target, Trigger.Go).Effect(context => context.Log.Add("effect1")).Effect(context => context.Log.Add("effect2")));
        StateMachineInstance<Context, Trigger> machine = builder.Build(new Context());
        machine.Transitioned += transition =>
        {
            Assert.Same(target, machine.CurrentState);
            Assert.Same(target, transition.To);
            machine.Context.Log.Add("notification");
        };
        Assert.True(machine.CanFire(Trigger.Go));
        Assert.Empty(machine.Context.Log);
        Assert.Same(start, machine.CurrentState);
        Assert.True(machine.Fire(Trigger.Go));
        Assert.Equal(new[] { "exit1", "exit2", "effect1", "effect2", "enter1", "enter2", "notification" }, machine.Context.Log);
    }

    /// <summary>Performs self transitions exit and reenter without flags.</summary>
    [Fact]
    public void SelfTransitionsExitAndReenterWithoutFlags()
    {
        State<Context> state = new State<Context>("Self").OnEnter(context => context.Count++).OnExit(context => context.Count += 10);
        var builder = new StateMachineBuilder<Context, Trigger>(state);
        builder.AddTransition(new Transition<Context, Trigger>(state, state, Trigger.Go).Effect(context => context.Count += 100));
        StateMachineInstance<Context, Trigger> machine = builder.Build(new Context());
        Assert.True(machine.Fire(Trigger.Go));
        Assert.Same(state, machine.CurrentState);
        Assert.Equal(112, machine.Context.Count);
    }

    /// <summary>Performs string and value triggers use default equality and unknown input is false.</summary>
    [Fact]
    public void StringAndValueTriggersUseDefaultEqualityAndUnknownInputIsFalse()
    {
        var start = new State<Context>("Start");
        var target = new State<Context>("Target");
        var strings = new StateMachineBuilder<Context, string>(start);
        strings.AddTransition(new Transition<Context, string>(start, target, "go"));
        StateMachineInstance<Context, string> stringMachine = strings.Build(new Context());
        Assert.False(stringMachine.Fire("unknown"));
        Assert.False(stringMachine.Fire(null!));
        Assert.True(stringMachine.Fire(new string(new[] { 'g', 'o' })));
        var values = new StateMachineBuilder<Context, ValueTrigger>(start);
        values.AddTransition(new Transition<Context, ValueTrigger>(start, target, new ValueTrigger(42)));
        Assert.True(values.Build(new Context()).Fire(new ValueTrigger(42)));
        Assert.NotSame(StateMachineTrigger.Create(), StateMachineTrigger.Create());
    }

    /// <summary>Performs explicit context does not replace stored context.</summary>
    [Fact]
    public void ExplicitContextDoesNotReplaceStoredContext()
    {
        var state = new State<Context>("State");
        var builder = new StateMachineBuilder<Context, Trigger>(state);
        builder.AddTransition(new Transition<Context, Trigger>(state, state, Trigger.Go).When(context => context.Enabled).Effect(context => context.Count++));
        var stored = new Context();
        var supplied = new Context
        {
            Enabled = true,
        };
        StateMachineInstance<Context, Trigger> machine = builder.Build(stored);
        Assert.True(machine.CanFire(Trigger.Go, supplied));
        Assert.True(machine.Fire(Trigger.Go, supplied));
        Assert.Same(stored, machine.Context);
        Assert.Equal(0, stored.Count);
        Assert.Equal(1, supplied.Count);
        Assert.False(machine.Fire(Trigger.Go));
    }

    /// <summary>Performs trigger sets are captured deduplicated and use transition priority.</summary>
    [Fact]
    public void TriggerSetsAreCapturedDeduplicatedAndUseTransitionPriority()
    {
        var state = new State<Context>("State");
        var selected = new State<Context>("Selected");
        var fallback = new State<Context>("Fallback");
        var builder = new StateMachineBuilder<Context, Trigger>(state);
        int guards = 0;
        builder.AddTransition(new Transition<Context, Trigger>(state, fallback, Trigger.Go));
        builder.AddTransition(new Transition<Context, Trigger>(state, selected, Trigger.Other).When(_ =>
        {
            guards++;
            return true;
        }).WithPriority(10));
        StateMachineInstance<Context, Trigger> machine = builder.Build(new Context());
        Assert.False(machine.FireAny([], machine.Context));
        Assert.True(machine.CanFireAny([Trigger.Go, Trigger.Other, Trigger.Other], machine.Context));
        Assert.Same(state, machine.CurrentState);
        Assert.Equal(1, guards);
        Assert.True(machine.FireAny([Trigger.Go, Trigger.Other, Trigger.Other], machine.Context));
        Assert.Same(selected, machine.CurrentState);
        Assert.Equal(2, guards);
    }

    /// <summary>Performs composition and builder snapshots freeze configuration.</summary>
    [Fact]
    public void CompositionAndBuilderSnapshotsFreezeConfiguration()
    {
        var initial = new State<Context>("Initial");
        var target = new State<Context>("Target");
        var transition = new Transition<Context, Trigger>(initial, target, Trigger.Go);
        var input = new List<Transition<Context, Trigger>>
        {
            transition,
        };
        ComposeStateMachines.Definitions.Machine<Context, Trigger> node = Machine<Context, Trigger>(initial);
        node.Transitions = input;
        StateMachineInstance<Context, Trigger> first = node.Build(new Context());
        input.Clear();
        StateMachineInstance<Context, Trigger> second = node.Build(new Context());
        Assert.True(first.Fire(Trigger.Go));
        Assert.False(second.Fire(Trigger.Go));
        Assert.Throws<InvalidOperationException>(() => initial.OnEnter(_ =>
        {
        }));
        Assert.Throws<InvalidOperationException>(() => target.OnExit(_ =>
        {
        }));
        Assert.Throws<InvalidOperationException>(() => transition.When(_ => true));
        Assert.Throws<InvalidOperationException>(() => transition.Effect(_ =>
        {
        }));
        Assert.Throws<InvalidOperationException>(() => transition.WithPriority(1));
    }

    /// <summary>Performs callback failure preserves documented partial state.</summary>
    /// <param name="stage">The stage.</param>
    /// <param name="changed">The changed.</param>
    [Theory]
    [InlineData("guard", false)]
    [InlineData("exit", false)]
    [InlineData("effect", false)]
    [InlineData("entry", true)]
    [InlineData("notification", true)]
    public void CallbackFailurePreservesDocumentedPartialState(string stage, bool changed)
    {
        var start = new State<Context>("Start");
        var target = new State<Context>("Target");
        var transition = new Transition<Context, Trigger>(start, target, Trigger.Go);
        Action<Context> fail = _ => throw new ApplicationException(stage);
        if (stage == "guard")
        {
            transition.When(_ => throw new ApplicationException(stage));
        }

        if (stage == "exit")
        {
            start.OnExit(fail);
        }

        if (stage == "effect")
        {
            transition.Effect(fail);
        }

        if (stage == "entry")
        {
            target.OnEnter(fail);
        }

        var builder = new StateMachineBuilder<Context, Trigger>(start);
        builder.AddTransition(transition);
        StateMachineInstance<Context, Trigger> machine = builder.Build(new Context());
        if (stage == "notification")
        {
            machine.Transitioned += _ => throw new ApplicationException(stage);
        }

        Assert.Throws<ApplicationException>(() => machine.Fire(Trigger.Go));
        Assert.Same(changed ? target : start, machine.CurrentState);
        Assert.False(machine.Fire(Trigger.Other));
    }

    /// <summary>Performs reentrant evaluation is rejected and guard failure does not lock instance.</summary>
    [Fact]
    public void ReentrantEvaluationIsRejectedAndGuardFailureDoesNotLockInstance()
    {
        var state = new State<Context>("State");
        StateMachineInstance<Context, Trigger>? machine = null;
        Transition<Context, Trigger> transition = new Transition<Context, Trigger>(state, state, Trigger.Go).When(context =>
        {
            if (!context.Enabled)
            {
                machine!.CanFire(Trigger.Go);
            }

            return true;
        });
        var builder = new StateMachineBuilder<Context, Trigger>(state);
        builder.AddTransition(transition);
        machine = builder.Build(new Context());
        Assert.Throws<InvalidOperationException>(() => machine.Fire(Trigger.Go));
        machine.Context.Enabled = true;
        Assert.True(machine.Fire(Trigger.Go));
    }

    /// <summary>Performs invalid context and configuration fail before freezing or entering.</summary>
    [Fact]
    public void InvalidContextAndConfigurationFailBeforeFreezingOrEntering()
    {
        Assert.Throws<ArgumentException>(() => new State<Context>(" "));
        var initial = new State<Context>("Initial");
        var builder = new StateMachineBuilder<Context, Trigger>(initial);
        Assert.Throws<ArgumentNullException>(() => builder.Build(null!));
        initial.OnEnter(context => context.Count++);
        ComposeStateMachines.Definitions.Machine<Context, Trigger> node = Machine<Context, Trigger>(initial);
        node.Transitions = [null!];
        Assert.Throws<ArgumentException>(() => node.BuildDefinition());
        initial.OnExit(_ =>
        {
        });
        Assert.Throws<ArgumentNullException>(() => builder.AddTransition(null!));
        Assert.Equal(1, builder.Build(new Context()).Context.Count);
    }

    /// <summary>Checks failed initial entry and separate compilation do not repeat lifecycle actions implicitly.</summary>
    [Fact]
    public void FailedInitialEntryReturnsNoInstanceAndDefinitionRemainsReusable()
    {
        State<Context> initial = new State<Context>("Initial").OnEnter(context =>
        {
            context.Count++;
            if (!context.Enabled)
            {
                throw new ApplicationException("Initial entry");
            }
        });
        var builder = new StateMachineBuilder<Context, Trigger>(initial);
        var failed = new Context();
        Assert.Throws<ApplicationException>(() => builder.Build(failed));
        Assert.Equal(1, failed.Count);
        Assert.Throws<InvalidOperationException>(() => initial.OnExit(_ => { }));
        StateMachine<Context, Trigger> definition = builder.BuildDefinition();
        Assert.Equal(1, failed.Count);
        var successful = new Context { Enabled = true };
        var machine = new StateMachineInstance<Context, Trigger>(definition, successful);
        Assert.Equal(1, machine.Context.Count);
    }

    /// <summary>Performs diagnostics record success unknown trigger and callback errors.</summary>
    [Fact]
    public void DiagnosticsRecordSuccessUnknownTriggerAndCallbackErrors()
    {
        var captured = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == StateMachineDiagnostics.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = captured.Add,
        };
        ActivitySource.AddActivityListener(listener);
        var state = new State<Context>("State");
        var builder = new StateMachineBuilder<Context, Trigger>(state);
        builder.AddTransition(new Transition<Context, Trigger>(state, state, Trigger.Go).WithPriority(12));
        builder.AddTransition(new Transition<Context, Trigger>(state, state, Trigger.Other).Effect(_ => throw new ApplicationException("Effect")));
        StateMachineInstance<Context, Trigger> machine = builder.Build(new Context());
        Assert.True(machine.Fire(Trigger.Go));
        Assert.Equal("StateMachine.Fire", captured[0].OperationName);
        Assert.Equal("State", captured[0].GetTagItem("state_machine.state"));
        Assert.Equal("Go", captured[0].GetTagItem("state_machine.trigger"));
        Assert.Equal("State", captured[0].GetTagItem("state_machine.target"));
        Assert.Equal(12, captured[0].GetTagItem("state_machine.priority"));
        Assert.Equal(true, captured[0].GetTagItem("state_machine.transitioned"));
        Assert.False(machine.CanFire((Trigger)99));
        Assert.Single(captured);
        Assert.False(machine.Fire((Trigger)99));
        Assert.Equal(false, captured[1].GetTagItem("state_machine.transitioned"));
        Assert.Throws<ApplicationException>(() => machine.Fire(Trigger.Other));
        Assert.Equal(ActivityStatusCode.Error, captured[2].Status);
        Assert.Equal(typeof(ApplicationException).FullName, captured[2].GetTagItem("error.type"));
    }

    /// <summary>Performs trigger snapshot failure does not run actions or lock instance.</summary>
    [Fact]
    public void TriggerSnapshotFailureDoesNotRunActionsOrLockInstance()
    {
        var state = new State<Context>("State");
        var builder = new StateMachineBuilder<Context, Trigger>(state);
        builder.AddTransition(new Transition<Context, Trigger>(state, state, Trigger.Go).Effect(context => context.Count++));
        StateMachineInstance<Context, Trigger> machine = builder.Build(new Context());
        Assert.Throws<ApplicationException>(() => machine.FireAny(new BrokenTriggers(), machine.Context));
        Assert.Equal(0, machine.Context.Count);
        Assert.True(machine.Fire(Trigger.Go));
    }

    /// <summary>Performs warmed enum transitions and trigger sets allocate no managed memory.</summary>
    [Fact]
    public void WarmedEnumTransitionsAndTriggerSetsAllocateNoManagedMemory()
    {
        var state = new State<Context>("State");
        var builder = new StateMachineBuilder<Context, Trigger>(state);
        builder.AddTransition(new Transition<Context, Trigger>(state, state, Trigger.Go).Effect(context => context.Count++));
        StateMachineInstance<Context, Trigger> machine = builder.Build(new Context());
        Trigger[] triggers = [Trigger.Go, Trigger.Go];
        for (int index = 0; index < 1000; index++)
        {
            machine.Fire(Trigger.Go);
            machine.FireAny(triggers, machine.Context);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1000; index++)
        {
            machine.Fire(Trigger.Go);
            machine.FireAny(triggers, machine.Context);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private readonly record struct ValueTrigger(int Value);

    private sealed class Context
    {
        /// <summary>Gets or sets a value indicating whether enabled.</summary>
        public bool Enabled { get; set; }

        /// <summary>Gets or sets the count.</summary>
        public int Count { get; set; }

        /// <summary>Gets the log.</summary>
        public List<string> Log { get; } = [];
    }

    private sealed class BrokenTriggers : IReadOnlyList<Trigger>
    {
        /// <summary>Gets the count.</summary>
        public int Count => 2;

        /// <summary>Gets the test trigger or throws for the invalid input.</summary>
        /// <param name="index">The input index.</param>
        /// <returns>The test trigger.</returns>
        public Trigger this[int index] => index == 0 ? Trigger.Go : throw new ApplicationException("Input");

        /// <summary>Performs get enumerator.</summary>
        /// <returns>The computed result.</returns>
        public IEnumerator<Trigger> GetEnumerator() => throw new NotSupportedException();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
