using BenchmarkDotNet.Attributes;

namespace Lumyte.Resources.Graphics.Benchmark.Pipelines;

/// <summary>Actual resource registration, typed consumer resolution and CPU command recording, without shader execution.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
public class GraphicsConsumerResourceBenchmarks
{
    private ResourceModelWorkload _workload = null!;

    /// <summary>Registers and primes real consumer shader packages.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _workload = new(true);
        _workload.RendererResourceAsync().AsTask().GetAwaiter().GetResult();
        _workload.EffectResourceAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>Acquires renderer shaders and creates/disposes a typed renderer.</summary>
    /// <returns>The consumed value or operation observation.</returns>
    [Benchmark]
    public async Task<ulong> RendererFactoryAsync() => await _workload.RendererResourceAsync();

    /// <summary>Acquires and pins the shared effect shaders.</summary>
    /// <returns>The consumed value or operation observation.</returns>
    [Benchmark]
    public async Task<ulong> ImageEffectShaderPackageAsync() => await _workload.EffectResourceAsync();

    /// <summary>Records a real resource-managed graph through scoped command-renderer DI.</summary>
    /// <returns>The nonzero draw count.</returns>
    [Benchmark]
    public async Task<ulong> GraphRendererFactoryRecordAsync() => await _workload.GraphRendererResourceAsync();

    /// <summary>Includes DI registration/resolution and complete model build/release.</summary>
    /// <returns>The consumed value or operation observation.</returns>
    [Benchmark]
    public async Task<ulong> RegisterResolveModelDisposeAsync()
    {
        await using var root = new ResourceModelWorkload(false);
        return await root.ColdDecodeUploadTrimAsync();
    }

    /// <summary>Releases the root.</summary>
    [GlobalCleanup]
    public void Cleanup() => _workload.DisposeAsync().AsTask().GetAwaiter().GetResult();
}
