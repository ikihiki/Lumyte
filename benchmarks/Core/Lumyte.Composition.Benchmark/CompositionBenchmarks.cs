using BenchmarkDotNet.Attributes;

namespace Lumyte.Composition.Benchmark;

/// <summary>Measures snapshots, constrained adapters, generated nodes, and recipes.</summary>
[MemoryDiagnoser]
public class CompositionBenchmarks
{
    private readonly CompositionSlot<Compose.Definitions.Item, int> _slot = new((target, children) => target.Children = children);
    private readonly Compose.Definitions.Item _target = new();
    private int[] _children = [];
    private CompositionSlotAssignment<Compose.Definitions.Item> _assignment;
    private Compose.Definitions.FunctionHost _recipe = null!;

    /// <summary>Gets or sets the finite content count.</summary>
    [Params(0, 1, 8, 256)]
    public int Count { get; set; } = 8;

    /// <summary>Prepares content and the reusable snapshot outside measurements.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _children = Enumerable.Range(1, Count).ToArray();
        _assignment = _slot[_children];
        _recipe = (Compose.Definitions.FunctionHost)Compose.Value(value: 17);
    }

    /// <summary>Captures an immutable slot assignment without applying it.</summary>
    /// <returns>The actual operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-COMP-01")]
    public CompositionSlotAssignment<Compose.Definitions.Item> Capture() => _slot[_children];

    /// <summary>Applies a retained assignment, replacing rather than retaining old arrays.</summary>
    /// <returns>The actual operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-COMP-02")]
    public int[] Apply()
    {
        _assignment.Apply(_target);
        return _target.Children;
    }

    /// <summary>Resolves a mutable explicit-interface adapter and attaches its preserved state.</summary>
    /// <returns>The actual operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-COMP-03")]
    public int Adapter()
    {
        var argument = new CountingArgument(17);
        if (ComposeArgument<int>.IsSpecified(ref argument))
        {
            int value = ComposeArgument<int>.GetInitialValue(ref argument);
            ComposeArgument<int>.AttachTo(ref argument, _target, "Value", value, static (target, initial) => target.Value = initial);
        }

        return argument.Calls;
    }

    /// <summary>Builds through the actual generated factory, actions, and content indexer.</summary>
    /// <returns>The actual operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-COMP-04")]
    public Compose.Definitions.Item Factory() => Compose.Item(value: 17, with: [Compose.Item.Set(19)])[_children];

    /// <summary>Constructs a passive function wrapper without evaluating its body.</summary>
    /// <returns>The actual operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-COMP-05")]
    public Compose.Definitions.Node FunctionWrapper() => Compose.Value(value: 17);

    /// <summary>Reconstructs a fresh wrapper from its generated retained recipe.</summary>
    /// <returns>The actual operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-COMP-05")]
    public object Rebuild() => _recipe.Factory!();

    private struct CountingArgument(int value) : IComposeArgument<int>
    {
        bool IComposeArgument<int>.IsSpecified => true;

        internal int Calls { get; private set; }

        int IComposeArgument<int>.GetInitialValue()
        {
            Calls++;
            return value;
        }

        void IComposeArgument<int>.AttachTo<TTarget>(TTarget target, object memberKey, int initialValue, Action<TTarget, int> setter)
        {
            Calls++;
            setter(target, initialValue);
        }
    }
}
