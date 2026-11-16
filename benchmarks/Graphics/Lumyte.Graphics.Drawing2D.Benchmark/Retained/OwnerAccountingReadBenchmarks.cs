using BenchmarkDotNet.Attributes;

namespace Lumyte.Graphics.Drawing2D.Benchmark.Retained;

/// <summary>One public statistics read after real resident drawing preparation.</summary>
[MemoryDiagnoser]
[MedianColumn]
[BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
public class OwnerAccountingReadBenchmarks
{
    private RetainedSceneFixture _fixture = null!;

    /// <summary>Gets or sets the number of individually transformed retained assets.</summary>
    [Params(1, 26)]
    public int NodeCount { get; set; }

    /// <summary>Creates and retires resident resources outside the read boundary.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _fixture = new(NodeCount);
        _fixture.PrepareRecordRetire();
        if (_fixture.Statistics.CachedAssets != NodeCount || _fixture.Statistics.CachedBytes <= 0)
        {
            throw new InvalidOperationException("The statistics fixture did not retain every expected asset.");
        }
    }

    /// <summary>Reads the live public cached-byte statistic once.</summary>
    /// <returns>The observed byte charge, consumed by BenchmarkDotNet.</returns>
    [Benchmark]
    public long Read() => _fixture.Statistics.CachedBytes;

    /// <summary>Releases completion-retired resources.</summary>
    [GlobalCleanup]
    public void Cleanup() => _fixture.Dispose();
}
