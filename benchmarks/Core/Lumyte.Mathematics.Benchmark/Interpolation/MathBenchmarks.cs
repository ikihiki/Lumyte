using System.Numerics;
using BenchmarkDotNet.Attributes;

namespace Lumyte.Mathematics.Benchmark;

/// <summary>Distinct interpolation and geometry algorithm paths.</summary>
[BenchmarkCategory("Core", "Mathematics", "Interpolation")]
public class MathBenchmarks
{
    /// <summary>Gets or sets endpoint, interior and extrapolation amounts.</summary>
    [Params(0f, 0.375f, 1f, 1.25f)]
    public float Amount { get; set; }

    /// <summary>Executes Linear through the public product API.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-MATH-01")]
    public float Linear() => Interpolation.Linear(-15f, 42f, Amount);

    /// <summary>Executes Integer through the public product API.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-MATH-01")]
    public long Integer() => IntegerInterpolation.Linear(-1500L, 4200L, Amount);

    /// <summary>Executes EaseIn through the public product API.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-MATH-01")]
    public double EaseIn() => Interpolation.EaseIn(Amount);

    /// <summary>Executes EaseOut through the public product API.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-MATH-01")]
    public double EaseOut() => Interpolation.EaseOut(Amount);

    /// <summary>Executes EaseInOut through the public product API.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-MATH-01")]
    public double EaseInOut() => Interpolation.EaseInOut(Amount);

    /// <summary>Executes Slerp through the public product API.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-MATH-02")]
    public Quaternion Slerp() => QuaternionInterpolation.Slerp(Quaternion.Identity, Quaternion.CreateFromYawPitchRoll(1, 2, 3), Amount);

    /// <summary>Executes DelegateBezier through the public product API.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-MATH-02")]
    public float DelegateBezier() => BezierInterpolation.Cubic(-15f, 4f, -2f, 42f, Amount, Interpolation.Linear);
}
