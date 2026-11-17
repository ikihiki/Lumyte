using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.Pipelines;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Mathematics.Colors;
using Xunit;
using static Lumyte.Graphics.Drawing2D.Pipelines.DrawingCompose;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Rejects cumulative membership work after a different custom command group.</summary>
public sealed class CoverageMembershipBudgetTests
{
    /// <summary>The second group must see both the configured root cap and the first group's private owner.</summary>
    /// <param name="graphRoute">Whether the normal parent Graph prepares the same selected table.</param>
    /// <returns>The completed no-GPU budget check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PriorCustomGroupConsumesConfiguredMembershipBudgetAsync(bool graphRoute)
    {
        var device = new ProfilingDevice { Caps = new() { ShaderTarget = ShaderTarget.SpirV, MaxBufferSize = 1UL << 30, MaxComputeInvocationsPerWorkgroup = 256, MaxComputeWorkgroupSizeX = 16, MaxComputeWorkgroupSizeY = 16, MaxComputeWorkgroupSizeZ = 1, MaxComputeWorkgroupsPerDimension = 65535 } };
        using IGraphicsShader vertex = device.CreateShader(DrawingShaders2D.Vertex);
        using IGraphicsShader fragment = device.CreateShader(DrawingShaders2D.Fragment);
        using IGraphicsPipeline pipeline = device.CreateGraphicsPipeline(new() { VertexShader = vertex, FragmentShader = fragment });
        var seed = new SeedRenderer();
        var seedDeclaration = new BorrowedPipeline2D<FillRectCommand2D>(seed);
        using RendererBuildHost2D host = Renderer(argumentTables: DrawingArgumentTableMode.Auto, retention: RetentionGpuLimits(maxFramesInFlight: 2, maxCachedAssets: 8, maxCachedBytes: 64 * 1024 * 1024, maxWorkingBytes: 1_000_000))[
            Renderer.FillRect()[seedDeclaration],
            Renderer.FillRoundedRect()[StandardFillRoundedRect()],
            Renderer.FillCircle()[StandardFillCircle()],
            Renderer.DrawLine()[StandardDrawLine()],
            Renderer.FillPath()[CoverageMembershipFillPath()],
            Renderer.StrokePath()[StandardStrokePath()],
            Renderer.DrawImage()[StandardDrawImage()],
            Renderer.DrawCoverageMask()[StandardDrawCoverageMask()],
            Renderer.DrawDistanceField()[StandardDrawDistanceField()],
            Renderer.DrawMesh()[StandardDrawMesh()],
            Renderer.DrawPaintGraph()[StandardDrawPaintGraph()]].CreateHost();
        using IRenderer2D renderer = host.BuildRenderer(device, new(device, pipeline), null, null);
        if (graphRoute)
        {
            var canvas = new RenderGraphCanvas2D();
            canvas.Begin();
            Draw(canvas);
            using var graph = new Graph(device, "membership-cumulative-budget");
            var description = new TextureDesc { Width = 16, Height = 16, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment };
            Lumyte.Graphics.RenderGraph.RenderGraphTexture target = graph.CreateTexture(description, "target");
            using var graphRenderer = new RenderGraphRenderer2D(renderer, canvas.End(), new() { Texture = target, Description = description, LoadOp = AttachmentLoadOp.Clear }, new(graph));
            PreparedDrawingPasses2D? unexpected = null;
            try
            {
                await Assert.ThrowsAsync<NotSupportedException>(async () => { unexpected = await graph.Append2D(graphRenderer); });
            }
            finally
            {
                unexpected?.Dispose();
            }
        }
        else
        {
            var canvas = new Canvas2D();
            canvas.Begin();
            Draw(canvas);
            using var target = new DispatchTexture { Width = 16, Height = 16, Format = TextureFormat.Rgba16Float };
            using IGraphicsTextureView view = target.CreateView();
            PreparedDraw2D? unexpected = null;
            try
            {
                await Assert.ThrowsAsync<NotSupportedException>(async () => { unexpected = await renderer.PrepareAsync(canvas.End(), new() { View = view }); });
            }
            finally
            {
                unexpected?.Dispose();
            }
        }

        Assert.Equal(1_000_000, seed.Maximum);
        Assert.Equal(1, seed.Batch.Disposals);
        Assert.Equal(0, device.NativeBuffers);
        Assert.Equal(0, device.Writes);
    }

