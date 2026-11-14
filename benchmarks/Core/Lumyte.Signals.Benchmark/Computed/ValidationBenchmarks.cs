using BenchmarkDotNet.Attributes;

namespace Lumyte.Signals.Benchmark;

/// <summary>Checks passive shared graphs after related or unrelated mutation generations change.</summary>
[BenchmarkCategory("Core", "Signals", "Computed")]
public class ValidationBenchmarks
{
    private Signal<int> _source = null!;
    private Signal<int> _unrelated = null!;
    private IReadSignal<int> _tail = null!;
    private int _next;
    private int _unrelatedNext;

    /// <summary>Gets or sets the number of shared two-node layers.</summary>
    [Params(2, 4, 8)]
    public int Layers { get; set; }

    /// <summary>Builds and primes a passive shared graph.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _source = Signal.Create(0);
        _unrelated = Signal.Create(0);
        IReadSignal<int> left = _source;
        IReadSignal<int> right = Signal.Create(1);
        for (int index = 0; index < Layers; index++)
        {
            IReadSignal<int> previousLeft = left;
            IReadSignal<int> previousRight = right;
            left = Signal.Computed(() => previousLeft.Value + previousRight.Value);
            right = Signal.Computed(() => previousLeft.Value + previousRight.Value);
        }

        _tail = left;
        _ = _tail.Value;
    }

    /// <summary>Changes an unrelated signal, then validates the unchanged shared graph.</summary>
    /// <returns>The graph result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-SIG-02")]
    public int ReadAfterUnrelatedWrite()
    {
        _unrelated.Value = ++_unrelatedNext;
        return _tail.Value;
    }

    /// <summary>Changes a graph input, then computes its fresh result.</summary>
    /// <returns>The graph result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-SIG-02")]
    public int UpdateAndRead()
    {
        _source.Value = ++_next;
        return _tail.Value;
    }
}
