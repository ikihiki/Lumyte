using BenchmarkDotNet.Attributes;
using Lumyte.Mathematics.Colors;

namespace Lumyte.Mathematics.Benchmark.ColorOperations;

/// <summary>Independent hue-sector profiles.</summary>
[BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
public class ColorModelBenchmarks
{
    /// <summary>Gets or sets hue sectors and a wrapped value.</summary>
    [Params(0f, 60f, 120f, 180f, 240f, 300f, 420f)]
    public float Hue { get; set; }

    /// <summary>Executes the Hsl color operation.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    public SRgba<float> Hsl() => SRgba<float>.FromHslSrgbDegrees(Hue, 0.8f, 0.4f, 1f);

    /// <summary>Executes the Hsv color operation.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    public SRgba<float> Hsv() => SRgba<float>.FromHsvSrgbDegrees(Hue, 0.8f, 0.4f, 1f);
}
