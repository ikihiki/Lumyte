using Lumyte.Core.Time;
using Lumyte.StateMachines;
using Xunit;
using static Lumyte.Animation.ComposeAnimation;

namespace Lumyte.Animation.Tests;

/// <summary>Represents animation state machine tests.</summary>
public sealed class AnimationStateMachineTests
{
    private enum Motion
    {
        /// <summary>Specifies idle.</summary>
        Idle,

        /// <summary>Specifies action.</summary>
        Action,

        /// <summary>Specifies moving.</summary>
        Moving,
    }

    /// <summary>Checks both build entry points return independent running instances with copied timelines.</summary>
    /// <param name="composition">Whether to use the Composition build entry point.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildReturnsRunningIndependentInstances(bool composition)
    {
        var clock = new ObservedClock();
        var source = new CountingSource();
        var channel = AnimationChannel<float>.Create();
        ComposeAnimation.Definitions.Timeline timeline = Timeline()[Track<float>(channel, source)];
        var builder = new AnimationStateMachineBuilder<Motion, Input>();
        builder.AddState(Motion.Idle, timeline);
        ComposeAnimation.Definitions.StateMachine<Motion, Input> definition = StateMachine<Motion, Input>(Motion.Idle)[State<Motion, Input>(Motion.Idle, timeline)];
        AnimationStateMachine<Motion, Input> first = composition
            ? definition.Build(clock, new Input(Moving: false))
            : builder.Build(clock, Motion.Idle, new Input(Moving: false));
        Assert.Equal(AnimationStateMachineStatus.Running, first.Status);
        Assert.Equal(Motion.Idle, first.CurrentState);
        Assert.Equal(Duration.Zero, first.Position);
        Assert.Equal(1, clock.Reads);
        Assert.Equal(0, source.Samples);
        Assert.Throws<InvalidOperationException>(() => first.Start(new Input(Moving: false)));
        timeline.Children = [Track<float>(channel, Scalar(9, 9))];
        AnimationStateMachine<Motion, Input> second = composition
            ? definition.Build(clock, new Input(Moving: false))
            : builder.Build(clock, Motion.Idle, new Input(Moving: false));
        clock.Time += Duration.FromSeconds(0.5);
        var output = new AnimationOutput();
        first.Update(new Input(Moving: false), output, [], []);
        Assert.True(output.TryGet(channel, out float original));
        Assert.Equal(0.5f, original);
        output.Clear();
        second.Update(new Input(Moving: false), output, [], []);
        Assert.True(output.TryGet(channel, out float changed));
        Assert.Equal(9, changed);
        first.Stop();
        Assert.Equal(AnimationStateMachineStatus.Running, second.Status);
    }

    /// <summary>Checks invalid build arguments fail before compilation, clock reads or value sampling.</summary>
    [Fact]
    public void BuildValidatesInputsBeforeCompilationAndStart()
    {
        var clock = new ObservedClock();
        var source = new CountingSource();
        var channel = AnimationChannel<float>.Create();
        ComposeAnimation.Definitions.Timeline timeline = Timeline()[Track<float>(channel, source)];
        var builder = new AnimationStateMachineBuilder<Motion, string>();
        builder.AddState(Motion.Idle, timeline);
        ComposeAnimation.Definitions.StateMachine<Motion, string> definition = StateMachine<Motion, string>(Motion.Idle)[State<Motion, string>(Motion.Idle, timeline)];
        Assert.Throws<ArgumentNullException>(() => builder.Build(null!, Motion.Idle, "input"));
        Assert.Throws<ArgumentNullException>(() => builder.Build(clock, Motion.Idle, null!));
        Assert.Throws<ArgumentNullException>(() => definition.Build(null!, "input"));
        Assert.Throws<ArgumentNullException>(() => definition.Build(clock, null!));
        Assert.Equal(0, source.DurationReads);
        Assert.Throws<ArgumentException>(() => builder.Build(clock, Motion.Action, "input"));
        Assert.Equal(0, clock.Reads);
        Assert.Equal(0, source.Samples);
        Assert.Equal(AnimationStateMachineStatus.Running, builder.Build(clock, Motion.Idle, "input").Status);
    }

