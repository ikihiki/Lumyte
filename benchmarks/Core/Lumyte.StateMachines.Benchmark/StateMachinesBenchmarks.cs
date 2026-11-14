using BenchmarkDotNet.Attributes;

namespace Lumyte.StateMachines.Benchmark;

/// <summary>Exercises immutable construction, candidate selection and successful bounded transitions.</summary>
[MemoryDiagnoser]
public class StateMachinesBenchmarks
{
    private readonly int[] _context = new int[2];
    private StateMachine<int[], int> _definition = null!;
    private StateMachineInstance<int[], int> _instance = null!;
    private StateMachineInstance<int[], int> _self = null!;
    private int[] _triggers = [];

    /// <summary>Gets or sets the state count; each state has four outgoing transitions.</summary>
    [Params(1, 8, 128)]
    public int States { get; set; }

    /// <summary>Gets or sets a value indicating whether selection uses mixed priorities.</summary>
    [Params(false, true)]
    public bool MixedPriority { get; set; }

    /// <summary>Gets or sets the number of synchronous transition subscribers.</summary>
    [Params(0, 1, 8)]
    public int Subscribers { get; set; }

    /// <summary>Prepares reusable definitions and warms trigger capture without retaining history.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _definition = CreateBuilder(States, MixedPriority).BuildDefinition();
        _instance = new StateMachineInstance<int[], int>(_definition, _context);
        _self = CreateBuilder(1, MixedPriority).Build(new int[2]);
        for (int i = 0; i < Subscribers; i++)
        {
            _instance.Transitioned += _ => _context[1] = unchecked(_context[1] + 8);
        }

        _triggers = [3, 2, 1, 0, 3, 2, 1, 0];
        _instance.CanFireAny(_triggers);
    }

    /// <summary>Registers fresh mutable states and transitions and freezes the definition.</summary>
    /// <returns>The observed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-STATE-01")]
    public StateMachine<int[], int> RegisterAndFreeze() => CreateBuilder(States, MixedPriority).BuildDefinition();

    /// <summary>Creates an independent executor from an already frozen shared definition.</summary>
    /// <returns>The observed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-STATE-02")]
    public StateMachineInstance<int[], int> CreateInstance() => new(_definition, new int[2]);

    /// <summary>Builds through the generated Composition consumer factory and content initializer.</summary>
    /// <returns>The observed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-STATE-02")]
    public StateMachine<int[], int> ComposeDefinition()
    {
        var a = new State<int[]>("a");
        var b = new State<int[]>("b");
        return ComposeStateMachines.Machine<int[], int>(a)[new Transition<int[], int>(a, b, 0), new Transition<int[], int>(b, a, 0)].BuildDefinition();
    }

    /// <summary>Alternates external context values to exercise accepting and rejecting guard paths.</summary>
    /// <returns>The observed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-STATE-03")]
    public bool CanFire()
    {
        _context[0] ^= 1;
        return _instance.CanFire(0, _context);
    }

    /// <summary>Captures duplicate inputs and selects by transition priority rather than input order.</summary>
    /// <returns>The observed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-STATE-04")]
    public bool CanFireAny() => _instance.CanFireAny(_triggers);

    /// <summary>Captures inputs and takes one valid transition on every invocation.</summary>
    /// <returns>The observed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-STATE-04")]
    public bool FireAny() => _instance.FireAny(_triggers);

    /// <summary>Takes a transition around the closed ring, including exit, effect and entry.</summary>
    /// <returns>The observed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-STATE-05")]
    public bool Fire() => _instance.Fire(3);

    /// <summary>Executes exit and entry even when source and destination are identical.</summary>
    /// <returns>The observed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-STATE-05")]
    public bool SelfTransition() => _self.Fire(3);

    internal static StateMachineBuilder<int[], int> CreateBuilder(int count, bool priorities)
    {
        State<int[]>[] states = Enumerable.Range(0, count).Select(i => new State<int[]>(i.ToString()).OnEnter(c => c[1] = unchecked(c[1] + 1)).OnExit(c => c[1] = unchecked(c[1] + 2))).ToArray();
        var builder = new StateMachineBuilder<int[], int>(states[0]);
        for (int i = 0; i < count; i++)
        {
            for (int trigger = 0; trigger < 4; trigger++)
            {
                int key = trigger;
                builder.AddTransition(new Transition<int[], int>(states[i], states[(i + 1) % count], trigger).WithPriority(priorities ? trigger : 0).When(c => key != 0 || c[0] == 0).When(c => c.Length == 2).Effect(c => c[1] = unchecked(c[1] + 4)));
            }
        }

        return builder;
    }
}
