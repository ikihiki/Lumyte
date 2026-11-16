using BenchmarkDotNet.Attributes;

namespace Lumyte.Graphics.Drawing2D.Benchmark.Retained;

/// <summary>Includes accounting maintenance in real CPU preparation, recording and simulated retirement.</summary>
[MemoryDiagnoser]
[MedianColumn]
[BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
public class OwnerAccountingFrameBenchmarks
{
    private RetainedSceneFixture _fixture = null!;

    /// <summary>Gets or sets the number of statistics observations per complete CPU frame.</summary>
    [Params(0, 1, 2)]
    public int StatisticsReads { get; set; }

    /// <summary>Gets or sets a value indicating whether one retained placement changes content each frame.</summary>
    [Params(false, true)]
    public bool Dynamic { get; set; }

    /// <summary>Primes 26 resident placements outside timing.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _fixture = new(26);
        _fixture.PrepareRecordRetire();
        if (_fixture.Statistics.CachedAssets != 26)
        {
            throw new InvalidOperationException("The frame fixture did not retain 26 assets.");
        }
    }

    /// <summary>Edits when selected, prepares, records and retires with explicit simulated completion.</summary>
    /// <returns>The consumed command checksum and byte observations.</returns>
    [Benchmark]
    public ulong Frame()
    {
        if (Dynamic)
        {
            _fixture.EditAndPublish(SceneMutation.Content);
        }

        ulong checksum = _fixture.PrepareRecordRetire();
        for (int read = 0; read < StatisticsReads; read++)
        {
            checksum ^= (ulong)_fixture.Statistics.CachedBytes;
        }

        return checksum;
    }

    /// <summary>Releases all completion-retired resources.</summary>
    [GlobalCleanup]
    public void Cleanup() => _fixture.Dispose();
}