    /// <summary>Checks absolute marker times survive state changes, pause and speed changes without external clock reads.</summary>
    [Fact]
    public void EventTimesRemainComparableAcrossStatesAndPause()
    {
        var clock = new ObservedClock
        {
            Time = TimePoint.Zero + Duration.FromSeconds(10),
        };
        var channel = AnimationChannel<float>.Create();
        var trigger = StateMachineTrigger.Create();
        var first = new AnimationTimelineBuilder();
        first.Add(Duration.Zero, Scalar(0, 1), channel);
        first.AddEvent(Duration.FromSeconds(0.4), "BeforePause");
        var second = new AnimationTimelineBuilder();
        second.Add(Duration.Zero, Scalar(1, 2), channel);
        second.AddEvent(Duration.Zero, "Entered");
        second.AddEvent(Duration.FromSeconds(0.4), "AfterTransition");
        var builder = new AnimationStateMachineBuilder<Motion, Input>();
        builder.AddState(Motion.Idle, first.Build());
        builder.AddState(Motion.Action, second.Build());
        builder.AddTransition(Motion.Idle, Motion.Action, trigger: trigger);
        AnimationStateMachine<Motion, Input> machine = builder.Build(clock, Motion.Idle, new Input(Moving: false));
        clock.Time += Duration.FromSeconds(0.5);
        machine.Pause();
        clock.Time += Duration.FromSeconds(3.5);
        var output = new AnimationOutput();
        var events = new List<AnimationStateEvent<Motion>>();
        var transitions = new List<AnimationStateTransition<Motion>>();
        machine.Update(new Input(Moving: false), output, events, transitions);
        Assert.Empty(events);
        machine.SetTrigger(trigger);
        machine.Resume();
        int reads = clock.Reads;
        Assert.True(machine.Update(new Input(Moving: false), output, events, transitions));
        Assert.Equal(reads + 1, clock.Reads);
        AnimationStateEvent<Motion> beforePause = Assert.Single(events);
        Assert.Equal(TimePoint.Zero + Duration.FromSeconds(10.4), beforePause.OccurredAt);
        Assert.Equal(Duration.FromSeconds(0.4), beforePause.Occurrence.UpdateOffset);
        AnimationStateTransition<Motion> transition = Assert.Single(transitions);
        Assert.Equal(TimePoint.Zero + Duration.FromSeconds(14), transition.ObservedAt);
        Assert.True(beforePause.OccurredAt < transition.ObservedAt);
        machine.Speed = 2;
        events.Clear();
        clock.Time += Duration.FromSeconds(0.2);
        reads = clock.Reads;
        machine.Update(new Input(Moving: false), output, events, transitions);
        Assert.Equal(reads + 1, clock.Reads);
        Assert.Equal(2, events.Count);
        Assert.Equal(transition.ObservedAt, events[0].OccurredAt);
        Assert.Equal(TimePoint.Zero + Duration.FromSeconds(14.2), events[1].OccurredAt);
        Assert.Equal(Duration.FromSeconds(0.2), events[1].Occurrence.UpdateOffset);
    }

    /// <summary>Performs composition builds automatically without reading clock or sampling.</summary>
    [Fact]
    public void CompositionBuildsAutomaticallyWithoutReadingClockOrSampling()
    {
        var clock = new ObservedClock();
        var source = new CountingSource();
        var channel = AnimationChannel<float>.Create();
        ComposeAnimation.Definitions.Timeline timeline = Timeline()[Track<float>(channel, source)];
        ComposeAnimation.Definitions.StateMachine<Motion, Input> definition = StateMachine<Motion, Input>(Motion.Idle)[State<Motion, Input>(Motion.Idle, timeline)];
        var machine = new AnimationStateMachine<Motion, Input>(clock, definition);
        int durationReads = source.DurationReads;
        Assert.True(durationReads > 0);
        Assert.Equal(0, clock.Reads);
        Assert.Equal(0, source.Samples);
        machine.Start(new Input(Moving: false));
        Assert.Equal(1, clock.Reads);
        Assert.Equal(0, source.Samples);
        clock.Time += Duration.FromSeconds(0.5);
        var output = new AnimationOutput();
        Assert.False(machine.Update(new Input(Moving: false), output, [], []));
        Assert.Equal(2, clock.Reads);
        Assert.Equal(durationReads, source.DurationReads);
        Assert.True(output.TryGet(channel, out float value));
        Assert.Equal(0.5f, value);
    }

    /// <summary>Performs snapshots are independent and mutable definitions are not cached.</summary>
    [Fact]
    public void SnapshotsAreIndependentAndMutableDefinitionsAreNotCached()
    {
        var clock = new ManualClock();
        var channel = AnimationChannel<float>.Create();
        ComposeAnimation.Definitions.Timeline timeline = Constant(channel, 1);
        ComposeAnimation.Definitions.StateMachine<Motion, Input> definition = StateMachine<Motion, Input>(Motion.Idle)[State<Motion, Input>(Motion.Idle, timeline)];
        var first = new AnimationStateMachine<Motion, Input>(clock, definition);
        timeline.Children = [Track<float>(channel, Scalar(3, 3))];
        var second = new AnimationStateMachine<Motion, Input>(clock, definition);
        first.Start(new Input(Moving: false));
        second.Start(new Input(Moving: false));
        var output = new AnimationOutput();
        first.Update(new Input(Moving: false), output, [], []);
        Assert.True(output.TryGet(channel, out float one));
        Assert.Equal(1, one);
        output.Clear();
        second.Update(new Input(Moving: false), output, [], []);
        Assert.True(output.TryGet(channel, out float three));
        Assert.Equal(3, three);
    }

