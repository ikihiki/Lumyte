using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Graphics.ImageEffects;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Mathematics.Colors;
using Xunit;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks the strategy-independent parent-graph image boundary.</summary>
public sealed class RendererImageTests
{
    /// <summary>An external direct-only renderer is rejected before graph mutation or direct preparation.</summary>
    [Fact]
    public void UnsupportedRendererLeavesGraphUnchanged()
    {
        var device = new ProfilingDevice();
        using var graph = new Graph(device);
        using var renderer = new DirectOnlyRenderer(device);
        Assert.Throws<NotSupportedException>(() => graph.Append2D(renderer, EmptyContent(), Options()));
        using CompiledRenderGraph compiled = graph.Compile();
        Assert.Empty(compiled.PassNames);
        Assert.Equal(0, compiled.Statistics.Resources);
    }

    /// <summary>Bounds and DPR reach target preparation unchanged and empty content still clears.</summary>
    /// <returns>The asynchronous preparation completion.</returns>
    [Fact]
    public async Task EmptyContentProducesTransparentFiniteImageAsync()
    {
        var device = new ProfilingDevice();
        using var graph = new Graph(device);
        using var renderer = new CapturingRenderer(device);
        DrawingImageOptions2D options = Options() with { PixelScale = 1.5f };
        EffectImage image = await graph.Append2D(renderer, EmptyContent(), options);
        Assert.Equal(options.Bounds, image.Bounds);
        Assert.Equal(EffectColorSemantic.DisplayLinear, image.ColorSemantic);
        Assert.Equal(new Vector2(-7, 11), renderer.Target!.PixelOrigin);
        Assert.Equal(1.5f, renderer.Target.PixelScale);
        Assert.Equal(TextureFormat.Rgba16Float, renderer.Target.Description.Format);
        Assert.Equal(TextureUsage.RenderAttachment | TextureUsage.Sampled, renderer.Target.Description.Usage);
        Assert.True(renderer.DeferredArguments);
        graph.MarkOutput(image.Texture);
        renderer.Dispose();
        using CompiledRenderGraph compiled = graph.Compile();
        graph.Dispose();
        Assert.Contains("drawing2d.image.clear", compiled.PassNames);
        using var commands = new ProfilingCommands();
        compiled.Record(commands);
        commands.Dispose();
    }

    /// <summary>The common contract rejects a foreign device before preparing or changing the graph.</summary>
    [Fact]
    public void DeviceMismatchLeavesGraphUnchanged()
    {
        using var graph = new Graph(new ProfilingDevice());
        using var renderer = new CapturingRenderer(new ProfilingDevice());
        Assert.Throws<ArgumentException>(() => graph.Append2D(renderer, EmptyContent(), Options()));
        Assert.Null(renderer.Target);
        using CompiledRenderGraph compiled = graph.Compile();
        Assert.Empty(compiled.PassNames);
        Assert.Equal(0, compiled.Statistics.Resources);
    }

    /// <summary>Invalid dimensions and DPR are rejected before preparing a backend.</summary>
    /// <param name="scale">The invalid scale.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void InvalidScaleLeavesGraphUnchanged(float scale)
    {
        var device = new ProfilingDevice();
        using var graph = new Graph(device);
        using var renderer = new CapturingRenderer(device);
        Assert.Throws<ArgumentOutOfRangeException>(() => graph.Append2D(renderer, EmptyContent(), Options() with { PixelScale = scale }));
        Assert.Throws<ArgumentOutOfRangeException>(() => graph.Append2D(renderer, EmptyContent(), Options() with { Bounds = new(0, 0, 0, 8) }));
        Assert.Null(renderer.Target);
        using CompiledRenderGraph compiled = graph.Compile();
        Assert.Empty(compiled.PassNames);
    }

    /// <summary>Real prepared drawing retains backend resources after renderer disposal.</summary>
    /// <returns>The asynchronous preparation and record completion.</returns>
    [Fact]
    public async Task BuiltinRendererCanBeDisposedBeforeGraphRecordAsync()
    {
        var device = new ProfilingDevice();
        using var graph = new Graph(device);
        using IRenderer2D renderer = Renderer2D.CreateDefault(device);
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillCircle(new(8), 3, new SRgba<float>(1, 0, 0, 1));
        EffectImage image = await graph.Append2D(renderer, canvas.End(), Options());
        renderer.Dispose();
        graph.MarkOutput(image.Texture);
        using CompiledRenderGraph compiled = graph.Compile();
        graph.Dispose();
        using var commands = new ProfilingCommands();
        compiled.Record(commands);
        Assert.NotEmpty(device.Tables);
        commands.Dispose();
    }

