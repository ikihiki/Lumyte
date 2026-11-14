using BenchmarkDotNet.Attributes;

namespace Lumyte.Mathematics.Benchmark;

/// <summary>Inverse timing solver uses its legal unit-interval domain.</summary>
public class TimingBenchmarks
{
    private readonly CubicBezierTiming _timing = new(0.25, 0.1, 0.25, 1);

    /// <summary>Gets or sets endpoint and interior amounts.</summary>
    [Params(0.0, 0.375, 1.0)]
    public double Amount { get; set; }

    /// <summary>Solves the inverse cubic timing curve.</summary>
    /// <returns>The transformed amount.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-MATH-03")]
    public double Solve() => _timing.Transform(Amount);

    /// <summary>Executes Hermite through the public product API.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-MATH-02")]
    public float Hermite() => HermiteInterpolation.Interpolate(-15f, 42f, 3f, -2f, 2, (float)Amount);
}
