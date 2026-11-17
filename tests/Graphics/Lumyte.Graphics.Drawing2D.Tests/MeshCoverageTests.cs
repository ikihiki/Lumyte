using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.Pipelines;
using Lumyte.Graphics.Shared;
using Lumyte.Mathematics.Colors;
using Xunit;
using static Lumyte.Graphics.Drawing2D.Pipelines.DrawingCompose;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>CPU-only mesh admission, actual dispatch recording and public factory construction.</summary>
public sealed class MeshCoverageTests
{
    /// <summary>All eleven slots are explicit; unsupported indexed meshes stay on Standard.</summary>
    /// <returns>The fake-device recording verification.</returns>
    [Fact]
    public async Task PublicFactoriesRecordActualMeshTasksAsync()
    {
        var device = new ProfilingDevice { Caps = SupportedCaps() };
        using RendererBuildHost2D host = Configuration().CreateHost();
        IRenderer2D renderer = host.BuildRenderer(device, new(device, device.CreateGraphicsPipeline(new() { VertexShader = device.CreateShader(DrawingShaders2D.Vertex), FragmentShader = device.CreateShader(DrawingShaders2D.Fragment) })), null, null);
        using var target = new DispatchTexture();
        using IGraphicsTextureView view = target.CreateView();
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(1, 1, 8, 8), new SRgba<float>(1, 0, 0, 0.5f));
        canvas.FillRect(new(2, 2, 8, 8), new SRgba<float>(0, 1, 0, 0.5f));
        using PreparedDraw2D prepared = await renderer.PrepareAsync(canvas.End(), new RenderTarget2D { View = view });
        renderer.Dispose();
        using var commands = new ProfilingCommands();
        prepared.Record(commands);
        Assert.Equal(2, commands.Events.OfType<(string, uint, uint, uint)>().Count(item => item.Item1 == "mesh"));
        Assert.Empty(commands.Events.OfType<(uint, uint)>());
        Assert.Equal(1, device.MeshPipelineCreations);
        Assert.All(commands.Events.OfType<IMeshPipeline>(), pipeline => Assert.Null(pipeline.Desc.TaskShader));
        commands.Dispose();
    }

    /// <summary>Missing features, limits, local size and output capacities reject without native allocation.</summary>
    [Fact]
    public void UnsupportedCapsRejectBeforeAllocations()
    {
        var device = new ProfilingDevice();
        using var service = new MeshCoverageService2D(device, new(32, 4 * 1024 * 1024, 0));
        DeviceCaps caps = SupportedCaps();
        Assert.True(service.Supports(caps));
        Assert.False(service.Supports(caps with { Features = GraphicsFeatures.None }));
        Assert.False(service.Supports(caps with { MeshShaderLimits = null }));
        Assert.False(service.Supports(caps with { ShaderTarget = ShaderTarget.Wgsl }));
        Assert.False(service.Supports(caps with { MeshShaderLimits = caps.MeshShaderLimits! with { MaxOutputVertices = 3 } }));
        Assert.False(service.Supports(caps with { MeshShaderLimits = caps.MeshShaderLimits! with { MaxOutputMemoryBytes = 1 } }));
        Assert.False(service.Supports(caps with { MeshShaderLimits = caps.MeshShaderLimits! with { Workgroup = caps.MeshShaderLimits.Workgroup with { MaxInvocations = 0 } } }));
        Assert.Equal(0, device.ShaderCreations);
        Assert.Equal(0, device.NativeBuffers);
    }

    /// <summary>Both real offline targets retain bounded Mesh declarations and exact Fragment/root linkage.</summary>
    /// <param name="target">The offline target, not a native runtime claim.</param>
    [Theory]
    [InlineData(ShaderTarget.SpirV)]
    [InlineData(ShaderTarget.Dxil)]
    public void CompiledMeshMatchesRecordFragment(ShaderTarget target)
    {
        foreach ((int mode, string variant) in new[] { (0, "-basic"), (1, "-mask"), (2, "-image"), (3, string.Empty), (4, "-solid"), (6, "-image-small"), (7, "-image-medium") })
        {
            var mesh = ShaderArtifact.LoadEmbedded(typeof(DrawingShaders2D).Assembly, "Lumyte.Drawing2D.draw-records" + variant + "-mesh.lshader");
            var fragment = ShaderArtifact.LoadEmbedded(typeof(DrawingShaders2D).Assembly, "Lumyte.Drawing2D.draw-records" + variant + "-fragment.lshader");
            DrawRecordAbi2D.Validate(mesh, target, mode);
            DrawRecordAbi2D.Validate(fragment, target, mode);
            MeshShaderValidation.Program(mesh.GetTarget(target), null, fragment.GetTarget(target));
            Assert.Throws<NotSupportedException>(() => mesh.GetTarget(ShaderTarget.Wgsl));
        }
    }

    /// <summary>Compatible declarations share the real pipeline only within one build.</summary>
    /// <returns>The asynchronous preparation verification.</returns>
    [Fact]
    public async Task CompatibleKindsSharePipelineButBuildsDoNotAsync()
    {
        var device = new ProfilingDevice { Caps = SupportedCaps() };
        using RendererBuildHost2D host = Configuration().CreateHost();
        DrawingResources2D resources = new(device, device.CreateGraphicsPipeline(new() { VertexShader = device.CreateShader(DrawingShaders2D.Vertex), FragmentShader = device.CreateShader(DrawingShaders2D.Fragment) }));
        using IRenderer2D first = host.BuildRenderer(device, resources, null, null);
        using IRenderer2D second = host.BuildRenderer(device, resources, null, null);
        using var target = new DispatchTexture();
        using IGraphicsTextureView view = target.CreateView();
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(1, 1, 8, 8), new SRgba<float>(1, 0, 0, 1));
        canvas.FillCircle(new(8, 8), 4, new SRgba<float>(0, 1, 0, 1));
        DrawList2D content = canvas.End();
        using PreparedDraw2D firstPrepared = await first.PrepareAsync(content, new RenderTarget2D { View = view });
        Assert.Equal(1, device.MeshPipelineCreations);
        using PreparedDraw2D secondPrepared = await second.PrepareAsync(content, new RenderTarget2D { View = view });
        Assert.Equal(2, device.MeshPipelineCreations);
    }

    /// <summary>A sole Mesh candidate rejects before payload creation on unsupported devices.</summary>
    /// <returns>The early-rejection verification.</returns>
    [Fact]
    public async Task MeshOnlyUnsupportedRejectsEarlyAsync()
    {
        var device = new ProfilingDevice();
        using RendererBuildHost2D host = Configuration().CreateHost();
        DrawingResources2D resources = new(device, device.CreateGraphicsPipeline(new() { VertexShader = device.CreateShader(DrawingShaders2D.Vertex), FragmentShader = device.CreateShader(DrawingShaders2D.Fragment) }));
        using IRenderer2D renderer = host.BuildRenderer(device, resources, null, null);
        using var target = new DispatchTexture();
        using IGraphicsTextureView view = target.CreateView();
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(1, 1, 8, 8), new SRgba<float>(1, 0, 0, 1));
        DrawList2D content = canvas.End();
        await Assert.ThrowsAsync<NotSupportedException>(async () => await renderer.PrepareAsync(content, new RenderTarget2D { View = view }));
        Assert.Equal(0, device.MeshPipelineCreations);
        Assert.Equal(0, device.NativeBuffers);
    }

    /// <summary>One passive declaration can appear twice without interning processing placements.</summary>
    [Fact]
    public void ReusedDeclarationBuildsIndependentPlacements()
    {
        Definitions.Renderer configuration = Configuration();
        Definitions.MeshFillRect declaration = MeshFillRect(options: MeshCoverageLimits(maxCommands: 32, maxWorkingBytes: 4 * 1024 * 1024, numericPoolBytes: 0));
        _ = configuration[Renderer.FillRect()[Array.Empty<IPipelineDeclaration2D<FillRectCommand2D>>()]];
        _ = configuration[Renderer.FillRect()[declaration, declaration]];
        using RendererBuildHost2D host = configuration.CreateHost();
        var device = new ProfilingDevice();
        DrawingResources2D resources = new(device, device.CreateGraphicsPipeline(new() { VertexShader = device.CreateShader(DrawingShaders2D.Vertex), FragmentShader = device.CreateShader(DrawingShaders2D.Fragment) }));
        using IRenderer2D renderer = host.BuildRenderer(device, resources, null, null);
        Assert.Equal(0, device.MeshPipelineCreations);
    }

    /// <summary>Unsupported Mesh chooses only an explicitly supplied Standard candidate.</summary>
    /// <returns>The fake legacy drawing verification.</returns>
    [Fact]
    public async Task ExplicitFallbackRecordsNoMeshAsync()
    {
        Definitions.Renderer configuration = Configuration();
        _ = configuration[Renderer.FillRect()[Array.Empty<IPipelineDeclaration2D<FillRectCommand2D>>()], Renderer.FillRect()[MeshFillRect(options: MeshCoverageLimits(maxCommands: 32, maxWorkingBytes: 4 * 1024 * 1024, numericPoolBytes: 0)), StandardFillRect()]];
        using RendererBuildHost2D host = configuration.CreateHost();
        var device = new ProfilingDevice();
        DrawingResources2D resources = new(device, device.CreateGraphicsPipeline(new() { VertexShader = device.CreateShader(DrawingShaders2D.Vertex), FragmentShader = device.CreateShader(DrawingShaders2D.Fragment) }));
        using IRenderer2D renderer = host.BuildRenderer(device, resources, null, null);
        using var target = new DispatchTexture();
        using IGraphicsTextureView view = target.CreateView();
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(1, 1, 8, 8), new SRgba<float>(1, 0, 0, 1));
        using PreparedDraw2D prepared = await renderer.PrepareAsync(canvas.End(), new RenderTarget2D { View = view });
        using var commands = new ProfilingCommands();
        prepared.Record(commands);
        Assert.Equal(0, device.MeshPipelineCreations);
        Assert.Empty(commands.Events.OfType<(string, uint, uint, uint)>());
        Assert.NotEmpty(commands.Events.OfType<(uint, uint)>());
        commands.Dispose();
    }

    /// <summary>Only completed immutable Mesh owners share across overlapping frames; replacement is copy-on-write.</summary>
    /// <returns>The CPU fake-device lease proof.</returns>
    [Fact]
    public async Task ThreeMeshVersionsShareOnlyCompletedImmutablePayloadAsync()
    {
        var device = new ProfilingDevice { Caps = SupportedCaps() };
        Definitions.Renderer configuration = Configuration(resident: true);
        using RendererBuildHost2D host = configuration.CreateHost();
        DrawingResources2D resources = new(device, device.CreateGraphicsPipeline(new() { VertexShader = device.CreateShader(DrawingShaders2D.Vertex), FragmentShader = device.CreateShader(DrawingShaders2D.Fragment) }));
        using IRenderer2D renderer = host.BuildRenderer(device, resources, null, null);
        using var texture = new DispatchTexture { Width = 64, Height = 64 };
        using IGraphicsTextureView view = texture.CreateView();
        var target = new RenderTarget2D { View = view };
        using var scene = new DrawingScene2D();
        DrawingNodeId2D node = scene.Insert(scene.Root, 0, Content(0));
        using DrawingSnapshot2D snapshot = scene.CaptureSnapshot();
        using PreparedDrawingFrame2D warm = await renderer.PrepareAsync(snapshot, target);
        using var warmCommands = new ProfilingCommands();
        warm.Record(warmCommands);
        int allocations = device.NativeBuffers;
        using PreparedDrawingFrame2D unfinished = await renderer.PrepareAsync(snapshot, target);
        Assert.True(device.NativeBuffers > allocations);
        unfinished.Dispose();
        Complete(warm, warmCommands);
        allocations = device.NativeBuffers;
        using PreparedDrawingFrame2D old = await renderer.PrepareAsync(snapshot, target);
        using PreparedDrawingFrame2D same = await renderer.PrepareAsync(snapshot, target);
        Assert.Equal(allocations, device.NativeBuffers);
        scene.ReplaceContent(node, Content(1));
        using DrawingSnapshot2D changed = scene.CaptureSnapshot();
        using PreparedDrawingFrame2D replacement = await renderer.PrepareAsync(changed, target);
        Assert.True(device.NativeBuffers > allocations);
        using var oldCommands = new ProfilingCommands();
        using var sameCommands = new ProfilingCommands();
        using var replacementCommands = new ProfilingCommands();
        renderer.Dispose();
        old.Record(oldCommands);
        same.Record(sameCommands);
        replacement.Record(replacementCommands);
        Assert.DoesNotContain("shader-copy", oldCommands.Events);
        Assert.DoesNotContain("shader-copy", sameCommands.Events);
        Assert.Contains("shader-copy", replacementCommands.Events);
        Assert.Same(Assert.Single(oldCommands.Events.OfType<RecordedDrawRange>()).Records, Assert.Single(sameCommands.Events.OfType<RecordedDrawRange>()).Records);
        Assert.NotSame(Assert.Single(oldCommands.Events.OfType<RecordedDrawRange>()).Records, Assert.Single(replacementCommands.Events.OfType<RecordedDrawRange>()).Records);
        Complete(old, oldCommands);
        Complete(same, sameCommands);
        Complete(replacement, replacementCommands);
    }

    /// <summary>Exact integer translation updates only root properties consumed by both actual shader stages.</summary>
    /// <returns>The fake immutable root identity verification.</returns>
    [Fact]
    public async Task IntegerTranslationUsesMeshRootPropertiesAsync()
    {
        var device = new ProfilingDevice { Caps = SupportedCaps() };
        using RendererBuildHost2D host = Configuration(resident: true).CreateHost();
        DrawingResources2D resources = new(device, device.CreateGraphicsPipeline(new() { VertexShader = device.CreateShader(DrawingShaders2D.Vertex), FragmentShader = device.CreateShader(DrawingShaders2D.Fragment) }));
        using IRenderer2D renderer = host.BuildRenderer(device, resources, null, null);
        using var texture = new DispatchTexture { Width = 64, Height = 64 };
        using IGraphicsTextureView view = texture.CreateView();
        var target = new RenderTarget2D { View = view };
        using var scene = new DrawingScene2D();
        DrawingNodeId2D node = scene.Insert(scene.Root, 0, Content(0));
        using DrawingSnapshot2D snapshot = scene.CaptureSnapshot();
        using PreparedDrawingFrame2D warm = await renderer.PrepareAsync(snapshot, target);
        using var warmCommands = new ProfilingCommands();
        warm.Record(warmCommands);
        RecordedDrawRange original = Assert.Single(warmCommands.Events.OfType<RecordedDrawRange>());
        Complete(warm, warmCommands);
        int allocations = device.NativeBuffers;
        scene.SetTransform(node, System.Numerics.Matrix3x2.CreateTranslation(3, 4));
        using DrawingSnapshot2D moved = scene.CaptureSnapshot();
        using PreparedDrawingFrame2D frame = await renderer.PrepareAsync(moved, target);
        using var commands = new ProfilingCommands();
        frame.Record(commands);
        RecordedDrawRange translated = Assert.Single(commands.Events.OfType<RecordedDrawRange>());
        Assert.Same(original.Records, translated.Records);
        Assert.Equal(allocations, device.NativeBuffers);
        Assert.Equal(3f, BitConverter.UInt32BitsToSingle(translated.Reserved0));
        Assert.Equal(4f, BitConverter.UInt32BitsToSingle(translated.Reserved1));
        Assert.DoesNotContain("shader-copy", commands.Events);
        Complete(frame, commands);
    }

    /// <summary>Every declared Mesh kind consumes real numeric, paint, mask and image inputs; indexed Mesh stays explicit Standard.</summary>
    /// <returns>The CPU recording proof for the complete configured API.</returns>
    [Fact]
    public async Task EveryConfiguredLogicalKindConsumesActualPayloadAsync()
    {
        var device = new ProfilingDevice { Caps = SupportedCaps() };
        using RendererBuildHost2D host = Configuration().CreateHost();
        DrawingResources2D resources = new(device, device.CreateGraphicsPipeline(new() { VertexShader = device.CreateShader(DrawingShaders2D.Vertex), FragmentShader = device.CreateShader(DrawingShaders2D.Fragment) }));
        using IRenderer2D renderer = host.BuildRenderer(device, resources, null, null);
        using var target = new DispatchTexture();
        using IGraphicsTextureView view = target.CreateView();
        using var imageTexture = new DispatchTexture();
        using IGraphicsTextureView image = imageTexture.CreateView();
        using var maskTexture = new DispatchTexture { Format = TextureFormat.R8Unorm };
        using IGraphicsTextureView mask = maskTexture.CreateView();
        using var sampler = new DispatchSampler();
        var builder = new PathBuilder2D();
        builder.MoveTo(new(1, 1));
        builder.LineTo(new(10, 1));
        builder.LineTo(new(5, 10));
        builder.Close();
        Path2D path = builder.Build();
        var paint = PaintGraph2D.Solid(new SRgba<float>(1, 0, 0, 0.5f));
        var style = new StrokeStyle2D();
        var rect = new Rect2D(1, 1, 10, 10);
        var mesh = new Mesh2D([new MeshVertex2D(new(1, 1), 0.25f), new MeshVertex2D(new(10, 1), 0.5f), new MeshVertex2D(new(5, 10), 1)], [0, 1, 2]);
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(rect, paint);
        canvas.FillRoundedRect(rect, 2, paint);
        canvas.FillCircle(new(6, 6), 4, paint);
        canvas.DrawLine(new(1, 1), new(10, 10), paint, style);
        canvas.FillPath(path, paint);
        canvas.StrokePath(path, paint, style);
        canvas.DrawImage(new Image2D(image, sampler), rect, rect);
        canvas.DrawCoverageMask(new CoverageImage2D(mask, sampler), rect, rect, paint);
        canvas.DrawDistanceField(new DistanceField2D(mask, sampler, DistanceFieldEncoding2D.Sdf, 4), rect, rect, paint);
        canvas.DrawMesh(mesh, paint);
        canvas.DrawPaintGraph(paint);
        using PreparedDraw2D prepared = await renderer.PrepareAsync(canvas.End(), new RenderTarget2D { View = view });
        using var commands = new ProfilingCommands();
        prepared.Record(commands);
        Assert.True(commands.Events.OfType<(string, uint, uint, uint)>().Count(item => item.Item1 == "mesh") >= 10);
        Assert.NotEmpty(commands.Events.OfType<(uint, uint)>());
        Assert.Contains(device.Records, record => record.Kind == 7);
        Assert.Contains(device.Records, record => record.Kind == 8);
        commands.Dispose();
    }

    /// <summary>Different immutable capacities must not reuse another declaration's pipeline service.</summary>
    /// <returns>The actual fake pipeline creation check.</returns>
    [Fact]
    public async Task DifferentOptionsCreateDifferentBuildServicesAsync()
    {
        Definitions.Renderer configuration = Configuration();
        _ = configuration[Renderer.FillCircle()[Array.Empty<IPipelineDeclaration2D<FillCircleCommand2D>>()], Renderer.FillCircle()[MeshFillCircle(options: MeshCoverageLimits(maxCommands: 64, maxWorkingBytes: 4 * 1024 * 1024, numericPoolBytes: 0))]];
        using RendererBuildHost2D host = configuration.CreateHost();
        var device = new ProfilingDevice { Caps = SupportedCaps() };
        DrawingResources2D resources = new(device, device.CreateGraphicsPipeline(new() { VertexShader = device.CreateShader(DrawingShaders2D.Vertex), FragmentShader = device.CreateShader(DrawingShaders2D.Fragment) }));
        using IRenderer2D renderer = host.BuildRenderer(device, resources, null, null);
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(1, 1, 8, 8), new SRgba<float>(1, 0, 0, 1));
        canvas.FillCircle(new(8, 8), 4, new SRgba<float>(0, 1, 0, 1));
        using PreparedDraw2D prepared = await renderer.PrepareAsync(canvas.End(), new RenderTarget2D { View = view });
        Assert.Equal(2, device.MeshPipelineCreations);
    }

    /// <summary>Mixed intrinsic Standard and closed Mesh routes must both receive extracted root translation.</summary>
    /// <returns>The regression proof for mixed internal record provenance.</returns>
    [Fact]
    public async Task MixedStandardMeshTranslationReachesEveryRecordAsync()
    {
        Definitions.Renderer configuration = Configuration(resident: true);
        _ = configuration[Renderer.FillRect()[Array.Empty<IPipelineDeclaration2D<FillRectCommand2D>>()], Renderer.FillRect()[StandardFillRect()]];
        using RendererBuildHost2D host = configuration.CreateHost();
        var device = new ProfilingDevice { Caps = SupportedCaps() };
        DrawingResources2D resources = new(device, device.CreateGraphicsPipeline(new() { VertexShader = device.CreateShader(DrawingShaders2D.Vertex), FragmentShader = device.CreateShader(DrawingShaders2D.Fragment) }));
        using IRenderer2D renderer = host.BuildRenderer(device, resources, null, null);
        using var texture = new DispatchTexture { Width = 64, Height = 64 };
        using IGraphicsTextureView view = texture.CreateView();
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(4, 4, 8, 8), new SRgba<float>(1, 0, 0, 0.5f));
        canvas.FillCircle(new(12, 12), 4, new SRgba<float>(0, 1, 0, 0.5f));
        using var scene = new DrawingScene2D();
        DrawingNodeId2D node = scene.Insert(scene.Root, 0, canvas.End());
        scene.SetTransform(node, System.Numerics.Matrix3x2.CreateTranslation(3, 4));
        using DrawingSnapshot2D snapshot = scene.CaptureSnapshot();
        using PreparedDrawingFrame2D frame = await renderer.PrepareAsync(snapshot, new RenderTarget2D { View = view });
        using var commands = new ProfilingCommands();
        frame.Record(commands);
        RecordedDrawRange[] roots = commands.Events.OfType<RecordedDrawRange>().ToArray();
        Assert.Equal(2, roots.Length);
        Assert.All(roots, root => Assert.Equal(3f, BitConverter.UInt32BitsToSingle(root.Reserved0)));
        Assert.All(roots, root => Assert.Equal(4f, BitConverter.UInt32BitsToSingle(root.Reserved1)));
        Assert.Single(commands.Events.OfType<(string, uint, uint, uint)>(), item => item.Item1 == "mesh");
        Assert.NotEmpty(commands.Events.OfType<(uint, uint)>());
        commands.Dispose();
        frame.AbortUnsubmitted();
    }

    private static DrawList2D Content(int variant)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(4, 4, 8, 8), PaintGraph2D.Solid(new SRgba<float>(1, variant, 0, 0.5f)));
        return canvas.End();
    }

    private static void Complete(PreparedDrawingFrame2D frame, ProfilingCommands commands)
    {
        commands.Submit();
        frame.AttachSubmission(new CompletedSubmission());
        commands.Dispose();
        frame.Retire();
    }

    private static DeviceCaps SupportedCaps() => new()
    {
        Features = GraphicsFeatures.MeshShader,
        ShaderTarget = ShaderTarget.SpirV,
        MaxTextureDimension2D = 4096,
        MaxColorAttachments = 8,
        MaxBufferSize = 1UL << 30,
        MeshShaderLimits = new()
        {
            Workgroup = new() { MaxSizeX = 128, MaxSizeY = 128, MaxSizeZ = 128, MaxInvocations = 128, MaxGroupCountX = 65535, MaxGroupCountY = 65535, MaxGroupCountZ = 65535, MaxTotalGroupCount = 65535 },
            MaxOutputVertices = 256,
            MaxOutputPrimitives = 256,
            MaxOutputMemoryBytes = 32768,
            MaxPayloadAndOutputMemoryBytes = 32768,
        },
    };

    private static Definitions.Renderer Configuration(bool resident = false) => Renderer(
        argumentTables: DrawingArgumentTableMode.Auto,
        retention: resident ? RetentionGpuLimits(3, 32, 4 * 1024 * 1024, 16 * 1024 * 1024) : RetentionLimits(maxFramesInFlight: 3, maxCachedAssets: 32, maxCachedBytes: 4 * 1024 * 1024))[
        Renderer.FillRect()[MeshFillRect(options: MeshCoverageLimits(maxCommands: 32, maxWorkingBytes: 4 * 1024 * 1024, numericPoolBytes: 0))],
        Renderer.FillRoundedRect()[MeshFillRoundedRect(options: MeshCoverageLimits(maxCommands: 32, maxWorkingBytes: 4 * 1024 * 1024, numericPoolBytes: 0))],
        Renderer.FillCircle()[MeshFillCircle(options: MeshCoverageLimits(maxCommands: 32, maxWorkingBytes: 4 * 1024 * 1024, numericPoolBytes: 0))],
        Renderer.DrawLine()[MeshDrawLine(options: MeshCoverageLimits(maxCommands: 32, maxWorkingBytes: 4 * 1024 * 1024, numericPoolBytes: 0))],
        Renderer.FillPath()[MeshFillPath(options: MeshCoverageLimits(maxCommands: 32, maxWorkingBytes: 4 * 1024 * 1024, numericPoolBytes: 0))],
        Renderer.StrokePath()[MeshStrokePath(options: MeshCoverageLimits(maxCommands: 32, maxWorkingBytes: 4 * 1024 * 1024, numericPoolBytes: 0))],
        Renderer.DrawImage()[MeshDrawImage(options: MeshCoverageLimits(maxCommands: 32, maxWorkingBytes: 4 * 1024 * 1024, numericPoolBytes: 0))],
        Renderer.DrawCoverageMask()[MeshDrawCoverageMask(options: MeshCoverageLimits(maxCommands: 32, maxWorkingBytes: 4 * 1024 * 1024, numericPoolBytes: 0))],
        Renderer.DrawDistanceField()[MeshDrawDistanceField(options: MeshCoverageLimits(maxCommands: 32, maxWorkingBytes: 4 * 1024 * 1024, numericPoolBytes: 0))],
        Renderer.DrawMesh()[StandardDrawMesh()],
        Renderer.DrawPaintGraph()[MeshDrawPaintGraph(options: MeshCoverageLimits(maxCommands: 32, maxWorkingBytes: 4 * 1024 * 1024, numericPoolBytes: 0))]];

    private sealed class CompletedSubmission : IGraphicsSubmission
    {
        public SubmissionStatus Status => SubmissionStatus.Completed;

        public ValueTask WaitAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }
}
