using BenchmarkDotNet.Attributes;

namespace Lumyte.Graphics.Incremental.Benchmarks;

/// <summary>Shows the tradeoff between fragment metadata and over-recording.</summary>
public class ChunkBenchmarks
{
    private ComparisonCase _case = null!;

    /// <summary>Gets or sets commands re-recorded together.</summary>
    [Params(1, 16, 64, 256)]
    public int ChunkSize { get; set; }

    /// <summary>Gets or sets sparse versus clustered updates.</summary>
    [Params("single", "scattered")]
    public string Pattern { get; set; } = "single";

    /// <summary>Creates the 10,000-command persistent scene.</summary>
    [GlobalSetup]
    public void Setup() => _case = new(10000, Pattern, "rectangles", ChunkSize, true, false);

    /// <summary>Re-records touched chunks and commits structural sharing.</summary>
    /// <returns>The persistent root; no flatten or GPU work is included.</returns>
    [Benchmark]
    public object RetainedCommit() => _case.RetainedFrame();
}
