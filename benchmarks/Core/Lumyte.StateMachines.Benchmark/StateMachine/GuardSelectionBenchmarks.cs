using BenchmarkDotNet.Attributes;

namespace Lumyte.StateMachines.Benchmark;

/// <summary>Exercises full candidate scans, short circuit guards and string trigger lookup.</summary>
[BenchmarkCategory("Core", "StateMachines", "Guards")]
public class GuardSelectionBenchmarks
{
    private readonly int[] _context = new int[1];
    private StateMachineInstance<int[], string> _instance = null!;

    /// <summary>Gets or sets the outgoing candidate count.</summary>
    [Params(8, 128)]
    public int Candidates { get; set; }

    /// <summary>Gets or sets the first, last, rejected or unknown input profile.</summary>
    [Params("first", "last", "rejected", "unknown")]
    public string Match { get; set; } = "first";

    /// <summary>Prepares same-trigger competing transitions with two short-circuit guards.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var state = new State<int[]>("source");
        var builder = new StateMachineBuilder<int[], string>(state);
        for (int i = 0; i < Candidates; i++)
        {
            int candidate = i;
            builder.AddTransition(new Transition<int[], string>(state, state, "go").When(c => c[0] == candidate).When(c => c[0] >= 0));
        }

        _context[0] = Match == "first" ? 0 : Match == "last" ? Candidates - 1 : -1;
        _instance = builder.Build(_context);
    }

    /// <summary>Evaluates the configured candidate position without mutating current state.</summary>
    /// <returns>Whether one candidate accepts the input.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-STATE-03")]
    public bool Select() => _instance.CanFire(Match == "unknown" ? "unknown" : "go");
}
