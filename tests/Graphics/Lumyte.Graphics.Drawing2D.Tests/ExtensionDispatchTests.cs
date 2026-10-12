using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Graphics.RenderGraph;
using Xunit;
using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks external typed renderer dispatch and graph ownership without a GPU backend.</summary>
public sealed class ExtensionDispatchTests
{
    /// <summary>Checks every public drawing command reaches its dedicated interface in painter order.</summary>
    /// <returns>The asynchronous test completion.</returns>
    [Fact]
    public async Task EveryCommandUsesItsDedicatedRendererInRecordedOrderAsync()
    {
        var strategies = new DispatchRenderers();
        using var renderer = new Renderer2D(new DispatchDevice(), strategies.CreateTable());
        using var target = new DispatchTexture();
        using IGraphicsTextureView targetView = target.CreateView();
        using var imageTexture = new DispatchTexture();
        using IGraphicsTextureView imageView = imageTexture.CreateView();
        using var coverageTexture = new DispatchTexture { Format = TextureFormat.R8Unorm };
        using IGraphicsTextureView coverageView = coverageTexture.CreateView();
        using var sampler = new DispatchSampler();
        var canvas = new Canvas2D();
        Path2D path = Triangle();
        var rect = new Rect2D(0, 0, 8, 8);
        var color = new Color2D(1, 0, 0, 0.5f);
        var paint = PaintGraph2D.Solid(color);
        var style = new StrokeStyle2D();
        var mesh = new Mesh2D(
            [new MeshVertex2D(Vector2.Zero, 1), new MeshVertex2D(new Vector2(8, 0), 1), new MeshVertex2D(new Vector2(0, 8), 1)],
            [0, 1, 2]);
        canvas.Begin();
        canvas.FillRect(rect, color);
        canvas.FillRect(rect with { X = 1 }, paint);
        canvas.FillRoundedRect(rect, 2, paint);
        canvas.FillCircle(new Vector2(4), 4, paint);
        canvas.DrawLine(Vector2.Zero, new Vector2(8), paint, style);
        canvas.FillPath(path, paint);
        canvas.StrokePath(path, paint, style);
        canvas.DrawImage(new Image2D(imageView, sampler), rect, rect);
        canvas.DrawCoverageMask(new CoverageImage2D(coverageView, sampler), rect, rect, paint);
        canvas.DrawDistanceField(new DistanceField2D(coverageView, sampler, DistanceFieldEncoding2D.Sdf, 4), rect, rect, paint);
        canvas.DrawMesh(mesh, paint);
        canvas.DrawPaintGraph(paint);
        canvas.FillRect(rect with { X = 2 }, color);

        using PreparedDraw2D prepared = await renderer.PrepareAsync(canvas.End(), new RenderTarget2D { View = targetView });
        Type[] expected =
        [
            typeof(FillRectCommand2D), typeof(FillRectCommand2D), typeof(FillRoundedRectCommand2D),
            typeof(FillCircleCommand2D), typeof(DrawLineCommand2D), typeof(FillPathCommand2D),
            typeof(StrokePathCommand2D), typeof(DrawImageCommand2D), typeof(DrawCoverageMaskCommand2D),
            typeof(DrawDistanceFieldCommand2D), typeof(DrawMeshCommand2D), typeof(DrawPaintGraphCommand2D),
            typeof(FillRectCommand2D),
        ];
        Assert.Equal(expected, strategies.Commands.Select(command => command.GetType()));
        Assert.Equal(12, strategies.Batches.Count);
        Assert.All(strategies.Batches, batch => Assert.Equal(0, batch.DisposeCount));
        using var commands = new DispatchCommands();
        prepared.Record(commands);
        Assert.Equal(
            expected.Where((type, index) => index == 0 || type != expected[index - 1]).Select(type => type.Name + ".draw"),
            strategies.Recorded);
    }

    /// <summary>Checks replacing FillPath does not reroute StrokePath or a paint graph containing a path.</summary>
    /// <returns>The asynchronous test completion.</returns>
    [Fact]
    public async Task FillPathReplacementDoesNotCaptureOtherCommandsAsync()
    {
        var normal = new DispatchRenderers();
        var replacement = new DispatchRenderers();
        using var renderer = new Renderer2D(new DispatchDevice(), normal.CreateTable(replacement));
        using var target = new DispatchTexture();
        using IGraphicsTextureView view = target.CreateView();
        Path2D path = Triangle();
        var paint = PaintGraph2D.Solid(new Color2D(0, 1, 0, 1));
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillPath(path, paint);
        canvas.StrokePath(path, paint, new StrokeStyle2D());
        canvas.DrawPaintGraph(PaintGraph2D.Clip(path, paint));

        using PreparedDraw2D prepared = await renderer.PrepareAsync(canvas.End(), new RenderTarget2D { View = view });
        Assert.IsType<FillPathCommand2D>(Assert.Single(replacement.Commands));
        Assert.Collection(
            normal.Commands,
            command => Assert.IsType<StrokePathCommand2D>(command),
            command => Assert.IsType<DrawPaintGraphCommand2D>(command));
    }

