using BenchmarkDotNet.Attributes;

namespace Lumyte.Graphics.Incremental.Benchmarks;

/// <summary>Includes initial fragment and persistent-tree construction.</summary>
public class ConstructionBenchmarks
{
    /// <summary>Gets or sets logical command count.</summary>
    [Params(1000, 10000)]
    public int Count { get; set; }

    /// <summary>Creates one flat recording and its input state.</summary>
    /// <returns>The new state.</returns>
    [Benchmark(Baseline = true)]
    public object LegacyInitial() => new ComparisonCase(Count, "single", "rectangles", 1, false, false);

    /// <summary>Creates per-element recordings and a persistent root.</summary>
    /// <returns>The new state.</returns>
    [Benchmark]
    public object RetainedInitial() => new ComparisonCase(Count, "single", "rectangles", 1, true, false);
}
