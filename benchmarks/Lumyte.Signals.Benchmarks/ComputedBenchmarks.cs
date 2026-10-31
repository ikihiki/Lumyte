using BenchmarkDotNet.Attributes;

namespace Lumyte.Signals.Benchmarks;

/// <summary>Separates active cached reads from passive dependency validation and recomputation.</summary>
public class ComputedBenchmarks
{
    private Signal<int> _observedSource = null!;
    private Signal<int> _unobservedSource = null!;
    private IReadSignal<int> _observed = null!;
    private IReadSignal<int> _unobserved = null!;
    private SignalEffect _effect = null!;
    private int _next;
    private int _sink;

    /// <summary>Gets or sets the number of computed nodes.</summary>
    [Params(1, 8, 32)]
    public int Depth { get; set; }

    /// <summary>Builds identical observed and unobserved chains and primes both caches.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _observedSource = Signal.Create(0);
        _unobservedSource = Signal.Create(0);
        _observed = Chain(_observedSource, Depth);
        _unobserved = Chain(_unobservedSource, Depth);
        _effect = Signal.Effect(() => _sink = _observed.Value);
        _ = _unobserved.Value;
    }

    /// <summary>Reads the cached value of an actively observed chain.</summary>
    /// <returns>The chain result.</returns>
    [Benchmark]
    public int ReadObservedCached() => _observed.Value;

    /// <summary>Reads the cached value of a passive chain, including dependency validation.</summary>
    /// <returns>The chain result.</returns>
    [Benchmark]
    public int ReadUnobservedCached() => _unobserved.Value;

    /// <summary>Updates an active chain and delivers its final effect.</summary>
    /// <returns>The observed result.</returns>
    [Benchmark]
    public int UpdateObserved()
    {
        _observedSource.Value = ++_next;
        return _sink;
    }

    /// <summary>Updates a passive chain and explicitly reads its final value.</summary>
    /// <returns>The recomputed result.</returns>
    [Benchmark]
    public int UpdateAndReadUnobserved()
    {
        _unobservedSource.Value = ++_next;
        return _unobserved.Value;
    }

    /// <summary>Removes the active subscription.</summary>
    [GlobalCleanup]
    public void Cleanup() => _effect.Dispose();

    private static IReadSignal<int> Chain(IReadSignal<int> source, int depth)
    {
        IReadSignal<int> result = source;
        for (int index = 0; index < depth; index++)
        {
            IReadSignal<int> upstream = result;
            result = Signal.Computed(() => upstream.Value + 1);
        }

        return result;
    }
}
