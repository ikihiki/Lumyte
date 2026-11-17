using Lumyte.Graphics.Drawing2D.Pipelines;
using Lumyte.Mathematics.Colors;
using Xunit;
using static Lumyte.Graphics.Drawing2D.Pipelines.DrawingCompose;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Exercises the actual sparse public factories and CPU geometry cache.</summary>
public sealed partial class CompositionMigrationTests
{
    /// <summary>Passive reuse creates independent placements without eager GPU allocation.</summary>
    [Fact]
    public void SparseFactoryIsPassiveAndRepeatedPlacementsBuild()
    {
        Definitions.SparseFillRect cfg = SparseFillRect(CpuSparse(1024 * 1024, 8, 65536, 262144));
        Definitions.Renderer definition = StandardDefinition();
        _ = definition[Renderer.FillRect()[Array.Empty<IPipelineDeclaration2D<FillRectCommand2D>>()], Renderer.FillRect()[cfg, cfg]];
        Assert.Equal(11, definition.DescribeDataflow().Count);
        var device = new ProfilingDevice();
        using RendererBuildHost2D host = definition.CreateHost();
        DrawingResources2D resources = Resources(device);
        using IRenderer2D first = host.BuildRenderer(device, resources, null, null);
        using IRenderer2D second = host.BuildRenderer(device, resources, null, null);
        Assert.NotSame(first, second);
        Assert.Equal(0, device.NativeBuffers);
    }

    /// <summary>Actual sparse declarations share only equal build-local services, never placements.</summary>
    [Fact]
    public void SparsePlacementsShareOnlyCompatibleBuildLocalGeometry()
    {
        var device = new ProfilingDevice();
        Definitions.SparseFillRect cfg = SparseFillRect(CpuSparse(1024 * 1024, 8, 65536, 262144));
        PipelineOccurrence2D[] positions = PipelineDeclaration2D.CaptureGraph([cfg, cfg], "sparse");
        using var scope = new BackendCompileScope2D(device, null);
        SparseFillRectElement2D first = Assert.IsType<SparseFillRectElement2D>(scope.Resolve(positions[0]));
        SparseFillRectElement2D second = Assert.IsType<SparseFillRectElement2D>(scope.Resolve(positions[1]));
        Assert.NotSame(first, second);
        Assert.Same(first.Service, second.Service);
        Definitions.SparseFillRect otherCfg = SparseFillRect(CpuSparse(1024 * 1024, 4, 65536, 262144));
        PipelineOccurrence2D other = Assert.Single(PipelineDeclaration2D.CaptureGraph([otherCfg], "other"));
        Assert.NotSame(first.Service, Assert.IsType<SparseFillRectElement2D>(scope.Resolve(other)).Service);
        using var otherBuild = new BackendCompileScope2D(device, null);
        Assert.NotSame(first.Service, Assert.IsType<SparseFillRectElement2D>(otherBuild.Resolve(positions[0])).Service);
        Assert.Equal(0, device.NativeBuffers);
        Assert.Equal(0, device.ShaderCreations);
    }

