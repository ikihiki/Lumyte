using BenchmarkDotNet.Attributes;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Benchmarks.Testing;
using Lumyte.Graphics.RenderGraph;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.ImageEffects.Benchmark.Preparation;

/// <summary>CPU effect expansion and command preparation, never shader execution or image processing.</summary>
/// <remarks>Embedded shader artifacts may be process-cached. Every invocation creates fresh graph-local preparation and resources. Output pixels and effect quality cannot be judged.</remarks>
[BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
public class EffectBenchmarks
{
    private CpuGraphicsDevice _device = null!;
    private IGraphicsTexture _source = null!;
    private ImageEffectGraph _effects = null!;

    /// <summary>Gets or sets the built-in DAG shape.</summary>
    [Params(EffectScenario.Blur, EffectScenario.BlurComposite, EffectScenario.SharedBranch)]
    public EffectScenario Scenario { get; set; }

    /// <summary>Prepares a reusable borrowed source and immutable effect definition.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _device = new();
        _source = _device.CreateTexture(EffectWorkloads.SourceDescription());
        _effects = EffectWorkloads.Definition(Scenario);
    }

    /// <summary>Expands actual built-in stages, creates their fixed resources, compiles, and cleans up without recording.</summary>
    /// <returns>The nonempty compiled plan statistics.</returns>
    [Benchmark]
    public RenderGraphStatistics PrepareCompileCleanup()
    {
        using Graph graph = EffectWorkloads.Build(_device, _source, _effects);
        using CompiledRenderGraph plan = graph.Compile();
        return plan.Statistics;
    }

    /// <summary>Adds graph allocation, argument transfer and fullscreen draw command recording to preparation.</summary>
    /// <returns>The observations after discarding command memory and releasing graph-owned resources.</returns>
    [Benchmark]
    public CpuGraphicsCounters PrepareCompileAllocateRecordCleanup()
    {
        _device.ResetCounters();
        using (Graph graph = EffectWorkloads.Build(_device, _source, _effects))
        using (CompiledRenderGraph plan = graph.Compile())
        using (IGraphicsCommandBuffer commands = _device.CreateCommandBuffer(new()))
        {
            plan.Record(commands);
            commands.Finish();
        }

        return _device.Counters;
    }

    /// <summary>Releases the imported source after all per-invocation owners.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _source.Dispose();
        _device.Dispose();
    }
}
