using System.Diagnostics;
using BenchmarkDotNet.Attributes;

namespace Lumyte.StateMachines.Benchmark.Machines;

/// <summary>Isolates exception recovery and listener-enabled diagnostic operations.</summary>
[BenchmarkCategory("Diagnostic")]
public class StateFailureBenchmarks
{
    private readonly bool[] _fail = [true];
    private readonly InvalidOperationException _expectedFailure = new("expected fixture failure");
    private StateMachineInstance<bool[], int> _instance = null!;
    private ActivityListener? _listener;
    private Activity? _diagnosticActivity;
    private ActivityContext _diagnosticParent;
    private string? _diagnosticParentId;
    private int _diagnosticThread;
    private bool _diagnosticSampled;

    /// <summary>Gets or sets the failing callback, or the successful diagnostic branch.</summary>
    [Params("guard", "exit", "effect", "enter", "notification", "diagnostic")]
    public string Phase { get; set; } = "guard";

    /// <summary>Gets the number of expected callback failures actually observed.</summary>
    public int FailureCount { get; private set; }

    /// <summary>Gets the number of transitions that completed all callbacks.</summary>
    public int CompletedCount { get; private set; }

    /// <summary>Gets the number of this fixture's sampled diagnostic activities.</summary>
    public int SampledCount { get; private set; }

    /// <summary>Gets the number of this fixture's stopped activities with matching success tags.</summary>
    public int MatchedActivityCount { get; private set; }

    /// <summary>Prepares an independently recoverable self-transition.</summary>
    [GlobalSetup]
    public void Setup()
    {
        if (Phase is not ("guard" or "exit" or "effect" or "enter" or "notification" or "diagnostic"))
        {
            throw new ArgumentException("Unknown failure phase.", nameof(Phase));
        }

        Cleanup();
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
                    if (_diagnosticThread != Environment.CurrentManagedThreadId
                        || options.Name != "StateMachine.Fire"
                        || options.Parent != _diagnosticParent)
                    {
                        return ActivitySamplingResult.None;
                    }

                    _diagnosticSampled = true;
                    SampledCount = unchecked(SampledCount + 1);
                    return ActivitySamplingResult.AllData;
                },
                ActivityStarted = activity =>
                {
                    if (_diagnosticThread == Environment.CurrentManagedThreadId
                        && _diagnosticSampled
                        && _diagnosticActivity is null
                        && activity.OperationName == "StateMachine.Fire"
                        && activity.ParentId == _diagnosticParentId)
                    {
                        _diagnosticActivity = activity;
                    }
                },
                ActivityStopped = activity =>
                {
                    if (ReferenceEquals(activity, _diagnosticActivity)
                        && activity.OperationName == "StateMachine.Fire"
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
    public bool Recover()
    {
        // Fire and Activity callbacks are synchronous; reuse the existing parent context
        // instead of adding a benchmark-only parent activity or changing library tags.
        bool diagnostic = _listener is not null;
        if (diagnostic)
        {
            Activity? parent = Activity.Current;
            _diagnosticParent = parent?.Context ?? default;
            _diagnosticParentId = parent?.Id;
            _diagnosticActivity = null;
            _diagnosticSampled = false;
            _diagnosticThread = Environment.CurrentManagedThreadId;
        }

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
        finally
        {
            if (diagnostic)
            {
                _diagnosticThread = 0;
                _diagnosticActivity = null;
            }
        }
    }

    /// <summary>Releases the process-wide diagnostic subscription.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _listener?.Dispose();
        _listener = null;
    }
}