    /// <summary>Performs trigger and completion select once and discard overshoot.</summary>
    [Fact]
    public void TriggerAndCompletionSelectOnceAndDiscardOvershoot()
    {
        var clock = new ObservedClock();
        var channel = AnimationChannel<float>.Create();
        var action = StateMachineTrigger.Create();
        ComposeAnimation.Definitions.Timeline forward = Timeline()[Track<float>(channel, Scalar(0, 1, 0.2))];
        ComposeAnimation.Definitions.StateMachine<Motion, Input> definition = StateMachine<Motion, Input>(Motion.Idle)[State<Motion, Input>(Motion.Idle, Constant(channel, 0), wrap: AnimationWrapMode.Loop)[Transition<Motion, Input>(Motion.Moving, condition: new Func<Input, AnimationStateInfo, bool>((input, _) => input.Moving)), Transition<Motion, Input>(Motion.Action, trigger: action, priority: 100)], State<Motion, Input>(Motion.Action, Timeline()[Sequence()[Repeat(2)[Sequence()[forward, Reverse()[forward]]], Marker("Finished")]])[Transition<Motion, Input>(Motion.Moving, onCompleted: true)], State<Motion, Input>(Motion.Moving, Constant(channel, 5), wrap: AnimationWrapMode.Loop)];
        var machine = new AnimationStateMachine<Motion, Input>(clock, definition);
        machine.Start(new Input(Moving: false));
        machine.SetTrigger(action);
        machine.SetTrigger(action);
        var output = new AnimationOutput();
        var events = new List<AnimationStateEvent<Motion>>();
        var transitions = new List<AnimationStateTransition<Motion>>();
        int before = clock.Reads;
        Assert.True(machine.Update(new Input(Moving: true), output, events, transitions));
        Assert.Equal(before + 1, clock.Reads);
        Assert.Equal(Motion.Action, machine.CurrentState);
        Assert.Single(transitions);
        Assert.Equal(TimePoint.Zero, transitions[0].ObservedAt);
        clock.Time += Duration.FromSeconds(1);
        output.Clear();
        events.Clear();
        transitions.Clear();
        before = clock.Reads;
        Assert.True(machine.Update(new Input(Moving: true), output, events, transitions));
        Assert.Equal(before + 1, clock.Reads);
        Assert.Equal(Motion.Moving, machine.CurrentState);
        Assert.Equal(Duration.Zero, machine.Position);
        AnimationStateEvent<Motion> marker = Assert.Single(events);
        Assert.Equal(Motion.Action, marker.State);
        Assert.Equal("Finished", marker.Occurrence.Event.Name);
        Assert.Equal(Duration.FromSeconds(0.8), marker.Occurrence.UpdateOffset);
        Assert.Equal(TimePoint.Zero + Duration.FromSeconds(1), Assert.Single(transitions).ObservedAt);
        Assert.True(output.TryGet(channel, out float value));
        Assert.Equal(5, value);
    }

    /// <summary>Performs marker zero is delivered on first advance and never duplicated.</summary>
    [Fact]
    public void MarkerZeroIsDeliveredOnFirstAdvanceAndNeverDuplicated()
    {
        var clock = new ManualClock();
        var channel = AnimationChannel<float>.Create();
        var builder = new AnimationStateMachineBuilder<Motion, Input>();
        builder.AddState(Motion.Idle, Timeline()[Marker("Zero"), Track<float>(channel, Scalar(0, 1))]);
        var machine = new AnimationStateMachine<Motion, Input>(clock, builder, Motion.Idle);
        machine.Start(new Input(Moving: false));
        var events = new List<AnimationStateEvent<Motion>>();
        machine.Update(new Input(Moving: false), new AnimationOutput(), events, []);
        Assert.Empty(events);
        clock.Advance(Duration.FromSeconds(0.1));
        machine.Update(new Input(Moving: false), new AnimationOutput(), events, []);
        Assert.Equal(Duration.Zero, Assert.Single(events).Occurrence.UpdateOffset);
        events.Clear();
        machine.Update(new Input(Moving: false), new AnimationOutput(), events, []);
        Assert.Empty(events);
    }

