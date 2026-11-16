using BenchmarkDotNet.Attributes;

namespace Lumyte.Graphics.Drawing2D.Benchmark.Retained;

/// <summary>Isolated one-invocation dirty-frame phase probe; edit/publication is excluded, not a throughput job.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Diagnostic")]
public class DirtyFrameBenchmarks
{
    private RetainedSceneFixture _fixture = null!;

    /// <summary>Gets or sets the base placement count.</summary>
    [Params(64, 1024)]
    public int NodeCount { get; set; }

    /// <summary>Gets or sets the single-placement invalidation.</summary>
    [Params(SceneMutation.Content, SceneMutation.Transform, SceneMutation.Clip)]
    public SceneMutation Mutation { get; set; }

    /// <summary>Primes completed resident assets outside timing.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _fixture = new(NodeCount);
        _fixture.PrepareRecordRetire();
    }

    /// <summary>Publishes a real alternating edit outside the single timed invocation; cache bound evicts the previous variant.</summary>
    [IterationSetup]
    public void Edit() => _fixture.EditAndPublish(Mutation);

    /// <summary>Prepares the dirty scene and records, CPU-submits and explicitly retires its frame.</summary>
    /// <returns>The consumed command/upload checksum.</returns>
    [Benchmark]
    public ulong PrepareRecordSimulatedSubmitRetire() => _fixture.PrepareRecordRetire();

    /// <summary>Releases all retired resident assets.</summary>
    [GlobalCleanup]
    public void Cleanup() => _fixture.Dispose();
}
