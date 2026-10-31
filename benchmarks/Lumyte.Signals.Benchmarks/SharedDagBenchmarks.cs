using BenchmarkDotNet.Attributes;

namespace Lumyte.Signals.Benchmarks;

/// <summary>Compares cached reads through shared dependency paths with and without an active observer.</summary>
public class SharedDagBenchmarks
{
    private IReadSignal<int> _observed = null!;
    private IReadSignal<int> _unobserved = null!;
    private SignalEffect _effect = null!;

    /// <summary>Gets or sets the number of two-node layers above two primitive inputs.</summary>
    [Params(2, 4, 8)]
    public int Layers { get; set; }

    /// <summary>Checks all graph sizes produce the intended cached values.</summary>
    public static void Verify()
    {
        foreach (int layers in new[] { 2, 4, 8 })
        {
            var benchmarks = new SharedDagBenchmarks { Layers = layers };
            benchmarks.Setup();
            try
            {
                int expected = 1 << layers;
                if (benchmarks.ReadObservedCached() != expected || benchmarks.ReadUnobservedCached() != expected
                    || benchmarks.ReadObservedCached() != expected || benchmarks.ReadUnobservedCached() != expected)
                {
                    throw new InvalidOperationException("Shared dependency paths must preserve the expected cached sum.");
                }
            }
            finally
            {
                benchmarks.Cleanup();
            }
        }
    }

    /// <summary>Builds separate graphs and initializes their cached values outside measurement.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _observed = Build(Layers);
        _unobserved = Build(Layers);
        _effect = Signal.Effect(() => _ = _observed.Value);
        _ = _unobserved.Value;
    }

    /// <summary>Reads an unchanged graph whose subscriptions propagate invalidation eagerly.</summary>
    /// <returns>The cached value.</returns>
    [Benchmark]
    public int ReadObservedCached() => _observed.Value;

    /// <summary>Reads an unchanged graph that must validate its shared dependencies lazily.</summary>
    /// <returns>The cached value.</returns>
    [Benchmark]
    public int ReadUnobservedCached() => _unobserved.Value;

    /// <summary>Releases the active graph's subscription.</summary>
    [GlobalCleanup]
    public void Cleanup() => _effect.Dispose();

    private static IReadSignal<int> Build(int layers)
    {
        IReadSignal<int> left = Signal.Create(1);
        IReadSignal<int> right = Signal.Create(1);
        for (int index = 0; index < layers; index++)
        {
            IReadSignal<int> previousLeft = left;
            IReadSignal<int> previousRight = right;
            left = Signal.Computed(() => previousLeft.Value + previousRight.Value);
            right = Signal.Computed(() => previousLeft.Value + previousRight.Value);
        }

        return left;
    }
}
