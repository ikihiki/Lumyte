using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.Pipelines;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Mathematics.Colors;
using Xunit;
using static Lumyte.Graphics.Drawing2D.Pipelines.DrawingCompose;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Exercises membership contracts without native GPU execution.</summary>
public sealed class CoverageMembershipTests
{
    /// <summary>Family and generic forks keep lazy prior accounting, service identity and the smaller cap.</summary>
    [Fact]
    public void MembershipForkPreservesLazyWorkingBudgetAndService()
    {
        var device = new ProfilingDevice();
        using var resources = new DrawingResourceState2D(device);
        using var plan = new DrawingArgumentPlan2D(device);
        using var service = new CoverageMembershipService2D(device);
        var context = new RenderPreparationContext2D(device, new() { Description = new() { Width = 16, Height = 16, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment } }, resources, plan) { MaxWorkingBytes = 1000 };
        int reads = 0;
        context.SetWorkingBytesSource(() =>
        {
            reads++;
            return 300;
        });
        RenderPreparationContext2D fork = context.WithShapeMembership(service).WithArgumentPlan(plan).WithWorkingLimit(500);
        Assert.Equal(0, reads);
        Assert.Equal(0, context.OtherWorkingBytes);
        Assert.Equal(0, reads);
        Assert.True(fork.ComputeShapeMembership);
        Assert.True(fork.EnforceWorkingBudget);
        Assert.Same(service, fork.MembershipService);
        Assert.Equal(500, fork.MaxWorkingBytes);
        Assert.Equal(300, fork.OtherWorkingBytes);
        Assert.Equal(300, fork.OtherWorkingBytes);
        Assert.Equal(1, reads);
        Assert.Equal(500, fork.WithWorkingLimit(2000).MaxWorkingBytes);
    }

    /// <summary>Reused and distinct passive declarations share only one build-local service.</summary>
    [Fact]
    public void ReusedDeclarationCreatesDistinctProcessorsAndOneService()
    {
        DrawingCompose.Definitions.CoverageMembershipFillPath declaration = DrawingCompose.CoverageMembershipFillPath();
        PipelineOccurrence2D[] placements = PipelineDeclaration2D.CaptureGraph([declaration, declaration, DrawingCompose.CoverageMembershipStrokePath()], "membership");
        var device = new ProfilingDevice();
        using var scope = new BackendCompileScope2D(device, null);
        CoverageMembershipProcessor2D first = Assert.IsType<CoverageMembershipProcessor2D>(scope.Resolve(placements[0]));
        CoverageMembershipProcessor2D second = Assert.IsType<CoverageMembershipProcessor2D>(scope.Resolve(placements[1]));
        CoverageMembershipProcessor2D third = Assert.IsType<CoverageMembershipProcessor2D>(scope.Resolve(placements[2]));
        Assert.NotSame(first, second);
        Assert.NotSame(second, third);
        Assert.Same(first.Service, second.Service);
        Assert.Same(first.Service, third.Service);
        Assert.Equal(0, device.ShaderCreations);
        Assert.Equal(0, device.NativeBuffers);
    }

    /// <summary>Every membership variant retains the independent 96-byte page ABI.</summary>
    /// <param name="target">The compiled shader target.</param>
    [Theory]
    [InlineData(ShaderTarget.SpirV)]
    [InlineData(ShaderTarget.Wgsl)]
    [InlineData(ShaderTarget.Dxil)]
    public void MembershipAbiIsCanonical(ShaderTarget target)
    {
        foreach (int mode in new[] { 8, 9, 10 })
        {
            string variant = mode == 9 ? "-membership-small" : mode == 10 ? "-membership-medium" : "-membership";
            DrawRecordAbi2D.Validate(ShaderArtifact.LoadEmbedded(typeof(Renderer2D).Assembly, "Lumyte.Drawing2D.draw-records-membership-vertex.lshader"), target, mode);
            DrawRecordAbi2D.Validate(ShaderArtifact.LoadEmbedded(typeof(Renderer2D).Assembly, "Lumyte.Drawing2D.draw-records" + variant + "-fragment.lshader"), target, mode);
        }
    }

