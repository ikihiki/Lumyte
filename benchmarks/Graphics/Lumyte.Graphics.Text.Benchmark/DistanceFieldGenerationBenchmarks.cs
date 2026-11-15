using BenchmarkDotNet.Attributes;

namespace Lumyte.Graphics.Text.Benchmark;

/// <summary>Generates one fixed outline without unrelated snapshot-count parameter duplication.</summary>
[BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
public class DistanceFieldGenerationBenchmarks
{
    private TextSnapshotBenchmarks _input = null!;

    /// <summary>Builds the shared immutable vector input outside measurement.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _input = new TextSnapshotBenchmarks { Outlines = 1 };
        _input.Setup();
    }

    /// <summary>Runs the production multichannel signed-distance generator.</summary>
    /// <returns>The complete generated pixel storage.</returns>
    [Benchmark]
    public DistanceFieldImage2D GenerateMsdf() => _input.GenerateMsdf();

    /// <summary>Runs the production four-channel distance generator.</summary>
    /// <returns>The complete generated pixel storage.</returns>
    [Benchmark]
    public DistanceFieldImage2D GenerateMtsdf() => _input.GenerateMtsdf();

    /// <summary>Releases untimed input support resources.</summary>
    [GlobalCleanup]
    public void Cleanup() => _input.Cleanup();
}
