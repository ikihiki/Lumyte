using BenchmarkDotNet.Attributes;

namespace Lumyte.Signals.Benchmarks;

/// <summary>Measures branching, diamond propagation, and passive read set construction.</summary>
public class GraphBenchmarks
{
    private Signal<int> _source = null!;
    private Signal<bool> _choose = null!;
    private SignalEffect _diamondEffect = null!;
    private SignalEffect _branchEffect = null!;
    private Func<int> _build = null!;
    private SignalReadSet _reads = null!;
    private int _next;
    private int _diamondSink;
    private int _branchSink;

    /// <summary>Creates independent diamond, branch, and read set graphs.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _source = Signal.Create(0);
        Computed<int> first = Signal.Computed(() => _source.Value * 2);
        Computed<int> second = Signal.Computed(() => _source.Value + 1);
        Computed<int> sum = Signal.Computed(() => first.Value + second.Value);
        _diamondEffect = Signal.Effect(() => _diamondSink = sum.Value);
        _choose = Signal.Create(false);
        var left = Signal.Create(1);
        var right = Signal.Create(2);
        Computed<int> selected = Signal.Computed(() => _choose.Value ? left.Value : right.Value);
        _branchEffect = Signal.Effect(() => _branchSink = selected.Value);
        var inputs = new Signal<int>[8];
        for (int index = 0; index < inputs.Length; index++)
        {
            inputs[index] = Signal.Create(index);
        }

        _build = () =>
        {
            int total = 0;
            foreach (Signal<int> input in inputs)
            {
                total += input.Value;
            }

            return total;
        };
        _ = Signal.CaptureReads(_build, out _reads);
    }

    /// <summary>Propagates one mutation through a diamond to one effect.</summary>
    /// <returns>The final sum.</returns>
    [Benchmark]
    public int DiamondUpdate()
    {
        _source.Value = ++_next;
        return _diamondSink;
    }

    /// <summary>Switches a conditional dependency and updates its effect.</summary>
    /// <returns>The selected value.</returns>
    [Benchmark]
    public int SwitchBranch()
    {
        _choose.Value = !_choose.Peek();
        return _branchSink;
    }

    /// <summary>Captures eight primitive reads with a preallocated build callback.</summary>
    /// <returns>The captured read set.</returns>
    [Benchmark]
    public SignalReadSet CaptureEightReads()
    {
        _ = Signal.CaptureReads(_build, out SignalReadSet reads);
        return reads;
    }

    /// <summary>Validates a previously captured, unchanged eight-input read set.</summary>
    /// <returns>Whether the read set is current.</returns>
    [Benchmark]
    public bool CheckReadSet() => _reads.IsCurrent;

    /// <summary>Releases both subscriptions.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _branchEffect.Dispose();
        _diamondEffect.Dispose();
    }
}