    /// <summary>A late custom group cannot publish an owner exceeding the cap after membership preparation.</summary>
    /// <param name="graphRoute">Selects the normal parent Graph route.</param>
    /// <param name="warm">Reuses a disposed, unrecorded numeric-pool owner first.</param>
    /// <returns>The completed final-aggregate admission check.</returns>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task FinalAggregateIncludesLateCustomOwnerAsync(bool graphRoute, bool warm)
    {
        var device = new ProfilingDevice();
        using IGraphicsShader vertex = device.CreateShader(DrawingShaders2D.Vertex);
        using IGraphicsShader fragment = device.CreateShader(DrawingShaders2D.Fragment);
        using IGraphicsPipeline pipeline = device.CreateGraphicsPipeline(new() { VertexShader = vertex, FragmentShader = fragment });
        var late = new LateRoundedRenderer();
        var declaration = new BorrowedPipeline2D<FillRoundedRectCommand2D>(late);
        using RendererBuildHost2D host = Renderer(argumentTables: DrawingArgumentTableMode.Auto, retention: RetentionGpuLimits(maxFramesInFlight: 2, maxCachedAssets: 0, maxCachedBytes: 0, maxWorkingBytes: 150_000))[
            Renderer.FillRect()[CoverageMembershipFillRect()],
            Renderer.FillRoundedRect()[declaration],
            Renderer.FillCircle()[StandardFillCircle()],
            Renderer.DrawLine()[StandardDrawLine()],
            Renderer.FillPath()[CoverageMembershipFillPath()],
            Renderer.StrokePath()[StandardStrokePath()],
            Renderer.DrawImage()[StandardDrawImage()],
            Renderer.DrawCoverageMask()[StandardDrawCoverageMask()],
            Renderer.DrawDistanceField()[StandardDrawDistanceField()],
            Renderer.DrawMesh()[StandardDrawMesh()],
            Renderer.DrawPaintGraph()[StandardDrawPaintGraph()]].CreateHost();
        using IRenderer2D renderer = host.BuildRenderer(device, new(device, pipeline), null, null);
        using var texture = new DispatchTexture { Width = 8, Height = 8, Format = TextureFormat.Rgba16Float };
        using IGraphicsTextureView view = texture.CreateView();
        if (warm)
        {
            var initial = new Canvas2D();
            initial.Begin();
            initial.FillRect(new(0, 0, 8, 8), new SRgba<float>(1, 0, 0, 1));
            using PreparedDraw2D prepared = await renderer.PrepareAsync(initial.End(), new() { View = view });
        }

        int priorWrites = device.Writes;
        if (graphRoute)
        {
            var canvas = new RenderGraphCanvas2D();
            canvas.Begin();
            DrawLate(canvas);
            using var graph = new Graph(device, "membership-final-budget");
            var description = new TextureDesc { Width = 8, Height = 8, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment };
            Lumyte.Graphics.RenderGraph.RenderGraphTexture target = graph.CreateTexture(description, "target");
            using var graphRenderer = new RenderGraphRenderer2D(renderer, canvas.End(), new() { Texture = target, Description = description, LoadOp = AttachmentLoadOp.Clear }, new(graph));
            PreparedDrawingPasses2D? unexpected = null;
            try
            {
                await Assert.ThrowsAsync<NotSupportedException>(async () => { unexpected = await graph.Append2D(graphRenderer); });
            }
            finally
            {
                unexpected?.Dispose();
            }
        }
        else
        {
            var canvas = new Canvas2D();
            canvas.Begin();
            DrawLate(canvas);
            PreparedDraw2D? unexpected = null;
            try
            {
                await Assert.ThrowsAsync<NotSupportedException>(async () => { unexpected = await renderer.PrepareAsync(canvas.End(), new() { View = view }); });
            }
            finally
            {
                unexpected?.Dispose();
            }
        }

        Assert.True(late.Other > 0);
        Assert.Equal(1, late.Batch.Disposals);
        Assert.Equal(priorWrites, device.Writes);
    }

    private static void DrawLate(DrawingCanvas2D canvas)
    {
        canvas.FillRect(new(0, 0, 8, 8), new SRgba<float>(1, 0, 0, 1));
        canvas.FillRoundedRect(new(0, 0, 8, 8), 0, new SRgba<float>(0, 0, 1, 1));
    }

    private static void Draw(DrawingCanvas2D canvas)
    {
        var paint = PaintGraph2D.Solid(new SRgba<float>(1, 0, 0, 1));
        canvas.FillRect(new(0, 0, 4, 4), paint);
        var path = new PathBuilder2D();
        path.MoveTo(new(1, 1));
        path.LineTo(new(12, 2));
        path.LineTo(new(2, 12));
        path.Close();
        canvas.FillPath(path.Build(), paint);
    }

    private sealed class SeedRenderer : IFillRectRenderer2D
    {
        internal SeedBatch Batch { get; } = new();

        internal long Maximum { get; private set; }

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillRectCommand2D> commands, CancellationToken cancellationToken = default)
        {
            Maximum = context.MaxWorkingBytes;
            return ValueTask.FromResult<IPreparedCommandBatch2D>(Batch);
        }
    }

    private sealed class LateRoundedRenderer : IFillRoundedRectRenderer2D
    {
        internal SeedBatch Batch { get; } = new(150_001);

        internal long Other { get; private set; }

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillRoundedRectCommand2D> commands, CancellationToken cancellationToken = default)
        {
            Other = context.OtherWorkingBytes;
            return ValueTask.FromResult<IPreparedCommandBatch2D>(Batch);
        }
    }

    private sealed class SeedBatch(long bytes = 999_000) : IRetainedCommandBatch2D
    {
        public long EstimatedBytes => bytes;

        internal int Disposals { get; private set; }

        public void DeclareResources(IDrawingResourceDeclarations2D resources)
        {
        }

        public void Record(IGraphicsCommandBuffer commands, IGraphicsTextureView target)
        {
        }

        public void RecordRetained(IGraphicsCommandBuffer commands, IGraphicsTextureView target, bool resident)
        {
        }

        public void Dispose() => Disposals++;
    }
}
