using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.Pipelines;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Mathematics.Colors;
using Xunit;
using static Lumyte.Graphics.Drawing2D.Pipelines.DrawingCompose;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Each independently valid membership page remains one physical graph binding operation.</summary>
public sealed class CoverageMembershipMultipageTests
{
    /// <summary>Two 100x100 masks require two pages at the unchanged 64KiB/7-binding limits.</summary>
    /// <param name="failWrite">Zero succeeds; ten injects a graph table registration failure after both pages prepare.</param>
    /// <returns>The completed no-GPU graph recording.</returns>
    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public async Task TwoValidPagesKeepSeparateGraphBindingOperationsAsync(int failWrite)
    {
        ProfilingDevice device = CreateDevice(failWrite);
        using IGraphicsShader vertex = device.CreateShader(DrawingShaders2D.Vertex);
        using IGraphicsShader fragment = device.CreateShader(DrawingShaders2D.Fragment);
        using IGraphicsPipeline pipeline = device.CreateGraphicsPipeline(new() { VertexShader = vertex, FragmentShader = fragment });
        using RendererBuildHost2D host = Renderer(argumentTables: DrawingArgumentTableMode.Auto, retention: RetentionGpuLimits(maxFramesInFlight: 3, maxCachedAssets: 8, maxCachedBytes: 65536, maxWorkingBytes: 256 * 1024 * 1024))[
            Renderer.FillRect()[StandardFillRect()],
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
        var canvas = new RenderGraphCanvas2D();
        canvas.Begin();
        canvas.FillPath(Box(0), new SRgba<float>(1, 0, 0, 0.5f));
        canvas.FillPath(Box(20), new SRgba<float>(0, 1, 0, 0.5f));
        using var graph = new Graph(device, "membership-two-valid-pages");
        var description = new TextureDesc { Width = 128, Height = 128, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.Sampled };
        Lumyte.Graphics.RenderGraph.RenderGraphTexture target = graph.CreateTexture(description, "target");
        using var graphRenderer = new RenderGraphRenderer2D(renderer, canvas.End(), new() { Texture = target, Description = description, LoadOp = AttachmentLoadOp.Clear }, new(graph));
        using PreparedDrawingPasses2D drawing = await graph.Append2D(graphRenderer);
        graph.ExportTexture(target, TextureState.ColorAttachment, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite));
        using Lumyte.Graphics.RenderGraph.CompiledRenderGraph compiled = graph.Compile();
        using var commands = new ProfilingCommands();
        if (failWrite != 0)
        {
            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => compiled.Record(commands));
            Assert.Contains("Injected registration failure", failure.Message, StringComparison.Ordinal);
            Cleanup();
            return;
        }

        compiled.Record(commands);
        Assert.Equal(2, device.Records.Count);
        Assert.All(device.Records, record => Assert.Equal(4u, record.Kind));
        ShapeMembershipArguments2D[] dispatches = commands.Events.OfType<ShapeMembershipArguments2D>().ToArray();
        Assert.Equal(2, dispatches.Length);
        Assert.Equal(new uint[] { 101, 102 }, dispatches.Select(dispatch => dispatch.Width));
        Assert.Equal(new uint[] { 101, 102 }, dispatches.Select(dispatch => dispatch.Height));
        Assert.All(dispatches, dispatch =>
        {
            IShaderReference output = Assert.IsAssignableFrom<IShaderReference>(dispatch.Output);
            Assert.Equal((ulong)dispatch.Width * dispatch.Height * 4, output.SizeInBytes);
            Assert.InRange(output.SizeInBytes, 1UL, 65536UL);
        });
        Assert.NotSame(((IShaderReference)dispatches[0].Output).Resource, ((IShaderReference)dispatches[1].Output).Resource);
        RecordedDrawRange[] draws = commands.Events.OfType<RecordedDrawRange>().ToArray();
        Assert.Equal(2, draws.Length);
        Assert.NotSame(draws[0].PageMetadata.Table, draws[1].PageMetadata.Table);
        Assert.All(draws, draw => Assert.Equal(1u, draw.RecordCount));
        Assert.All(device.Tables, table => Assert.InRange(table.BufferCapacity, 0u, 7u));
        Assert.Equal(2, device.Tables.Count(table => table.BufferCapacity == 7));
        Assert.Equal(new uint[] { 0, 0 }, dispatches.Select(dispatch => dispatch.Offset));
        Cleanup();

        void Cleanup()
        {
            commands.Dispose();
            compiled.Dispose();
            drawing.Dispose();
            graphRenderer.Dispose();
            graph.Dispose();
            renderer.Dispose();
            Assert.Equal(0, device.ActiveTables);
        }
    }

    /// <summary>Pre-reservation and immutable upload estimates cover both complete seven-buffer page footprints.</summary>
    /// <returns>The completed fake materialization check.</returns>
    [Fact]
    public async Task BothPagesKeepFullWorkingReferenceFootprintsAsync()
    {
        ProfilingDevice device = CreateDevice(0);
        using var resources = new DrawingResourceState2D(device);
        using var plan = new DrawingArgumentPlan2D(device);
        using var service = new CoverageMembershipService2D(device);
        var context = new RenderPreparationContext2D(device, new() { Description = new() { Width = 128, Height = 128, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment } }, resources, plan);
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillPath(Box(0), new SRgba<float>(1, 0, 0, 0.5f));
        canvas.FillPath(Box(20), new SRgba<float>(0, 1, 0, 0.5f));
        FillPathCommand2D[] commands = canvas.End().Commands.Cast<FillPathCommand2D>().ToArray();
        using IPreparedCommandBatch2D batch = await PreparedRecordBatch2D.PrepareAsync<FillPathCommand2D>(context.WithShapeMembership(service), commands, default);
        Assert.True(Assert.IsAssignableFrom<IPreparedWorkingBudget2D>(batch).EnforcesWorkingBudget);
        Assert.True(plan.WorkingReferencesEnabled);
        long collecting = plan.EstimatedBytes;
        Assert.True(collecting >= 256 + (2L * (7 + 1 + 1 + 2) * 256));
        await plan.SealAsync();
        Assert.True(collecting >= plan.EstimatedBytes);
        Assert.Equal(2, Assert.IsType<PreparedRecordBatch2D>(batch).GraphPages.Count());
        var owners = new Dictionary<object, long>(ReferenceEqualityComparer.Instance);
        Assert.IsAssignableFrom<IRetainedMemory2D>(batch).AccountMemory(owners);
        Assert.Equal(2, owners.Count);
        Assert.Equal(plan.EstimatedBytes, owners[plan]);
    }

    private static ProfilingDevice CreateDevice(int failWrite) => new ProfilingDevice { FailWrite = failWrite, Caps = new() { ShaderTarget = ShaderTarget.SpirV, MaxBufferSize = 1UL << 30, MaxStorageBufferBindingSize = 65536, MaxComputeInvocationsPerWorkgroup = 256, MaxComputeWorkgroupSizeX = 16, MaxComputeWorkgroupSizeY = 16, MaxComputeWorkgroupSizeZ = 1, MaxComputeWorkgroupsPerDimension = 65535, ArgumentTableBindingLimits = new() { BufferSlots = 7, BuffersPerDraw = 7, DirectSlotBindings = true, MaximumBufferRangeBytes = 65536 } } };

    private static Path2D Box(float offset)
    {
        var path = new PathBuilder2D();
        path.MoveTo(new(offset, offset));
        path.LineTo(new(offset + 100, offset));
        path.LineTo(new(offset + 100, offset + 100));
        path.LineTo(new(offset, offset + 100));
        path.Close();
        return path.Build();
    }
}
