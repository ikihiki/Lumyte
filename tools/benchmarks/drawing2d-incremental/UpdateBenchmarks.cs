using BenchmarkDotNet.Attributes;

namespace Lumyte.Graphics.Incremental.Benchmarks;

/// <summary>CPU publication cost; retained-only output is not yet renderer-ready.</summary>
public class UpdateBenchmarks
{
    private ComparisonCase _case = null!;

    /// <summary>Gets or sets the logical command count.</summary>
    [Params(1000, 10000)]
    public int Count { get; set; }

    /// <summary>Gets or sets changed command locations per frame.</summary>
    [Params("static", "single", "clustered", "scattered", "all")]
    public string Pattern { get; set; } = "single";

    /// <summary>Initializes the existing flat recording path.</summary>
    [GlobalSetup(Target = nameof(LegacyFrame))]
    public void SetupLegacy() => _case = new(Count, Pattern, "rectangles", 1, false, false);

    /// <summary>Initializes immutable per-element fragments without a flat tape.</summary>
    [GlobalSetup(Target = nameof(RetainedCommit))]
    public void SetupRetained() => _case = new(Count, Pattern, "rectangles", 1, true, false);

    /// <summary>Initializes the compatibility path, including both representations.</summary>
    [GlobalSetup(Target = nameof(RetainedWithFlatten))]
    public void SetupFlatten() => _case = new(Count, Pattern, "rectangles", 1, true, true);

    /// <summary>Returns a renderer-ready flat recording; static frames reuse it.</summary>
    /// <returns>The immutable draw list.</returns>
    [Benchmark(Baseline = true)]
    public object LegacyFrame() => _case.LegacyFrame();

    /// <summary>Updates persistent fragments only; excludes renderer integration.</summary>
    /// <returns>The persistent root.</returns>
    [Benchmark]
    public object RetainedCommit() => _case.RetainedFrame();

    /// <summary>Also pays full Replay/End conversion required by the existing renderer.</summary>
    /// <returns>The renderer-ready draw list.</returns>
    [Benchmark]
    public object RetainedWithFlatten() => _case.RetainedAndFlattenFrame();
}
