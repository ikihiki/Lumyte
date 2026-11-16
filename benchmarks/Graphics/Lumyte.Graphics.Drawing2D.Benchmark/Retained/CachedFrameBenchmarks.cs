using BenchmarkDotNet.Attributes;

namespace Lumyte.Graphics.Drawing2D.Benchmark.Retained;

/// <summary>Unchanged resident scene preparation, recording and explicit simulated-completion retirement.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
public class CachedFrameBenchmarks
{
    private RetainedSceneFixture _fixture = null!;

    /// <summary>Gets or sets the number of resident placements.</summary>
    [Params(64, 1024)]
    public int NodeCount { get; set; }

    /// <summary>Primes completed resident assets outside timing.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _fixture = new(NodeCount);
        _fixture.PrepareRecordRetire();
    }

    /// <summary>Prepares through a live public borrow and records, submits and retires unchanged cached runs.</summary>
    /// <returns>The consumed synthetic command checksum.</returns>
    [Benchmark]
    public ulong PrepareRecordSimulatedSubmitRetire() => _fixture.PrepareRecordRetire();

    /// <summary>Releases all retired resident assets.</summary>
    [GlobalCleanup]
    public void Cleanup() => _fixture.Dispose();
}