    /// <summary>Checks compatible batches cannot cross a clip change.</summary>
    /// <returns>The asynchronous test completion.</returns>
    [Fact]
    public async Task ClipChangesSplitContiguousBatchesAsync()
    {
        var strategies = new DispatchRenderers();
        using var renderer = new Renderer2D(new DispatchDevice(), strategies.CreateTable());
        using var target = new DispatchTexture();
        using IGraphicsTextureView view = target.CreateView();
        var canvas = new Canvas2D();
        var rect = new Rect2D(0, 0, 8, 8);
        var color = new Color2D(1, 1, 1, 1);
        canvas.Begin();
        canvas.FillRect(rect, color);
        canvas.FillRect(rect, color);
        canvas.ClipRect(new Rect2D(1, 1, 4, 4));
        canvas.FillRect(rect, color);

        using PreparedDraw2D prepared = await renderer.PrepareAsync(canvas.End(), new RenderTarget2D { View = view });
        Assert.Equal(3, strategies.Commands.Count);
        Assert.Equal(2, strategies.Batches.Count);
        Assert.Same(strategies.Commands[0].State.Clip, strategies.Commands[1].State.Clip);
        Assert.NotSame(strategies.Commands[1].State.Clip, strategies.Commands[2].State.Clip);
    }

    /// <summary>Checks external batches expose multiple ordered passes and let the graph insert transitions.</summary>
    /// <returns>The asynchronous test completion.</returns>
    [Fact]
    public async Task ExternalRendererCanAppendMultiplePassesWithGraphBarriersAsync()
    {
        var device = new DispatchDevice();
        var strategies = new DispatchRenderers { MultiplePasses = true };
        var graph = new RenderGraphBuilder(device);
        using var texture = new DispatchTexture();
        (RenderTargetBinding2D target, DrawingGraphBindings2D bindings) = BindTarget(graph, texture);
        using var renderer = new RenderGraphRenderer2D(strategies.CreateTable(), RectList(), target, bindings);
        using PreparedDrawingPasses2D prepared = await graph.Append2D(renderer);
        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new DispatchCommands();
        plan.Record(commands);

        Assert.Equal(["FillRectCommand2D.prepare", "FillRectCommand2D.draw"], strategies.Recorded);
        TextureBarrierDesc[] barriers = commands.Events.OfType<TextureBarrierDesc>().ToArray();
        Assert.Single(commands.Events.OfType<MemoryBarrierDesc>());
        Assert.All(barriers, barrier => Assert.Equal(TextureState.ColorAttachment, barrier.AfterState));
        Assert.Equal(CommandBufferState.Recording, commands.State);
    }

    /// <summary>Checks a completed batch is released when a later renderer fails during preparation.</summary>
    /// <returns>The asynchronous test completion.</returns>
    [Fact]
    public async Task PreparationFailureReleasesEarlierBatchesAsync()
    {
        var strategies = new DispatchRenderers
        {
            OnPreparing = name =>
            {
                if (name == nameof(FillPathCommand2D))
                {
                    throw new InvalidOperationException("External preparation failed.");
                }
            },
        };
        var device = new DispatchDevice();
        var graph = new RenderGraphBuilder(device);
        using var texture = new DispatchTexture();
        (RenderTargetBinding2D target, DrawingGraphBindings2D bindings) = BindTarget(graph, texture);

        using var renderer = new RenderGraphRenderer2D(strategies.CreateTable(), RectAndPathList(), target, bindings);
        await Assert.ThrowsAsync<InvalidOperationException>(() => graph.Append2D(renderer).AsTask());
        Assert.Equal(1, Assert.Single(strategies.Batches).DisposeCount);
        Assert.Empty(strategies.Recorded);
    }

