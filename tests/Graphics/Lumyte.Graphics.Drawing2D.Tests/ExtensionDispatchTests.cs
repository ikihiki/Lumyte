using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Xunit;

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
        canvas.DrawMesh(mesh, paint);
        canvas.FillRect(rect with { X = 2 }, color);

        using PreparedDraw2D prepared = await renderer.PrepareAsync(canvas.End(), new RenderTarget2D { View = targetView });
        Type[] expected =
        [
            typeof(FillRectCommand2D), typeof(FillRectCommand2D), typeof(FillRoundedRectCommand2D), typeof(FillCircleCommand2D), typeof(DrawLineCommand2D), typeof(FillPathCommand2D), typeof(StrokePathCommand2D), typeof(DrawImageCommand2D), typeof(DrawMeshCommand2D), typeof(FillRectCommand2D),
        ];
        Assert.Equal(expected, strategies.Commands.Select(command => command.GetType()));
        Assert.Equal(9, strategies.Batches.Count);
        Assert.All(strategies.Batches, batch => Assert.Equal(0, batch.DisposeCount));
        using var commands = new DispatchCommands();
        prepared.Record(commands);
        Assert.Equal(
            expected.Where((type, index) => index == 0 || type != expected[index - 1]).Select(type => type.Name + ".draw"),
            strategies.Recorded);
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

    private static Path2D Triangle()
    {
        var builder = new PathBuilder2D();
        builder.MoveTo(Vector2.Zero);
        builder.LineTo(new Vector2(8, 0));
        builder.LineTo(new Vector2(0, 8));
        builder.Close();
        return builder.Build();
    }
}
