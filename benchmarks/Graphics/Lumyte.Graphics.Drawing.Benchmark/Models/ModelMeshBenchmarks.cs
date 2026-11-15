using BenchmarkDotNet.Attributes;

namespace Lumyte.Graphics.Drawing.Benchmark.Models;

/// <summary>Actual mesh allocation, staging, copy recording and disposal; no GPU work.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
public class ModelMeshBenchmarks
{
    private ModelWorkload _workload = null!;

    /// <summary>Creates retained device/shader inputs.</summary>
    [GlobalSetup]
    public void Setup() => _workload = new();

    /// <summary>Uploads typed vertices and native odd ushort indices.</summary>
    /// <returns>The consumed value or operation observation.</returns>
    [Benchmark]
    public ulong TypedMeshUpload() => _workload.CreateUploadDispose(false);

    /// <summary>Includes packed CPU byte preparation and bulk upload.</summary>
    /// <returns>The consumed value or operation observation.</returns>
    [Benchmark]
    public ulong PackedMeshUpload() => _workload.CreateUploadDispose(true);

    /// <summary>Releases retained resources.</summary>
    [GlobalCleanup]
    public void Cleanup() => _workload.Dispose();
}