    /// <summary>The public factory reaches the actual CPU strip lowerer and GPU paint payload.</summary>
    /// <returns>The completed fake-device preparation.</returns>
    [Fact]
    public async Task SparsePublicFactoryPreparesActualStripsAsync()
    {
        Definitions.Renderer definition = StandardDefinition();
        _ = definition[Renderer.FillRect()[Array.Empty<IPipelineDeclaration2D<FillRectCommand2D>>()], Renderer.FillRect()[SparseFillRect(CpuSparse(1024 * 1024, 8, 65536, 262144))]];
        var device = new ProfilingDevice();
        using RendererBuildHost2D host = definition.CreateHost();
        using IRenderer2D renderer = host.BuildRenderer(device, Resources(device), null, null);
        using var texture = new DispatchTexture();
        using Abstractions.IGraphicsTextureView view = texture.CreateView();
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(0.125f, 0.125f, 2, 2), SRgba<float>.Red);
        using PreparedDraw2D prepared = await renderer.PrepareAsync(canvas.End(), new() { View = view });
        Assert.True(device.NativeBuffers > 0);
        Assert.NotEmpty(device.Records);
        Assert.All(device.Records, record => Assert.Equal(9u, record.Kind));
    }

    /// <summary>Sparse mesh payload preserves original center-interpolated coverage geometry.</summary>
    /// <returns>The completed CPU fake-device preparation.</returns>
    [Fact]
    public async Task SparseMeshRetainsActualGeometryAsync()
    {
        var device = new ProfilingDevice();
        using var resources = new DrawingResourceState2D(device);
        using var arguments = new DrawingArgumentPlan2D(device);
        var target = new DrawingTargetInfo2D { Description = new() { Width = 8, Height = 8, Format = Abstractions.TextureFormat.Rgba16Float, Usage = Abstractions.TextureUsage.RenderAttachment } };
        var context = new RenderPreparationContext2D(device, target, resources, arguments);
        var mesh = new Mesh2D([new(new(0, 0), 0), new(new(8, 0), 1), new(new(0, 8), 0.5f)], [0, 1, 2]);
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.DrawMesh(mesh, PaintGraph2D.Solid(SRgba<float>.Red));
        SparseStripPlan2D plan = SparseStripBuilder2D.Create(canvas.End(), target);
        using PreparedSparseStripBatch2D batch = await PreparedSparseStripBatch2D.PrepareAsync(context, plan, default);
        await arguments.SealAsync();
        Assert.NotEmpty(device.Records);
        Assert.All(device.Records, record =>
        {
            Assert.Equal(6u, record.Kind);
            Assert.Equal(1u, record.PayloadCount);
        });
    }

    /// <summary>Shape reuse rebinds paint and returns private mutable arrays.</summary>
    [Fact]
    public void SparseCacheRebindsCurrentPaintAndKeepsMasksPrivate()
    {
        var cache = new SparseGeometryCache2D(1024 * 1024, 8, 65536, 262144);
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(0.125f, 0.125f, 0.5f, 0.5f), SRgba<float>.Red);
        DrawList2D red = canvas.End();
        canvas.Begin();
        canvas.FillRect(new(0.125f, 0.125f, 0.5f, 0.5f), SRgba<float>.Blue);
        DrawList2D blue = canvas.End();
        var target = new DrawingTargetInfo2D { Description = new() { Width = 8, Height = 8, Format = Abstractions.TextureFormat.Rgba16Float, Usage = Abstractions.TextureUsage.RenderAttachment } };
        SparseStripPlan2D first = cache.GetSparse(red, target, 128 * 1024 * 1024);
        Assert.Equal((ushort)0x0033, Assert.Single(first.BoundaryMasks));
        first.BoundaryMasks[0] = 0;
        SparseStripPlan2D next = cache.GetSparse(blue, target, 128 * 1024 * 1024);
        Assert.Equal((ushort)0x0033, Assert.Single(next.BoundaryMasks));
        Assert.Same(blue.Commands[0], Assert.Single(next.Commands));
        Assert.Equal(1, cache.SparseBuildCount);
        cache.Clear();
        Assert.Equal(0, cache.AssetCount);
        Assert.Equal(0, cache.EstimatedBytes);
    }

    /// <summary>Explicit limits reject before output buffers or native resources are created.</summary>
    [Fact]
    public void SparseOutputLimitsAreNotClamped()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CpuSparse(-1, 1, 1, 1));
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(0, 0, 8, 8), SRgba<float>.Red);
        var target = new DrawingTargetInfo2D { Description = new() { Width = 8, Height = 8, Format = Abstractions.TextureFormat.Rgba16Float, Usage = Abstractions.TextureUsage.RenderAttachment } };
        Assert.Throws<NotSupportedException>(() => SparseStripBuilder2D.Create(canvas.End(), target, maxStrips: 1));
    }
}
