using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.Pipelines;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Graphics.ImageEffects;
using Lumyte.Graphics.RenderGraph;
using static Lumyte.Graphics.Drawing2D.Pipelines.DrawingCompose;

namespace Lumyte.Graphics.Tests;

/// <summary>Creates the real sparse family through public typed declarations.</summary>
public sealed partial class Drawing2DTests
{
    private static IRenderer2D CreateSparseOracleRenderer(IGraphicDevice device, bool legacyCircle = false)
    {
        IGraphicsShader vertex = device.CreateShader(DrawingShaders2D.Vertex);
        IGraphicsShader fragment = device.CreateShader(DrawingShaders2D.Fragment);
        IGraphicsPipeline pipeline;
        try
        {
            pipeline = device.CreateGraphicsPipeline(new() { VertexShader = vertex, FragmentShader = fragment });
        }
        catch
        {
            fragment.Dispose();
            vertex.Dispose();
            throw;
        }

        var owner = new SparseOracleResources(pipeline, fragment, vertex);
        try
        {
            Definitions.Renderer definition = Renderer(argumentTables: DrawingArgumentTableMode.Auto, retention: RetentionGpuLimits(3, 512, 64 * 1024 * 1024, 256 * 1024 * 1024))[
                Renderer.FillRect()[SparseFillRect(CpuSparse(8 * 1024 * 1024, 64, 65536, 262144))],
                Renderer.FillRoundedRect()[SparseFillRoundedRect(CpuSparse(8 * 1024 * 1024, 64, 65536, 262144))],
                Renderer.FillCircle()[legacyCircle ? StandardFillCircle() : SparseFillCircle(CpuSparse(8 * 1024 * 1024, 64, 65536, 262144))],
                Renderer.DrawLine()[SparseDrawLine(CpuSparse(8 * 1024 * 1024, 64, 65536, 262144))],
                Renderer.FillPath()[SparseFillPath(CpuSparse(8 * 1024 * 1024, 64, 65536, 262144))],
                Renderer.StrokePath()[SparseStrokePath(CpuSparse(8 * 1024 * 1024, 64, 65536, 262144))],
                Renderer.DrawImage()[SparseDrawImage(CpuSparse(8 * 1024 * 1024, 64, 65536, 262144))],
                Renderer.DrawCoverageMask()[SparseDrawCoverageMask(CpuSparse(8 * 1024 * 1024, 64, 65536, 262144))],
                Renderer.DrawDistanceField()[SparseDrawDistanceField(CpuSparse(8 * 1024 * 1024, 64, 65536, 262144))],
                Renderer.DrawMesh()[SparseDrawMesh(CpuSparse(8 * 1024 * 1024, 64, 65536, 262144))],
                Renderer.DrawPaintGraph()[SparseDrawPaintGraph(CpuSparse(8 * 1024 * 1024, 64, 65536, 262144))]];
            using RendererBuildHost2D host = definition.CreateHost();
            return host.BuildRenderer(device, new DrawingResources2D(device, pipeline), owner, null);
        }
        catch
        {
            owner.Dispose();
            throw;
        }
    }

    private static async Task<(PreparedDrawingPasses2D Owner, EffectImage Image)> AppendSparseOracleImageAsync(Lumyte.Graphics.RenderGraph.RenderGraph graph, IRenderer2D renderer, DrawList2D list, uint width, uint height, params IGraphicsTexture[] inputs)
    {
        var description = new TextureDesc { Width = width, Height = height, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.Sampled | TextureUsage.CopySource };
        RenderGraphTexture target = graph.CreateTexture(description, "sparse-oracle");
        var bindings = new DrawingGraphBindings2D(graph);
        foreach (IGraphicsTexture texture in inputs.Distinct())
        {
            RenderGraphTexture imported = graph.ImportTexture(texture, TextureState.Sampled, new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead), "sparse-input");
            bindings.BindTexture(texture, imported);
        }

        var canvas = new RenderGraphCanvas2D();
        canvas.Begin();
        canvas.Replay(list);
        using var configured = new RenderGraphRenderer2D(renderer, canvas.End(), new RenderTargetBinding2D { Texture = target, Description = description, LoadOp = AttachmentLoadOp.Clear }, bindings);
        PreparedDrawingPasses2D owner = await graph.Append2D(configured);
        return (owner, new EffectImage(target, new(0, 0, checked((int)width), checked((int)height)), EffectColorSemantic.DisplayLinear));
    }

    private sealed class SparseOracleResources(params IDisposable[] resources) : IDisposable
    {
        private int _next;

        public void Dispose()
        {
            while (_next < resources.Length)
            {
                resources[_next].Dispose();
                _next++;
            }
        }
    }
}
