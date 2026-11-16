using BenchmarkDotNet.Attributes;

namespace Lumyte.Graphics.Drawing.Benchmark.Models;

/// <summary>CPU draw-buffer uploads, texture bindings and indexed mesh recording.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
public class ModelDrawingBenchmarks
{
    private ModelWorkload _workload = null!;

    /// <summary>Gets or sets independently transformed draws sharing geometry, not hardware instancing.</summary>
    [Params(1, 32)]
    public int Instances { get; set; }

    /// <summary>Uploads the mesh outside measurement.</summary>
    [GlobalSetup]
    public void Setup() => _workload = new();

    /// <summary>Records model instances, without shading.</summary>
    /// <returns>The consumed value or operation observation.</returns>
    [Benchmark]
    public ulong RecordIndexedInstances() => _workload.RecordInstances(Instances);

    /// <summary>Releases retained resources.</summary>
    [GlobalCleanup]
    public void Cleanup() => _workload.Dispose();
}
