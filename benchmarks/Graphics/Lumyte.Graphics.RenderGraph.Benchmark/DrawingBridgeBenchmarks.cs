using BenchmarkDotNet.Attributes;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Benchmarks.Testing;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Mathematics.Colors;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.RenderGraph.Benchmark.DrawingBridge;

/// <summary>Actual public Drawing2D-to-RenderGraph append, compile and command preparation; no pixels execute.</summary>
[BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
public class DrawingBridgeBenchmarks
{
    private CpuGraphicsDevice _device = null!;
    private RenderGraphDrawList2D _list = null!;
    private DrawingCommandRenderers2D _renderers = null!;

    /// <summary>Gets or sets the bounded circle command count.</summary>
    [Params(8, 64)]
    public int CommandCount { get; set; }

    /// <summary>Records a reusable command list outside graph preparation.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _device = new();
        _renderers = DrawingCommandRenderers2D.CreateDefault();
        var canvas = new RenderGraphCanvas2D();
        canvas.Begin();
        for (int i = 0; i < CommandCount; i++)
        {
            canvas.FillCircle(new(4 + ((i % 8) * 7), 4 + ((i / 8) * 7)), 3, new SRgba<float>(1, 0.2f, 0.4f, 1));
        }

        _list = canvas.End();
    }

    /// <summary>Creates fresh graph-local preparation and records all live drawing batches each time.</summary>
    /// <returns>The bounded CPU command observations after cleanup.</returns>
    [Benchmark]
    public async Task<CpuGraphicsCounters> AppendCompileAllocateRecordCleanupAsync()
    {
        _device.ResetCounters();
        using (var graph = new Graph(_device))
        {
            var description = new TextureDesc { Width = 64, Height = 64, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment | TextureUsage.Sampled };
            RenderGraphTexture target = graph.CreateTexture(description);
            using var renderer = new RenderGraphRenderer2D(_renderers, _list, new() { Texture = target, Description = description, LoadOp = AttachmentLoadOp.Clear }, new DrawingGraphBindings2D(graph));
            using PreparedDrawingPasses2D prepared = await graph.Append2D(renderer);
            graph.ExportTexture(target, TextureState.Sampled, new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead));
            using CompiledRenderGraph plan = graph.Compile();
            using IGraphicsCommandBuffer commands = _device.CreateCommandBuffer(new());
            plan.Record(commands);
            commands.Finish();
        }

        return _device.Counters;
    }

    /// <summary>Releases the CPU recording device after graph resources have been retired.</summary>
    [GlobalCleanup]
    public void Cleanup() => _device.Dispose();
}