    /// <summary>Performs pause keeps undelivered markers and pending triggers.</summary>
    [Fact]
    public void PauseKeepsUndeliveredMarkersAndPendingTriggers()
    {
        var clock = new ManualClock();
        var channel = AnimationChannel<float>.Create();
        var trigger = StateMachineTrigger.Create();
        var timeline = new AnimationTimelineBuilder();
        timeline.Add(Duration.Zero, Scalar(0, 1), channel);
        timeline.AddEvent(Duration.FromSeconds(0.4), "BeforePause", null);
        var builder = new AnimationStateMachineBuilder<Motion, Input>();
        builder.AddState(Motion.Idle, timeline.Build());
        builder.AddState(Motion.Action, Constant(channel, 7));
        builder.AddTransition(Motion.Idle, Motion.Action, trigger: trigger);
        var machine = new AnimationStateMachine<Motion, Input>(clock, builder, Motion.Idle);
        machine.Start(new Input(Moving: false));
        clock.Advance(Duration.FromSeconds(0.5));
        machine.Pause();
        machine.SetTrigger(trigger);
        clock.Advance(Duration.FromSeconds(3.5));
        var output = new AnimationOutput();
        var events = new List<AnimationStateEvent<Motion>>();
        Assert.False(machine.Update(new Input(Moving: false), output, events, []));
        Assert.Empty(events);
        Assert.Equal(Motion.Idle, machine.CurrentState);
        Assert.True(output.TryGet(channel, out float paused));
        Assert.Equal(0.5f, paused);
        machine.Resume();
        output.Clear();
        Assert.True(machine.Update(new Input(Moving: false), output, events, []));
        Assert.Equal(Motion.Action, machine.CurrentState);
        Assert.Equal(Duration.FromSeconds(0.4), Assert.Single(events).Occurrence.UpdateOffset);
    }

    /// <summary>Performs speed zero and changes preserve event offsets and carry to next state.</summary>
    [Fact]
    public void SpeedZeroAndChangesPreserveEventOffsetsAndCarryToNextState()
    {
        var clock = new ManualClock();
        var channel = AnimationChannel<float>.Create();
        var timeline = new AnimationTimelineBuilder();
        timeline.Add(Duration.Zero, Scalar(0, 1), channel);
        timeline.AddEvent(Duration.FromSeconds(0.4), "Point", null);
        var builder = new AnimationStateMachineBuilder<Motion, Input>();
        builder.AddState(Motion.Idle, timeline.Build());
        builder.AddState(Motion.Action, Timeline()[Track<float>(channel, Scalar(0, 1))]);
        builder.AddTransition(Motion.Idle, Motion.Action, onCompleted: true);
        var machine = new AnimationStateMachine<Motion, Input>(clock, builder, Motion.Idle);
        machine.Start(new Input(Moving: false));
        clock.Advance(Duration.FromSeconds(0.2));
        machine.Speed = 0;
        clock.Advance(Duration.FromSeconds(2));
        var events = new List<AnimationStateEvent<Motion>>();
        machine.Update(new Input(Moving: false), new AnimationOutput(), events, []);
        Assert.Equal(Duration.FromSeconds(0.2), machine.Position);
        Assert.Empty(events);
        machine.Speed = 2;
        clock.Advance(Duration.FromSeconds(0.1));
        machine.Update(new Input(Moving: false), new AnimationOutput(), events, []);
        Assert.Equal(Duration.FromSeconds(0.1), Assert.Single(events).Occurrence.UpdateOffset);
        clock.Advance(Duration.FromSeconds(0.3));
        Assert.True(machine.Update(new Input(Moving: false), new AnimationOutput(), [], []));
        Assert.Equal(Motion.Action, machine.CurrentState);
        clock.Advance(Duration.FromSeconds(0.1));
        machine.Update(new Input(Moving: false), new AnimationOutput(), [], []);
        Assert.Equal(Duration.FromSeconds(0.2), machine.Position);
    }

    /// <summary>Performs zero speed still evaluates conditions and one call does not chain.</summary>
    [Fact]
    public void ZeroSpeedStillEvaluatesConditionsAndOneCallDoesNotChain()
    {
        var clock = new ManualClock();
        var channel = AnimationChannel<float>.Create();
        var builder = new AnimationStateMachineBuilder<Motion, Input>();
        builder.AddState(Motion.Idle, Constant(channel, 0));
        builder.AddState(Motion.Action, Constant(channel, 1));
        builder.AddState(Motion.Moving, Constant(channel, 2));
        builder.AddTransition(Motion.Idle, Motion.Action);
        builder.AddTransition(Motion.Action, Motion.Moving);
        var machine = new AnimationStateMachine<Motion, Input>(clock, builder, Motion.Idle)
        {
            Speed = 0,
        };
        machine.Start(new Input(Moving: false));
        Assert.True(machine.Update(new Input(Moving: false), new AnimationOutput(), [], []));
        Assert.Equal(Motion.Action, machine.CurrentState);
        Assert.Equal(Duration.Zero, machine.Position);
        Assert.True(machine.Update(new Input(Moving: false), new AnimationOutput(), [], []));
        Assert.Equal(Motion.Moving, machine.CurrentState);
    }