    /// <summary>Each backend exposes distinct graph stages and drops all work for unused output.</summary>
    /// <param name="strategy">The explicit backend selection.</param>
    /// <returns>The completed graph preparation.</returns>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task BackendStagesRemainExplicitAndCullAsync(int strategy)
    {
        var device = new ProfilingDevice { Caps = new() { ShaderTarget = ShaderTarget.SpirV, MaxBufferSize = 1UL << 30, MaxTextureDimension2D = 4096, MaxComputeInvocationsPerWorkgroup = 256, MaxComputeWorkgroupSizeX = 16, MaxComputeWorkgroupSizeY = 16, MaxComputeWorkgroupSizeZ = 1, MaxComputeWorkgroupsPerDimension = 65535 } };
        using IRenderer2D renderer = strategy switch { 0 => Renderer2D.CreateDefault(device), 1 => new SparseStripRenderer2D(device), _ => new ComputeRenderer2D(device) };
        var canvas = new Canvas2D();
        canvas.Begin();
        var path = new PathBuilder2D();
        path.MoveTo(new(0, 0));
        path.LineTo(new(4, 0));
        path.LineTo(new(2, 4));
        path.Close();
        canvas.FillPath(path.Build(), SRgba<float>.Red);
        DrawList2D content = canvas.End();
        using var unused = new Graph(device);
        await unused.Append2D(renderer, content, new() { Bounds = new(0, 0, 8, 8) });
        using CompiledRenderGraph culled = unused.Compile();
        Assert.Empty(culled.PassNames);
        using var graph = new Graph(device);
        EffectImage image = await graph.Append2D(renderer, content, new() { Bounds = new(0, 0, 8, 8) });
        graph.MarkOutput(image.Texture);
        renderer.Dispose();
        using CompiledRenderGraph compiled = graph.Compile();
        Assert.Contains("drawing2d.image.clear", compiled.PassNames);
        Assert.Contains(strategy switch { 0 => "drawing2d.shape-membership", 1 => "drawing2d.sparse-strips", _ => "drawing2d.tiled.shade" }, compiled.PassNames);
        using var commands = new ProfilingCommands();
        compiled.Record(commands);
        commands.Dispose();
    }

    private static DrawingImageOptions2D Options() => new() { Bounds = new(-7, 11, 24, 16) };

    private static DrawList2D EmptyContent()
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        return canvas.End();
    }

    private class DirectOnlyRenderer(IGraphicDevice device) : IRenderer2D
    {
        public IGraphicDevice Device { get; } = device;

        public DrawingRetentionStatistics2D RetentionStatistics => default;

        public ValueTask<PreparedDraw2D> PrepareAsync(DrawList2D list, RenderTarget2D target, CancellationToken cancellationToken = default) => throw new InvalidOperationException("The adapter must not invoke direct preparation.");

        public ValueTask<PreparedDrawingFrame2D> PrepareAsync(DrawingSceneReadLease2D source, RenderTarget2D target, CancellationToken cancellationToken = default) => throw new InvalidOperationException();

        public ValueTask<PreparedDrawingFrame2D> PrepareAsync(DrawingSnapshot2D source, RenderTarget2D target, CancellationToken cancellationToken = default) => throw new InvalidOperationException();

        public void Dispose()
        {
        }
    }

    private sealed class CapturingRenderer(IGraphicDevice device) : DirectOnlyRenderer(device), IDrawingPassRenderer2D
    {
        internal DrawingTargetInfo2D? Target { get; private set; }

        internal bool DeferredArguments { get; private set; }

        public ValueTask<DrawingPassPlan2D> PreparePassesAsync(DrawList2D content, DrawingTargetInfo2D target, DrawingArgumentPlan2D argumentPlan, CancellationToken cancellationToken)
        {
            Target = target;
            DeferredArguments = argumentPlan.DeferredMaterialization;
            return ValueTask.FromResult(new DrawingPassPlan2D(argumentPlan));
        }

        public ValueTask<DrawingPassPlan2D> PreparePassesAsync(DrawingSceneReadLease2D source, DrawingTargetInfo2D target, DrawingArgumentPlan2D argumentPlan, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<DrawingPassPlan2D> PreparePassesAsync(DrawingSnapshot2D source, DrawingTargetInfo2D target, DrawingArgumentPlan2D argumentPlan, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
