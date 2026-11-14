using BenchmarkDotNet.Attributes;
using Lumyte.Core.Time;
using Lumyte.StateMachines;

namespace Lumyte.Animation.Benchmark;

/// <summary>Separates machine compilation/start, retained predicates, triggered completion cycles and lifecycle.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Animation", "StateMachine", "Screening", "Comparison", "Diagnostic")]
public class AnimationStateMachineBenchmarks
{
    private readonly AnimationChannel<float> _channel = AnimationChannel<float>.Create();
    private readonly StateMachineTrigger _trigger = StateMachineTrigger.Create();
    private readonly AnimationOutput _output = new();
    private readonly List<AnimationStateEvent<int>> _events = [];
    private readonly List<AnimationStateTransition<int>> _transitions = [];
    private ManualClock _clock = null!;
    private AnimationStateMachineBuilder<int, bool> _builder = null!;
    private AnimationStateMachine<int, bool> _machine = null!;

    /// <summary>Creates timelines and starts a reusable idle machine outside update measurements.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _clock = new ManualClock();
        var idle = new AnimationTimelineBuilder();
        idle.Add(Duration.Zero, new Tween<float>(0, 0, Duration.FromTicks(100), AnimationInterpolators.Float, AnimationEasing.Linear), _channel);
        var action = new AnimationTimelineBuilder();
        action.Add(Duration.Zero, new Tween<float>(0, 10, Duration.FromTicks(100), AnimationInterpolators.Float, AnimationEasing.Linear), _channel);
        action.AddEvent(Duration.FromTicks(50), "Middle");
        action.AddEvent(Duration.FromTicks(100), "Finished");
        _builder = new AnimationStateMachineBuilder<int, bool>();
        _builder.AddState(0, idle.Build(), AnimationWrapMode.Loop);
        _builder.AddState(1, action.Build());
        _builder.AddTransition(0, 1, condition: static (enabled, _) => enabled, trigger: _trigger);
        _builder.AddTransition(1, 0, onCompleted: true);
        _machine = _builder.Build(_clock, 0, true);
    }

    /// <summary>Compiles a prepared builder, constructs an independent machine and starts its initial state.</summary>
    /// <returns>The running independent instance.</returns>
    [Benchmark]
    public AnimationStateMachine<int, bool> ConstructAndStart() => _builder.Build(_clock, 0, true);

    /// <summary>Evaluates retained idle output and a rejected trigger predicate without taking a transition.</summary>
    /// <returns>The sampled value and actual transition count.</returns>
    [Benchmark]
    public double RetainedNoTransition()
    {
        _machine.SetTrigger(_trigger);
        return Update(false);
    }

    /// <summary>Consumes coalesced trigger input, samples action markers, completes and returns to idle.</summary>
    /// <returns>Values plus emitted marker and transition counts across the complete cycle.</returns>
    [Benchmark]
    public double TriggerCompletionCycle()
    {
        _machine.SetTrigger(_trigger);
        _machine.SetTrigger(_trigger);
        double sum = Update(true);
        _clock.Advance(Duration.FromTicks(50));
        sum += Update(true);
        _clock.Advance(Duration.FromTicks(50));
        sum += Update(true);
        return sum;
    }

    /// <summary>Exercises pause, speed, pending-input clearing, resume, stop and restart on one retained machine.</summary>
    /// <returns>The paused sample followed by a restarted idle sample.</returns>
    [Benchmark]
    public double LifecycleCycle()
    {
        _machine.Pause();
        _machine.Speed = 2;
        _machine.SetTrigger(_trigger);
        _machine.ClearTriggers();
        double sum = Update(true);
        _machine.Resume();
        _machine.Stop();
        _machine.Speed = 1;
        _machine.Start(true);
        return sum + Update(true);
    }

    private double Update(bool enabled)
    {
        _output.Clear();
        _events.Clear();
        _transitions.Clear();
        bool transitioned = _machine.Update(enabled, _output, _events, _transitions);
        _output.TryGet(_channel, out float value);
        return value + _events.Count + _transitions.Count + (transitioned ? 1 : 0);
    }
}
