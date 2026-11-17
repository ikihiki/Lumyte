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
        Definitions.MeshFillRect declaration = MeshFillRect(options: new(32, 4 * 1024 * 1024, 0));
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
        _ = configuration[Renderer.FillRect()[Array.Empty<IPipelineDeclaration2D<FillRectCommand2D>>()], Renderer.FillRect()[MeshFillRect(options: new(32, 4 * 1024 * 1024, 0)), StandardFillRect()]];
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

    private static Definitions.Renderer Configuration() => Renderer(
        argumentTables: DrawingArgumentTableMode.Auto,
        retention: RetentionLimits(maxFramesInFlight: 3, maxCachedAssets: 32, maxCachedBytes: 4 * 1024 * 1024))[
        Renderer.FillRect()[MeshFillRect(options: new(32, 4 * 1024 * 1024, 0))],
        Renderer.FillRoundedRect()[MeshFillRoundedRect(options: new(32, 4 * 1024 * 1024, 0))],
        Renderer.FillCircle()[MeshFillCircle(options: new(32, 4 * 1024 * 1024, 0))],
        Renderer.DrawLine()[MeshDrawLine(options: new(32, 4 * 1024 * 1024, 0))],
        Renderer.FillPath()[MeshFillPath(options: new(32, 4 * 1024 * 1024, 0))],
        Renderer.StrokePath()[MeshStrokePath(options: new(32, 4 * 1024 * 1024, 0))],
        Renderer.DrawImage()[MeshDrawImage(options: new(32, 4 * 1024 * 1024, 0))],
        Renderer.DrawCoverageMask()[MeshDrawCoverageMask(options: new(32, 4 * 1024 * 1024, 0))],
        Renderer.DrawDistanceField()[MeshDrawDistanceField(options: new(32, 4 * 1024 * 1024, 0))],
        Renderer.DrawMesh()[StandardDrawMesh()],
        Renderer.DrawPaintGraph()[MeshDrawPaintGraph(options: new(32, 4 * 1024 * 1024, 0))]];
}