    /// <summary>Performs triggers are consumed on miss and can be cleared or reset.</summary>
    [Fact]
    public void TriggersAreConsumedOnMissAndCanBeClearedOrReset()
    {
        var clock = new ManualClock();
        var channel = AnimationChannel<float>.Create();
        var trigger = StateMachineTrigger.Create();
        var builder = new AnimationStateMachineBuilder<Motion, Input>();
        builder.AddState(Motion.Idle, Constant(channel, 0));
        builder.AddState(Motion.Action, Constant(channel, 1));
        builder.AddTransition(Motion.Idle, Motion.Action, condition: (input, _) => input.Moving, trigger: trigger);
        var machine = new AnimationStateMachine<Motion, Input>(clock, builder, Motion.Idle);
        Assert.Throws<InvalidOperationException>(() => machine.SetTrigger(trigger));
        machine.Start(new Input(Moving: false));
        machine.SetTrigger(trigger);
        Assert.False(machine.Update(new Input(Moving: false), new AnimationOutput(), [], []));
        Assert.False(machine.Update(new Input(Moving: true), new AnimationOutput(), [], []));
        machine.SetTrigger(trigger);
        machine.ClearTriggers();
        Assert.False(machine.Update(new Input(Moving: true), new AnimationOutput(), [], []));
        machine.SetTrigger(trigger);
        machine.Stop();
        machine.Start(new Input(Moving: false));
        Assert.False(machine.Update(new Input(Moving: true), new AnimationOutput(), [], []));
        machine.SetTrigger(trigger);
        Assert.True(machine.Update(new Input(Moving: true), new AnimationOutput(), [], []));
    }

    /// <summary>Performs old values are discarded and missing destination channels are not restored.</summary>
    [Fact]
    public void OldValuesAreDiscardedAndMissingDestinationChannelsAreNotRestored()
    {
        var clock = new ManualClock();
        var channel = AnimationChannel<float>.Create();
        var builder = new AnimationStateMachineBuilder<Motion, Input>();
        builder.AddState(Motion.Idle, Constant(channel, 5));
        builder.AddState(Motion.Action, Timeline()[Delay(Duration.FromSeconds(1))]);
        builder.AddTransition(Motion.Idle, Motion.Action, onCompleted: true);
        var machine = new AnimationStateMachine<Motion, Input>(clock, builder, Motion.Idle);
        machine.Start(new Input(Moving: false));
        var output = new AnimationOutput();
        machine.Update(new Input(Moving: false), output, [], []);
        Assert.True(output.TryGet(channel, out float initial));
        Assert.Equal(5, initial);
        output.Clear();
        clock.Advance(Duration.FromSeconds(1));
        Assert.True(machine.Update(new Input(Moving: false), output, [], []));
        Assert.False(output.TryGet(channel, out _));
    }

    /// <summary>Performs self transition restarts playback and output collections are appended.</summary>
    [Fact]
    public void SelfTransitionRestartsPlaybackAndOutputCollectionsAreAppended()
    {
        var clock = new ManualClock();
        var channel = AnimationChannel<float>.Create();
        var trigger = StateMachineTrigger.Create();
        var builder = new AnimationStateMachineBuilder<Motion, Input>();
        builder.AddState(Motion.Idle, Timeline()[Track<float>(channel, Scalar(0, 1))]);
        builder.AddTransition(Motion.Idle, Motion.Idle, trigger: trigger);
        var machine = new AnimationStateMachine<Motion, Input>(clock, builder, Motion.Idle);
        machine.Start(new Input(Moving: false));
        clock.Advance(Duration.FromSeconds(0.5));
        machine.SetTrigger(trigger);
        var output = new AnimationOutput();
        var transitions = new List<AnimationStateTransition<Motion>>
        {
            new(Motion.Action, Motion.Moving, TimePoint.Zero),
        };
        Assert.True(machine.Update(new Input(Moving: false), output, [], transitions));
        Assert.Equal(2, transitions.Count);
        Assert.Equal(Motion.Idle, transitions[1].From);
        Assert.Equal(Motion.Idle, transitions[1].To);
        Assert.Equal(Duration.Zero, machine.Position);
        Assert.True(output.TryGet(channel, out float value));
        Assert.Equal(0, value);
    }

