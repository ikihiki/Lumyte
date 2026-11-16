using BenchmarkDotNet.Attributes;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Benchmarks.Testing;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.RenderGraph.Benchmark.Compilation;

/// <summary>Fresh declarations and compilation, with optional lazy allocation and CPU command recording.</summary>
[BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
public class GraphBenchmarks
{
    private CpuGraphicsDevice _device = null!;
    private IGraphicsBuffer<uint> _source = null!;

    /// <summary>Gets or sets the bounded number of retained copies.</summary>
    [Params(8, 32)]
    public int PassCount { get; set; }

    /// <summary>Gets or sets the dependency shape.</summary>
    [Params(GraphShape.Chain, GraphShape.FanOutJoin, GraphShape.ImportedAlias)]
    public GraphShape Shape { get; set; }

    /// <summary>Creates the reusable imported allocation, outside the measured boundary.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _device = new();
        _source = _device.CreateBuffer<uint>(new() { Count = 64, Usage = BufferUsage.CopySource | BufferUsage.CopyDestination });
    }

    /// <summary>Builds and compiles a fresh graph; does not allocate its temporary buffers.</summary>
    /// <returns>The nonempty plan's statistics.</returns>
    [Benchmark]
    public RenderGraphStatistics BuildCompile()
    {
        using Graph graph = GraphWorkloads.Build(_device, _source, PassCount, Shape);
        using CompiledRenderGraph plan = graph.Compile();
        return plan.Statistics;
    }

    /// <summary>Builds, compiles, allocates, records and discards a fresh graph on every invocation.</summary>
    /// <returns>The bounded command observations, after cleanup.</returns>
    [Benchmark]
    public CpuGraphicsCounters BuildCompileAllocateRecordCleanup()
    {
        _device.ResetCounters();
        using (Graph graph = GraphWorkloads.Build(_device, _source, PassCount, Shape))
        using (CompiledRenderGraph plan = graph.Compile())
        using (IGraphicsCommandBuffer commands = _device.CreateCommandBuffer(new()))
        {
            plan.Record(commands);
            commands.Finish();
        }

        return _device.Counters;
    }

    /// <summary>Releases the borrowed allocation after all plans have been discarded.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _source.Dispose();
        _device.Dispose();
    }
}