    /// <summary>Admission rejects insufficient compute groups before acquiring resources.</summary>
    [Fact]
    public void UnsupportedComputeAdmissionDoesNotAllocate()
    {
        var device = new ProfilingDevice();
        using var state = new DrawingResourceState2D(device);
        using var plan = new DrawingArgumentPlan2D(device);
        var target = new DrawingTargetInfo2D { Description = new() { Width = 16, Height = 16, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment } };
        var context = new RenderPreparationContext2D(device, target, state, plan);
        Assert.False(CoverageMembershipAdmission2D.CanPrepare<FillPathCommand2D>(context, ReadOnlyMemory<FillPathCommand2D>.Empty));
        Assert.Equal(0, device.NativeBuffers);
        Assert.Equal(0, device.ShaderCreations);
        Assert.Equal(0, device.Writes);
    }

    /// <summary>Optional capacity rejection is false before buffers, shaders, or writes exist.</summary>
    /// <param name="maximum">The intentionally insufficient working-byte budget.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(8192)]
    public void WorkingBudgetRejectionIsPure(long maximum)
    {
        var device = new ProfilingDevice();
        using var state = new DrawingResourceState2D(device);
        using var plan = new DrawingArgumentPlan2D(device);
        var context = new RenderPreparationContext2D(device, new() { Description = new() { Width = 16, Height = 16, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment } }, state, plan) { MaxWorkingBytes = maximum };
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(0, 0, 8, 8), new SRgba<float>(1, 0, 0, 1));
        FillRectCommand2D[] commands = canvas.End().Commands.Cast<FillRectCommand2D>().ToArray();
        Assert.False(CoverageMembershipAdmission2D.CanPrepare<FillRectCommand2D>(context, commands));
        Assert.Equal(0, device.NativeBuffers);
        Assert.Equal(0, device.ShaderCreations);
        Assert.Equal(0, device.Writes);
    }

    /// <summary>One path group actually records compute before raster and cannot claim residency.</summary>
    /// <returns>The completed fake-device preparation.</returns>
    [Fact]
    public async Task PreparedPathGroupRecordsComputeBeforeRasterAsync()
    {
        var device = new ProfilingDevice { Caps = new() { ShaderTarget = ShaderTarget.SpirV, MaxBufferSize = 1UL << 30, MaxComputeInvocationsPerWorkgroup = 256, MaxComputeWorkgroupSizeX = 16, MaxComputeWorkgroupSizeY = 16, MaxComputeWorkgroupSizeZ = 1, MaxComputeWorkgroupsPerDimension = 65535 } };
        using var state = new DrawingResourceState2D(device);
        using var plan = new DrawingArgumentPlan2D(device);
        using var service = new CoverageMembershipService2D(device);
        using var target = new DispatchTexture { Width = 16, Height = 16, Format = TextureFormat.Rgba16Float };
        using IGraphicsTextureView view = target.CreateView();
        RenderPreparationContext2D context = new RenderPreparationContext2D(device, new() { Description = new() { Width = 16, Height = 16, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment } }, state, plan).WithShapeMembership(service);
        var path = new PathBuilder2D();
        path.MoveTo(new(1, 1));
        path.LineTo(new(12, 2));
        path.LineTo(new(2, 12));
        path.Close();
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillPath(path.Build(), new Lumyte.Mathematics.Colors.SRgba<float>(1, 0, 0, 1));
        DrawList2D list = canvas.End();
        FillPathCommand2D[] paths = list.Commands.Cast<FillPathCommand2D>().ToArray();
        Assert.True(CoverageMembershipAdmission2D.CanPrepare<FillPathCommand2D>(context, paths));
        using IPreparedCommandBatch2D prepared = await PreparedRecordBatch2D.PrepareAsync<FillPathCommand2D>(context, paths, default);
        long collectingReferences = plan.EstimatedBytes;
        Assert.True(plan.WorkingReferencesEnabled);
        Assert.True(collectingReferences >= 256 + ((7 + 1 + 2) * 256));
        Assert.True(Assert.IsAssignableFrom<IPreparedWorkingBudget2D>(prepared).EnforcesWorkingBudget);
        await plan.SealAsync();
        Assert.True(collectingReferences >= plan.EstimatedBytes);
        PreparedRecordBatch2D records = Assert.IsType<PreparedRecordBatch2D>(prepared);
        Assert.True(records.HasShapeMembership);
        using var commands = new ProfilingCommands();
        Assert.Throws<InvalidOperationException>(() => records.RecordRetained(commands, view, true));
        prepared.Record(commands, view);
        ShapeMembershipArguments2D firstArguments = Assert.Single(commands.Events.OfType<ShapeMembershipArguments2D>());
        using var secondPlan = new DrawingArgumentPlan2D(device);
        RenderPreparationContext2D secondContext = context.WithArgumentPlan(secondPlan).WithShapeMembership(service);
        using IPreparedCommandBatch2D secondPrepared = await PreparedRecordBatch2D.PrepareAsync<FillPathCommand2D>(secondContext, paths, default);
        await secondPlan.SealAsync();
        using var secondCommands = new ProfilingCommands();
        secondPrepared.Record(secondCommands, view);
        ShapeMembershipArguments2D secondArguments = Assert.Single(secondCommands.Events.OfType<ShapeMembershipArguments2D>());
        Assert.NotSame(((IShaderReference)firstArguments.Output).Resource, ((IShaderReference)secondArguments.Output).Resource);
        Assert.NotSame(((IShaderReference)firstArguments.Output).Table, ((IShaderReference)secondArguments.Output).Table);
        Assert.Single(commands.Events.OfType<ValueTuple<string, uint, uint, uint>>(), value => value.Item1 == "dispatch");
        Assert.True(commands.Events.FindIndex(value => value is ShapeMembershipArguments2D) < commands.Events.FindIndex(value => value is RecordedDrawRange));
    }

    /// <summary>All eleven generated factories use the common root and keep actual typed groups.</summary>
    /// <param name="computeSupported">Whether only the path slots can use membership compute.</param>
    /// <returns>The completed fake-device drawing.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MixedElevenKindsUseCommonRootAsync(bool computeSupported)
    {
        var device = new ProfilingDevice { Caps = new() { ShaderTarget = ShaderTarget.SpirV, MaxBufferSize = 1UL << 30, MaxComputeInvocationsPerWorkgroup = computeSupported ? 256u : 0u, MaxComputeWorkgroupSizeX = 16, MaxComputeWorkgroupSizeY = 16, MaxComputeWorkgroupSizeZ = 1, MaxComputeWorkgroupsPerDimension = 65535 } };
        using IGraphicsShader vertex = device.CreateShader(DrawingShaders2D.Vertex);
        using IGraphicsShader fragment = device.CreateShader(DrawingShaders2D.Fragment);
        using IGraphicsPipeline pipeline = device.CreateGraphicsPipeline(new() { VertexShader = vertex, FragmentShader = fragment });
        using RendererBuildHost2D host = Renderer(argumentTables: DrawingArgumentTableMode.Auto, retention: RetentionLimits(maxFramesInFlight: 2, maxCachedAssets: 32, maxCachedBytes: 64 * 1024 * 1024))[
            Renderer.FillRect()[CoverageMembershipFillRect()],
            Renderer.FillRoundedRect()[CoverageMembershipFillRoundedRect()],
            Renderer.FillCircle()[CoverageMembershipFillCircle()],
            Renderer.DrawLine()[CoverageMembershipDrawLine(), StandardDrawLine()],
            Renderer.FillPath()[CoverageMembershipFillPath(), StandardFillPath()],
            Renderer.StrokePath()[CoverageMembershipStrokePath(), StandardStrokePath()],
            Renderer.DrawImage()[CoverageMembershipDrawImage()],
            Renderer.DrawCoverageMask()[CoverageMembershipDrawCoverageMask()],
            Renderer.DrawDistanceField()[CoverageMembershipDrawDistanceField()],
            Renderer.DrawMesh()[CoverageMembershipDrawMesh()],
            Renderer.DrawPaintGraph()[CoverageMembershipDrawPaintGraph()]].CreateHost();
        using IRenderer2D renderer = host.BuildRenderer(device, new(device, pipeline), null, null);
        using var target = new DispatchTexture { Width = 16, Height = 16, Format = TextureFormat.Rgba16Float };
        using IGraphicsTextureView view = target.CreateView();
        using var image = new DispatchTexture();
        using IGraphicsTextureView imageView = image.CreateView();
        using var mask = new DispatchTexture { Format = TextureFormat.R8Unorm };
        using IGraphicsTextureView maskView = mask.CreateView();
        using var sampler = new DispatchSampler();
        var path = new PathBuilder2D();
        path.MoveTo(new(1, 1));
        path.LineTo(new(12, 2));
        path.LineTo(new(2, 12));
        path.Close();
        Path2D triangle = path.Build();
        var paint = PaintGraph2D.Solid(new SRgba<float>(1, 0, 0, 0.5f));
        var rect = new Rect2D(0, 0, 8, 8);
        var mesh = new Mesh2D([new MeshVertex2D(Vector2.Zero, 0.5f), new MeshVertex2D(new(8, 0), 1), new MeshVertex2D(new(0, 8), 1)], [0, 1, 2]);
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(rect, paint);
        canvas.FillRoundedRect(rect, 2, paint);
        canvas.FillCircle(new(4), 4, paint);
        canvas.DrawLine(new(1, 1), new(7, 6), paint, new() { Width = 2 });
        canvas.FillPath(triangle, paint);
        canvas.StrokePath(triangle, paint, new() { Width = 2 });
        canvas.DrawImage(new Image2D(imageView, sampler), rect, rect);
        canvas.DrawCoverageMask(new CoverageImage2D(maskView, sampler), rect, rect, paint);
        canvas.DrawDistanceField(new DistanceField2D(maskView, sampler, DistanceFieldEncoding2D.Sdf, 4), rect, rect, paint);
        canvas.DrawMesh(mesh, paint);
        canvas.DrawPaintGraph(paint);
        using PreparedDraw2D prepared = await renderer.PrepareAsync(canvas.End(), new() { View = view });
        using var commands = new ProfilingCommands();
        prepared.Record(commands);
        Assert.Equal(11, commands.Events.OfType<RecordedDrawRange>().Count());
        Assert.Equal(computeSupported ? 3 : 0, commands.Events.OfType<ShapeMembershipArguments2D>().Count());
        Assert.Equal(new uint[] { 1, 2, 3, 5, 4, 5, 1, 7, 8, 6, 9 }, device.Records.Select(record => record.Kind));
        var graphCanvas = new RenderGraphCanvas2D();
        graphCanvas.Begin();
        graphCanvas.FillPath(triangle, paint);
        using var graph = new Graph(device, "membership-common-root");
        var description = new TextureDesc { Width = 16, Height = 16, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.Sampled };
        Lumyte.Graphics.RenderGraph.RenderGraphTexture graphTarget = graph.CreateTexture(description, "target");
        using var graphRenderer = new RenderGraphRenderer2D(renderer, graphCanvas.End(), new() { Texture = graphTarget, Description = description, LoadOp = AttachmentLoadOp.Clear }, new(graph));
        using PreparedDrawingPasses2D drawing = await graph.Append2D(graphRenderer);
        graph.ExportTexture(graphTarget, TextureState.ColorAttachment, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite));
        using Lumyte.Graphics.RenderGraph.CompiledRenderGraph graphPlan = graph.Compile();
        using var graphCommands = new ProfilingCommands();
        graphPlan.Record(graphCommands);
        Assert.Equal(computeSupported ? 1 : 0, graphCommands.Events.OfType<ShapeMembershipArguments2D>().Count());
    }

    /// <summary>Every mask group reserves the full later binding generation, without counting its shared plan twice.</summary>
    /// <returns>The asynchronous fake-device materialization check.</returns>
    [Fact]
    public async Task MembershipGroupsReserveFullReferencesBeforeMaterializationAsync()
    {
        var device = new ProfilingDevice { Caps = new() { ShaderTarget = ShaderTarget.SpirV, MaxBufferSize = 1UL << 30, MaxComputeInvocationsPerWorkgroup = 256, MaxComputeWorkgroupSizeX = 16, MaxComputeWorkgroupSizeY = 16, MaxComputeWorkgroupSizeZ = 1, MaxComputeWorkgroupsPerDimension = 65535 } };
        using var resources = new DrawingResourceState2D(device);
        using var plan = new DrawingArgumentPlan2D(device);
        using var service = new CoverageMembershipService2D(device);
        var context = new RenderPreparationContext2D(device, new() { Description = new() { Width = 16, Height = 16, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment } }, resources, plan);
        var path = new PathBuilder2D();
        path.MoveTo(new(1, 1));
        path.LineTo(new(12, 2));
        path.LineTo(new(2, 12));
        path.Close();
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillPath(path.Build(), new SRgba<float>(1, 0, 0, 1));
        FillPathCommand2D[] commands = canvas.End().Commands.Cast<FillPathCommand2D>().ToArray();
        var batches = new List<IPreparedCommandBatch2D>();
        try
        {
            for (int i = 0; i < 128; i++)
            {
                context.SetWorkingBytesSource(() => DrawingWorkingBudget2D.OwnedBytes(batches, plan));
                batches.Add(await PreparedRecordBatch2D.PrepareAsync<FillPathCommand2D>(context.WithShapeMembership(service), commands, default));
            }

            Assert.Equal(0, context.OtherWorkingBytes);
            Assert.True(plan.WorkingReferencesEnabled);
            Assert.False(plan.Registrations.IsMaterialized);
            long collecting = plan.EstimatedBytes;
            Assert.True(collecting >= 256 + (128L * (7 + 1 + 2) * 256));
            await plan.SealAsync();
            Assert.True(collecting >= plan.EstimatedBytes);
            var owners = new Dictionary<object, long>(ReferenceEqualityComparer.Instance);
            Assert.All(batches, batch =>
            {
                Assert.True(Assert.IsAssignableFrom<IPreparedWorkingBudget2D>(batch).EnforcesWorkingBudget);
                Assert.IsAssignableFrom<IRetainedMemory2D>(batch).AccountMemory(owners);
            });
            Assert.Equal(129, owners.Count);
            Assert.Equal(plan.EstimatedBytes, owners[plan]);
            Assert.Equal(owners.Values.Sum(), DrawingWorkingBudget2D.OwnedBytes(batches));
        }
        finally
        {
            foreach (IPreparedCommandBatch2D batch in batches)
            {
                batch.Dispose();
            }
        }
    }

    /// <summary>The planner rounds genuine pixel bounds to 16x16 dispatch groups.</summary>
    [Fact]
    public void DispatchGroupsCoverIntegerBounds()
    {
        DeviceCaps caps = new() { MaxComputeInvocationsPerWorkgroup = 256, MaxComputeWorkgroupSizeX = 16, MaxComputeWorkgroupSizeY = 16, MaxComputeWorkgroupSizeZ = 1, MaxComputeWorkgroupsPerDimension = 65535 };
        ShapeMembership2D.Dispatch dispatch = ShapeMembership2D.Plan(3, new Vector4(2, 4, 19, 37), 7, caps);
        Assert.Equal(new ShapeMembership2D.Dispatch(3, 17, 33, 7, 2, 3), dispatch);
        Assert.Throws<NotSupportedException>(() => ShapeMembership2D.Plan(0, new Vector4(-1, 0, 1, 1), 0, caps));
    }
}
