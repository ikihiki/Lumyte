using BenchmarkDotNet.Attributes;

namespace Lumyte.Graphics.Drawing2D.Benchmark.Retained;

/// <summary>Scene edits and placement publication only; no renderer work is included.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
public class ScenePublicationBenchmarks
{
    private RetainedSceneFixture _fixture = null!;

    /// <summary>Gets or sets the base scene size.</summary>
    [Params(64, 1024)]
    public int NodeCount { get; set; }

    /// <summary>Gets or sets a non-no-op alternating public scene edit.</summary>
    [ParamsAllValues]
    public SceneMutation Mutation { get; set; }

    /// <summary>Builds and publishes the scene outside timing.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _fixture = new(NodeCount);
        using DrawingSceneReadLease2D source = _fixture.Scene.Borrow();
    }

    /// <summary>Applies one bounded edit and publishes the current placement plan.</summary>
    /// <returns>The actual changed scene revision.</returns>
    [Benchmark]
    public ulong EditAndPublish() => _fixture.EditAndPublish(Mutation);

    /// <summary>Releases scene and managed resources.</summary>
    [GlobalCleanup]
    public void Cleanup() => _fixture.Dispose();
}