    /// <summary>Performs supplied control keeps callbacks and can share frozen definition.</summary>
    [Fact]
    public void SuppliedControlKeepsCallbacksAndCanShareFrozenDefinition()
    {
        var clock = new ManualClock();
        var channel = AnimationChannel<float>.Create();
        var log = new List<string>();
        var trigger = StateMachineTrigger.Create();
        State<AnimationStateContext<Input>> initial = new State<AnimationStateContext<Input>>("Initial").OnEnter(_ => log.Add("initial"));
        initial.OnExit(_ => log.Add("exit"));
        AnimationStateMachine<Motion, Input>? machine = null;
        State<AnimationStateContext<Input>> target = new State<AnimationStateContext<Input>>("Target").OnEnter(context =>
        {
            Assert.Equal(Motion.Action, machine!.CurrentState);
            Assert.Equal(Duration.FromSeconds(0.5), context.Playback.Position);
            log.Add("enter");
        });
        var core = new StateMachineBuilder<AnimationStateContext<Input>, StateMachineTrigger>(initial);
        core.AddTransition(new Transition<AnimationStateContext<Input>, StateMachineTrigger>(initial, target, trigger).Effect(_ => log.Add("effect")));
        var definition = new AnimationStateMachineDefinition<Motion, Input>(core.BuildDefinition(), new AnimationStateBinding<Motion, Input>[] { new(Motion.Idle, initial, Constant(channel, 0).Build()), new(Motion.Action, target, Constant(channel, 1).Build()), });
        machine = new AnimationStateMachine<Motion, Input>(clock, definition);
        var independent = new AnimationStateMachine<Motion, Input>(clock, definition);
        Assert.Empty(log);
        machine.Start(new Input(Moving: false));
        independent.Start(new Input(Moving: false));
        clock.Advance(Duration.FromSeconds(0.5));
        machine.SetTrigger(trigger);
        Assert.True(machine.Update(new Input(Moving: false), new AnimationOutput(), [], []));
        Assert.Equal(new[] { "initial", "initial", "exit", "effect", "enter" }, log);
        Assert.Equal(Motion.Idle, independent.CurrentState);
        machine.Stop();
        Assert.Equal(5, log.Count);
    }

    /// <summary>Performs completed state keeps evaluating and independent clocks do not advance together.</summary>
    [Fact]
    public void CompletedStateKeepsEvaluatingAndIndependentClocksDoNotAdvanceTogether()
    {
        var gameClock = new ManualClock();
        var uiClock = new ManualClock();
        var channel = AnimationChannel<float>.Create();
        var builder = new AnimationStateMachineBuilder<Motion, Input>();
        builder.AddState(Motion.Idle, Timeline()[Track<float>(channel, Scalar(0, 1))]);
        var game = new AnimationStateMachine<Motion, Input>(gameClock, builder, Motion.Idle);
        var ui = new AnimationStateMachine<Motion, Input>(uiClock, builder, Motion.Idle);
        game.Start(new Input(Moving: false));
        ui.Start(new Input(Moving: false));
        uiClock.Advance(Duration.FromSeconds(2));
        var output = new AnimationOutput();
        Assert.False(ui.Update(new Input(Moving: false), output, [], []));
        Assert.Equal(AnimationStateMachineStatus.Running, ui.Status);
        Assert.Equal(Duration.FromSeconds(1), ui.Position);
        Assert.True(output.TryGet(channel, out float end));
        Assert.Equal(1, end);
        output.Clear();
        game.Update(new Input(Moving: false), output, [], []);
        Assert.Equal(Duration.Zero, game.Position);
        Assert.True(output.TryGet(channel, out float start));
        Assert.Equal(0, start);
    }

    /// <summary>Performs invalid definitions are rejected without reading clock or changing nodes.</summary>
    [Fact]
    public void InvalidDefinitionsAreRejectedWithoutReadingClockOrChangingNodes()
    {
        var clock = new ObservedClock();
        var channel = AnimationChannel<float>.Create();
        ComposeAnimation.Definitions.Timeline timeline = Constant(channel, 1);
        ComposeAnimation.Definitions.State<Motion, Input> state = State<Motion, Input>(Motion.Idle, timeline, wrap: AnimationWrapMode.Loop)[Transition<Motion, Input>(Motion.Action, onCompleted: true)];
        ComposeAnimation.Definitions.StateMachine<Motion, Input> definition = StateMachine<Motion, Input>(Motion.Idle)[state, State<Motion, Input>(Motion.Action, timeline)];
        Assert.Throws<ArgumentException>(() => new AnimationStateMachine<Motion, Input>(clock, definition));
        Assert.Equal(0, clock.Reads);
        Assert.Single(state.Transitions);
        state.Transitions = [Transition<Motion, Input>(Motion.Moving)];
        Assert.Throws<ArgumentException>(() => new AnimationStateMachine<Motion, Input>(clock, definition));
        state.Transitions = [];
        definition.States = [state, state];
        Assert.Throws<ArgumentException>(() => new AnimationStateMachine<Motion, Input>(clock, definition));
        definition.States = [state];
        timeline.Children = [timeline];
        Assert.Throws<ArgumentException>(() => new AnimationStateMachine<Motion, Input>(clock, definition));
        Assert.Equal(0, clock.Reads);
    }

