using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.Pipelines;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Mathematics.Colors;
using Xunit;
using static Lumyte.Graphics.Drawing2D.Pipelines.DrawingCompose;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Exact near-boundary aggregate capacity checks through public renderer entry points.</summary>
public sealed partial class CompositionMigrationTests
{
    /// <summary>The full two-group charge must fit, not just the strip-only preliminary charge.</summary>
    /// <param name="graph">Whether to use the normal graph adapter.</param>
    /// <returns>The completed public admission check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SparseNearBoundaryPublicBudgetRejectsBeforeSecondGroupAsync(bool graph)
    {
        using RendererBuildHost2D host = SparseBoundaryDefinition(150000, 0).CreateHost();
        var device = new ProfilingDevice();
        using IRenderer2D renderer = host.BuildRenderer(device, Resources(device), null, null);
        using var texture = new DispatchTexture { Width = 8, Height = 8, Format = TextureFormat.Rgba16Float };
        using IGraphicsTextureView view = texture.CreateView();
        DrawList2D list = SparseBoundaryContent(false);
        await Assert.ThrowsAsync<NotSupportedException>(() => PrepareSparseBoundaryAsync(device, renderer, list, view, graph));
    }

    /// <summary>Cold/warm and both logical orders reject without allocating the second group's buffers.</summary>
    /// <param name="graph">Whether preparation uses the ordinary graph adapter.</param>
    /// <param name="roundedFirst">Whether rounded geometry precedes the rectangle.</param>
    /// <param name="warm">Whether both geometry cache entries have been prepared.</param>
    /// <returns>The completed repeatable admission check.</returns>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task SparseNearBoundaryColdWarmOrdersReserveBeforeNativeBuffersAsync(bool graph, bool roundedFirst, bool warm)
    {
        using RendererBuildHost2D host = SparseBoundaryDefinition(150000, warm ? 1024 * 1024 : 0).CreateHost();
        var device = new ProfilingDevice();
        using IRenderer2D renderer = host.BuildRenderer(device, Resources(device), null, null);
        using var texture = new DispatchTexture { Width = 8, Height = 8, Format = TextureFormat.Rgba16Float };
        using IGraphicsTextureView view = texture.CreateView();
        DrawList2D both = SparseBoundaryContent(roundedFirst);
        var first = new DrawList2D([both.Commands[0]]);
        var second = new DrawList2D([both.Commands[1]]);
        if (warm)
        {
            await PrepareSparseBoundaryAsync(device, renderer, first, view, graph);
            await PrepareSparseBoundaryAsync(device, renderer, second, view, graph);
        }

        int beforeSingle = device.NativeBuffers;
        await PrepareSparseBoundaryAsync(device, renderer, first, view, graph);
        int firstGroupBuffers = device.NativeBuffers - beforeSingle;
        Assert.True(firstGroupBuffers > 0);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            int beforeBoth = device.NativeBuffers;
            await Assert.ThrowsAsync<NotSupportedException>(() => PrepareSparseBoundaryAsync(device, renderer, both, view, graph));
            Assert.Equal(firstGroupBuffers, device.NativeBuffers - beforeBoth);
        }
    }

    /// <summary>The admission predicate is pure and aggregate overflow cannot wrap into acceptance.</summary>
    [Fact]
    public void SparseAdmissionAndCumulativeOverflowAreAllocationFree()
    {
        var device = new ProfilingDevice();
        using var resources = new DrawingResourceState2D(device);
        using var arguments = new DrawingArgumentPlan2D(device);
        var target = new DrawingTargetInfo2D { Description = new() { Width = 8, Height = 8, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment } };
        var context = new RenderPreparationContext2D(device, target, resources, arguments);
        using var service = new SparseCpuService2D(CpuSparse(0, 0, 65536, 262144));
        var element = new SparseFillRectElement2D(service);
        FillRectCommand2D command = Assert.IsType<FillRectCommand2D>(SparseBoundaryContent(false).Commands[0]);
        Assert.True(element.CanPrepare(context, new FillRectCommand2D[] { command }));
        Assert.Equal(0, device.NativeBuffers);
        Assert.Equal(0, device.ShaderCreations);
        Assert.Equal(0, device.PipelineCreations);
        PreparedSparseStripBatch2D.ValidateWorkingCapacity(long.MaxValue, 0, 0, long.MaxValue);
        Assert.Throws<NotSupportedException>(() => PreparedSparseStripBatch2D.ValidateWorkingCapacity(long.MaxValue, 1, 0, long.MaxValue));
        Assert.Throws<NotSupportedException>(() => PreparedSparseStripBatch2D.ValidateWorkingCapacity(1, long.MaxValue, 0, long.MaxValue));
        Assert.Throws<NotSupportedException>(() => PreparedSparseStripBatch2D.ValidateWorkingCapacity(0, 1, long.MaxValue, long.MaxValue));
        Assert.Equal(0, device.NativeBuffers);
    }

    private static Definitions.Renderer SparseBoundaryDefinition(long budget, long cacheBytes) => Renderer(argumentTables: DrawingArgumentTableMode.Auto, retention: RetentionGpuLimits(3, 32, 1024 * 1024, budget))[
        Renderer.FillRect()[SparseFillRect(CpuSparse(cacheBytes, cacheBytes == 0 ? 0 : 8, 65536, 262144))],
        Renderer.FillRoundedRect()[SparseFillRoundedRect(CpuSparse(cacheBytes, cacheBytes == 0 ? 0 : 8, 65536, 262144))],
        Renderer.FillCircle()[StandardFillCircle()],
        Renderer.DrawLine()[StandardDrawLine()],
        Renderer.FillPath()[StandardFillPath()],
        Renderer.StrokePath()[StandardStrokePath()],
        Renderer.DrawImage()[StandardDrawImage()],
        Renderer.DrawCoverageMask()[StandardDrawCoverageMask()],
        Renderer.DrawDistanceField()[StandardDrawDistanceField()],
        Renderer.DrawMesh()[StandardDrawMesh()],
        Renderer.DrawPaintGraph()[StandardDrawPaintGraph()]];

    private static DrawList2D SparseBoundaryContent(bool roundedFirst)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        if (roundedFirst)
        {
            canvas.FillRoundedRect(new(0, 0, 8, 8), 0, new SRgba<float>(0, 0, 1, 1));
            canvas.FillRect(new(0, 0, 8, 8), new SRgba<float>(1, 0, 0, 1));
        }
        else
        {
            canvas.FillRect(new(0, 0, 8, 8), new SRgba<float>(1, 0, 0, 1));
            canvas.FillRoundedRect(new(0, 0, 8, 8), 0, new SRgba<float>(0, 0, 1, 1));
        }

        return canvas.End();
    }

    private static async Task PrepareSparseBoundaryAsync(IGraphicDevice device, IRenderer2D renderer, DrawList2D list, IGraphicsTextureView view, bool graph)
    {
        if (!graph)
        {
            using PreparedDraw2D prepared = await renderer.PrepareAsync(list, new() { View = view });
            return;
        }

        var canvas = new RenderGraphCanvas2D();
        canvas.Begin();
        canvas.Replay(list);
        using var parent = new Lumyte.Graphics.RenderGraph.RenderGraph(device, "sparse-boundary");
        var description = new TextureDesc { Width = 8, Height = 8, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment };
        Lumyte.Graphics.RenderGraph.RenderGraphTexture target = parent.CreateTexture(description);
        var bindings = new DrawingGraphBindings2D(parent);
        using var configured = new RenderGraphRenderer2D(renderer, canvas.End(), new() { Texture = target, Description = description }, bindings);
        using PreparedDrawingPasses2D owner = await parent.Append2D(configured);
    }
}