    /// <summary>Checks a returned batch is owned before cancellation is observed by the host.</summary>
    /// <returns>The asynchronous test completion.</returns>
    [Fact]
    public async Task CancellationAfterPrepareReleasesReturnedAndEarlierBatchesAsync()
    {
        using var cancellation = new CancellationTokenSource();
        var strategies = new DispatchRenderers
        {
            OnPreparing = name =>
            {
                if (name == nameof(FillPathCommand2D))
                {
                    cancellation.Cancel();
                }
            },
        };
        var device = new DispatchDevice();
        var graph = new RenderGraphBuilder(device);
        using var texture = new DispatchTexture();
        (RenderTargetBinding2D target, DrawingGraphBindings2D bindings) = BindTarget(graph, texture);

        using var renderer = new RenderGraphRenderer2D(strategies.CreateTable(), RectAndPathList(), target, bindings);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => graph.Append2D(renderer, cancellation.Token).AsTask());
        Assert.Equal(2, strategies.Batches.Count);
        Assert.All(strategies.Batches, batch => Assert.Equal(1, batch.DisposeCount));
        Assert.Empty(strategies.Recorded);
    }

    /// <summary>Checks a batch throwing while declaring passes is still released by the preparation owner.</summary>
    /// <returns>The asynchronous test completion.</returns>
    [Fact]
    public async Task AppendFailureReleasesItsBatchAsync()
    {
        var strategies = new DispatchRenderers { OnAppend = () => throw new InvalidOperationException("External append failed.") };
        var device = new DispatchDevice();
        var graph = new RenderGraphBuilder(device);
        using var texture = new DispatchTexture();
        (RenderTargetBinding2D target, DrawingGraphBindings2D bindings) = BindTarget(graph, texture);

        using var renderer = new RenderGraphRenderer2D(strategies.CreateTable(), RectList(), target, bindings);
        await Assert.ThrowsAsync<InvalidOperationException>(() => graph.Append2D(renderer).AsTask());
        Assert.Equal(1, Assert.Single(strategies.Batches).DisposeCount);
    }

    /// <summary>Checks repeated graph compilation cannot replay callbacks that share one prepared owner.</summary>
    /// <returns>The asynchronous test completion.</returns>
    [Fact]
    public async Task PreparedOwnerCannotBeRecordedThroughASecondCompiledPlanAsync()
    {
        var device = new DispatchDevice();
        var strategies = new DispatchRenderers();
        var graph = new RenderGraphBuilder(device);
        using var texture = new DispatchTexture();
        (RenderTargetBinding2D target, DrawingGraphBindings2D bindings) = BindTarget(graph, texture);
        using var renderer = new RenderGraphRenderer2D(strategies.CreateTable(), RectList(), target, bindings);
        using PreparedDrawingPasses2D prepared = await graph.Append2D(renderer);
        using CompiledRenderGraph first = graph.Compile();
        using CompiledRenderGraph second = graph.Compile();
        using var firstCommands = new DispatchCommands();
        using var secondCommands = new DispatchCommands();
        first.Record(firstCommands);
        Assert.Throws<InvalidOperationException>(() => second.Record(secondCommands));
        Assert.Single(strategies.Recorded);
    }

    private static Path2D Triangle()
    {
        var builder = new PathBuilder2D();
        builder.MoveTo(Vector2.Zero);
        builder.LineTo(new Vector2(8, 0));
        builder.LineTo(new Vector2(0, 8));
        builder.Close();
        return builder.Build();
    }

    private static RenderGraphDrawList2D RectList()
    {
        var canvas = new RenderGraphCanvas2D();
        canvas.Begin();
        canvas.FillRect(new Rect2D(0, 0, 8, 8), new Color2D(1, 1, 1, 1));
        return canvas.End();
    }

    private static RenderGraphDrawList2D RectAndPathList()
    {
        var canvas = new RenderGraphCanvas2D();
        canvas.Begin();
        var color = new Color2D(1, 1, 1, 1);
        canvas.FillRect(new Rect2D(0, 0, 8, 8), color);
        canvas.FillPath(Triangle(), color);
        return canvas.End();
    }

    private static (RenderTargetBinding2D Target, DrawingGraphBindings2D Bindings) BindTarget(RenderGraphBuilder graph, IGraphicsTexture texture)
    {
        RenderGraphTexture handle = graph.ImportTexture(texture, TextureState.ColorAttachment, new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite));
        var bindings = new DrawingGraphBindings2D(graph);
        bindings.BindTexture(texture, handle);
        return (new RenderTargetBinding2D
        {
            Texture = handle,
            Description = new TextureDesc
            {
                Width = texture.Width,
                Height = texture.Height,
                Format = texture.Format,
                Usage = texture.Usage,
            },
        }, bindings);
    }
}