    /// <summary>Performs invalid operations fail before sampling and rewound clock is rejected.</summary>
    [Fact]
    public void InvalidOperationsFailBeforeSamplingAndRewoundClockIsRejected()
    {
        var clock = new ObservedClock();
        var source = new CountingSource();
        var channel = AnimationChannel<float>.Create();
        var builder = new AnimationStateMachineBuilder<Motion, Input>();
        builder.AddState(Motion.Idle, Timeline()[Track<float>(channel, source)]);
        var machine = new AnimationStateMachine<Motion, Input>(clock, builder, Motion.Idle);
        Assert.Throws<InvalidOperationException>(() => machine.CurrentState);
        Assert.Throws<ArgumentOutOfRangeException>(() => machine.Speed = double.NaN);
        machine.Start(new Input(Moving: false));
        Assert.Throws<InvalidOperationException>(() => machine.Start(new Input(Moving: false)));
        Assert.Throws<ArgumentException>(() => machine.SetTrigger(StateMachineTrigger.Create()));
        int reads = clock.Reads;
        Assert.Throws<ArgumentNullException>(() => machine.Update(new Input(Moving: false), null!, [], []));
        Assert.Equal(reads, clock.Reads);
        Assert.Equal(0, source.Samples);
        clock.Time += Duration.FromSeconds(0.5);
        machine.Update(new Input(Moving: false), new AnimationOutput(), [], []);
        clock.Time = TimePoint.Zero;
        Assert.Throws<InvalidOperationException>(() => machine.Update(new Input(Moving: false), new AnimationOutput(), [], []));
        machine.Stop();
        machine.Stop();
        Assert.False(machine.Update(new Input(Moving: false), new AnimationOutput(), [], []));
    }

    /// <summary>Checks typed slot transfer preserves unrelated consumer contributions and reference values.</summary>
    [Fact]
    public void TypedTransferPreservesUnrelatedValuesAndLaterEvaluationWins()
    {
        var clock = new ManualClock();
        var position = AnimationChannel<System.Numerics.Vector3>.Create();
        var label = AnimationChannel<string>.Create();
        var external = AnimationChannel<float>.Create();
        var vector = new Tween<System.Numerics.Vector3>(System.Numerics.Vector3.Zero, System.Numerics.Vector3.One, Duration.FromSeconds(1), AnimationInterpolators.Vector3, AnimationEasing.Linear);
        var text = new Tween<string>("Idle", "Ready", Duration.FromSeconds(1), AnimationInterpolators.Step<string>(), AnimationEasing.Linear);
        ComposeAnimation.Definitions.StateMachine<Motion, Input> node = StateMachine<Motion, Input>(Motion.Idle)[
            State<Motion, Input>(Motion.Idle, Timeline()[Track<System.Numerics.Vector3>(position, vector), Track<string>(label, text)])];
        var machine = new AnimationStateMachine<Motion, Input>(clock, node);
        var other = new AnimationPlayback(clock, Timeline()[Track<float>(external, Scalar(9, 9))].Build());
        machine.Start(new Input(Moving: false));
        other.Play();
        clock.Advance(Duration.FromSeconds(0.5));
        var output = new AnimationOutput();
        other.Update(output, []);
        machine.Update(new Input(Moving: false), output, [], []);
        Assert.True(output.TryGet(external, out float untouched));
        Assert.Equal(9, untouched);
        Assert.True(output.TryGet(position, out System.Numerics.Vector3 value));
        Assert.Equal(new System.Numerics.Vector3(0.5f), value);
        Assert.True(output.TryGet(label, out string? name));
        Assert.Equal("Idle", name);
        var replacement = new AnimationPlayback(clock, Timeline()[Track<System.Numerics.Vector3>(
            position,
            new Tween<System.Numerics.Vector3>(System.Numerics.Vector3.One, System.Numerics.Vector3.One, Duration.FromSeconds(1), AnimationInterpolators.Vector3, AnimationEasing.Linear))].Build());
        replacement.Play();
        replacement.Update(output, []);
        Assert.True(output.TryGet(position, out value));
        Assert.Equal(System.Numerics.Vector3.One, value);
    }

