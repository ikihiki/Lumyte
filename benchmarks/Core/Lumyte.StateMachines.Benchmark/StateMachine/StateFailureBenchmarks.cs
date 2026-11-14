using System.Diagnostics;
using BenchmarkDotNet.Attributes;

namespace Lumyte.StateMachines.Benchmark;

/// <summary>Isolates exception recovery and listener-enabled diagnostic operations.</summary>
[BenchmarkCategory("Core", "StateMachines", "Diagnostics")]
public class StateFailureBenchmarks
{
    private readonly bool[] _fail = [true];
    private readonly InvalidOperationException _expectedFailure = new("expected fixture failure");
    private StateMachineInstance<bool[], int> _instance = null!;
    private ActivityListener? _listener;

    /// <summary>Gets or sets the failing callback, or the successful diagnostic branch.</summary>
    [Params("guard", "exit", "effect", "enter", "notification", "diagnostic")]
    public string Phase { get; set; } = "guard";

    /// <summary>Gets the number of expected callback failures actually observed.</summary>
    public int FailureCount { get; private set; }

    /// <summary>Gets the number of transitions that completed all callbacks.</summary>
    public int CompletedCount { get; private set; }

    /// <summary>Gets the number of sampled diagnostic activities.</summary>
    public int SampledCount { get; private set; }

    /// <summary>Gets the number of stopped activities with matching success tags.</summary>
    public int MatchedActivityCount { get; private set; }

    /// <summary>Prepares an independently recoverable self-transition.</summary>
    [GlobalSetup]
    public void Setup()
    {
        if (Phase is not ("guard" or "exit" or "effect" or "enter" or "notification" or "diagnostic"))
        {
            throw new ArgumentException("Unknown failure phase.", nameof(Phase));
        }

        FailureCount = 0;
        CompletedCount = 0;
        SampledCount = 0;
        MatchedActivityCount = 0;
        var state = new State<bool[]>("self");
        var transition = new Transition<bool[], int>(state, state, 0);
        void Callback(bool[] context)
        {
            if (context[0])
            {
                throw _expectedFailure;
            }
        }

        if (Phase == "guard")
        {
            transition.When(c =>
            {
                Callback(c);
                return true;
            });
        }

        if (Phase == "exit")
        {
            state.OnExit(Callback);
        }

        if (Phase == "effect")
        {
            transition.Effect(Callback);
        }

        if (Phase == "enter")
        {
            state.OnEnter(Callback);
        }

        _fail[0] = false;
        var builder = new StateMachineBuilder<bool[], int>(state);
        builder.AddTransition(transition);
        _instance = builder.Build(_fail);
        if (Phase == "notification")
        {
            _instance.Transitioned += _ => Callback(_fail);
        }

        _instance.Transitioned += _ => CompletedCount = unchecked(CompletedCount + 1);

        if (Phase == "diagnostic")
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == StateMachineDiagnostics.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> options) =>
                {
                    SampledCount = unchecked(SampledCount + 1);
                    return ActivitySamplingResult.AllData;
                },
                ActivityStopped = activity =>
                {
                    if (activity.OperationName == "StateMachine.Fire"
                        && Equals(activity.GetTagItem("state_machine.transitioned"), true)
                        && Equals(activity.GetTagItem("state_machine.target"), "self")
                        && Equals(activity.GetTagItem("state_machine.trigger"), "0"))
                    {
                        MatchedActivityCount = unchecked(MatchedActivityCount + 1);
                    }
                },
            };
            ActivitySource.AddActivityListener(_listener);
        }
    }

    /// <summary>Runs one isolated failure followed by a successful recovery, or one traced success.</summary>
    /// <returns>The observed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-STATE-06", "diagnostic-only")]
    public bool Recover()
    {
        _fail[0] = Phase != "diagnostic";
        try
        {
            bool accepted = _instance.Fire(0);
            return Phase == "diagnostic" && accepted;
        }
        catch (InvalidOperationException exception) when (ReferenceEquals(exception, _expectedFailure))
        {
            FailureCount = unchecked(FailureCount + 1);
            _fail[0] = false;
            return _instance.Fire(0);
        }
    }

    /// <summary>Releases the process-wide diagnostic subscription.</summary>
    [GlobalCleanup]
    public void Cleanup() => _listener?.Dispose();
}
