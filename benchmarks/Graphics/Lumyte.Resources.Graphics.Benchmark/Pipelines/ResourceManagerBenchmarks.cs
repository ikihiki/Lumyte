using BenchmarkDotNet.Attributes;

namespace Lumyte.Resources.Graphics.Benchmark.Pipelines;

/// <summary>Real source/decode/upload/cache/pin/release paths; completion is CPU simulation.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
public class ResourceManagerBenchmarks
{
    private ResourceModelWorkload _cold = null!;
    private ResourceModelWorkload _warm = null!;

    /// <summary>Registers independent roots without process-global Execution endpoints.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _cold = new(false);
        _warm = new(true);
    }

    /// <summary>Decodes, allocates, uploads and releases an entire noncached generation.</summary>
    /// <returns>The consumed value or operation observation.</returns>
    [Benchmark]
    public async Task<ulong> ColdDecodeUploadTrimAsync() => await _cold.ColdDecodeUploadTrimAsync();

    /// <summary>Reuses an actually retained generation, with no source reads or uploads.</summary>
    /// <returns>The consumed value or operation observation.</returns>
    [Benchmark]
    public async Task<ulong> WarmRetainPinImportAsync() => await _warm.RetainPinImportAsync();

    /// <summary>Rebuilds GPU output while its CPU decode dependency remains cached where resident.</summary>
    /// <returns>The consumed value or operation observation.</returns>
    [Benchmark]
    public async Task<ulong> RebuildGraphicsGenerationAsync() => await _warm.RebuildGraphicsAsync();

    /// <summary>Releases managers before their original device owners.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _warm.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _cold.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