    /// <summary>Checks action failures and recursive adapter operations can be recovered by stop and start.</summary>
    [Fact]
    public void InitialEntryAndRecursiveControlFailuresCanBeRecovered()
    {
        var clock = new ManualClock();
        var channel = AnimationChannel<float>.Create();
        AnimationStateMachine<Motion, Input>? machine = null;
        State<AnimationStateContext<Input>> state = new State<AnimationStateContext<Input>>("Initial").OnEnter(context =>
        {
            if (!context.Input.Moving)
            {
                throw new ApplicationException("Initial entry");
            }
        });
        var trigger = StateMachineTrigger.Create();
        var core = new StateMachineBuilder<AnimationStateContext<Input>, StateMachineTrigger>(state);
        core.AddTransition(new Transition<AnimationStateContext<Input>, StateMachineTrigger>(state, state, trigger)
            .When(_ =>
            {
                machine!.Stop();
                return true;
            }));
        var definition = new AnimationStateMachineDefinition<Motion, Input>(
            core.BuildDefinition(),
            new AnimationStateBinding<Motion, Input>[] { new(Motion.Idle, state, Constant(channel, 0).Build()) });
        machine = new AnimationStateMachine<Motion, Input>(clock, definition);
        Assert.Throws<ApplicationException>(() => machine.Start(new Input(Moving: false)));
        Assert.Equal(AnimationStateMachineStatus.Stopped, machine.Status);
        machine.Start(new Input(Moving: true));
        machine.SetTrigger(trigger);
        Assert.Throws<InvalidOperationException>(() => machine.Update(new Input(Moving: true), new AnimationOutput(), [], []));
        Assert.Equal(Motion.Idle, machine.CurrentState);
        machine.Stop();
        machine.Start(new Input(Moving: true));
        Assert.False(machine.Update(new Input(Moving: true), new AnimationOutput(), [], []));
    }

    /// <summary>Performs warmed numeric updates and repeated transitions allocate no managed memory.</summary>
    [Fact]
    public void WarmedNumericUpdatesAndRepeatedTransitionsAllocateNoManagedMemory()
    {
        var clock = new ManualClock();
        var channel = AnimationChannel<float>.Create();
        var builder = new AnimationStateMachineBuilder<Motion, Input>();
        builder.AddState(Motion.Idle, Constant(channel, 0));
        builder.AddState(Motion.Action, Constant(channel, 1));
        builder.AddTransition(Motion.Idle, Motion.Action);
        builder.AddTransition(Motion.Action, Motion.Idle);
        var machine = new AnimationStateMachine<Motion, Input>(clock, builder, Motion.Idle);
        machine.Start(new Input(Moving: false));
        var output = new AnimationOutput();
        var events = new List<AnimationStateEvent<Motion>>();
        var transitions = new List<AnimationStateTransition<Motion>>();
        for (int index = 0; index < 1000; index++)
        {
            output.Clear();
            transitions.Clear();
            machine.Update(new Input(Moving: false), output, events, transitions);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1000; index++)
        {
            output.Clear();
            transitions.Clear();
            machine.Update(new Input(Moving: false), output, events, transitions);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static Tween<float> Scalar(float from, float to, double seconds = 1) => new(from, to, Duration.FromSeconds(seconds), AnimationInterpolators.Float, AnimationEasing.Linear);

    private static ComposeAnimation.Definitions.Timeline Constant(AnimationChannel<float> channel, float value) => Timeline()[Track<float>(channel, Scalar(value, value))];

    private readonly record struct Input(bool Moving = false);

    private sealed class ObservedClock : IMonotonicClock
    {
        /// <summary>Gets or sets the time.</summary>
        public TimePoint Time { get; set; }

        /// <summary>Gets the reads.</summary>
        public int Reads { get; private set; }

        /// <summary>Gets the now.</summary>
        public TimePoint Now
        {
            get
            {
                Reads++;
                return Time;
            }
        }
    }

    private sealed class CountingSource : IAnimationSource<float>
    {
        /// <summary>Gets the duration reads.</summary>
        public int DurationReads { get; private set; }

        /// <summary>Gets the samples.</summary>
        public int Samples { get; private set; }

        /// <summary>Gets the duration.</summary>
        public Duration Duration
        {
            get
            {
                DurationReads++;
                return Duration.FromSeconds(1);
            }
        }

        /// <summary>Computes a value at a local time within the source duration without side effects.</summary>
        /// <param name="time">The time.</param>
        /// <returns>The computed result.</returns>
        public float Sample(Duration time)
        {
            Samples++;
            return (float)time.TotalSeconds;
        }
    }
}
