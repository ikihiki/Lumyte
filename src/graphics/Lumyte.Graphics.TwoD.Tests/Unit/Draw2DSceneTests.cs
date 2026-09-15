using System.Numerics;

using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.TwoD.Tests;

public sealed class Draw2DSceneTests
{
    [Fact]
    public void ClipKeepsTheTransformAtItsCreation()
    {
        using var builder = new Draw2DSceneBuilder();
        builder.SetTransform(Matrix3x2.CreateTranslation(10, 20));
        using (builder.BeginClip(new Rect(0, 0, 10, 10)))
        { builder.SetTransform(Matrix3x2.CreateScale(2)); builder.FillRectangle(new(0, 0, 3, 3), Brush.Solid(Color.White)); }

        var command = Assert.IsType<Draw2DShapeCommand>(Assert.Single(builder.Finish().Commands));

        Assert.Equal(Matrix3x2.CreateScale(2), command.State.Transform);
        Assert.Equal(Matrix3x2.CreateTranslation(10, 20), Assert.Single(command.State.Clips).Transform);
    }

    [Fact]
    public void InvalidClipDoesNotOpenAScope()
    {
        using var builder = new Draw2DSceneBuilder();

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.BeginClip(new Rect(0, 0, -1, 1)));

        Assert.Empty(builder.Finish().Commands);
    }

    [Fact]
    public void ScopeMustCloseInReverseOrder()
    {
        using var builder = new Draw2DSceneBuilder();
        var outer = builder.BeginState();
        var inner = builder.BeginState();

        Assert.Throws<InvalidOperationException>(() => outer.Dispose());
        inner.Dispose();
        outer.Dispose();

        Assert.Empty(builder.Finish().Commands);
    }

    [Fact]
    public void LayerOwnsLocalCoordinatesAndAppliesTheParentClipAtComposite()
    {
        using var builder = new Draw2DSceneBuilder();
        builder.SetTransform(Matrix3x2.CreateTranslation(4, 5));
        using (builder.BeginClip(new Rect(0, 0, 10, 10)))
        using (builder.BeginLayer(new(Opacity: 0.5f)))
        { builder.FillRectangle(new(0, 0, 3, 3), Brush.Solid(Color.White)); }

        var layer = Assert.IsType<Draw2DLayerCommand>(Assert.Single(builder.Finish().Commands));
        var shape = Assert.IsType<Draw2DShapeCommand>(Assert.Single(layer.Content.Commands));

        Assert.Equal(Matrix3x2.CreateTranslation(4, 5), layer.State.Transform);
        Assert.Single(layer.State.Clips);
        Assert.Equal(Matrix3x2.Identity, shape.State.Transform);
        Assert.Empty(shape.State.Clips);
    }

    [Fact]
    public void FinishedSceneSurvivesBuilderDisposal()
    {
        var builder = new Draw2DSceneBuilder();
        builder.FillEllipse(new(0, 0, 4, 5), Brush.Solid(Color.White));
        var scene = builder.Finish();

        builder.Dispose();

        Assert.Equal(Draw2DShapeKind.Ellipse, Assert.IsType<Draw2DShapeCommand>(Assert.Single(scene.Commands)).Kind);
        Assert.Throws<ObjectDisposedException>(() => builder.Finish());
    }

    [Fact]
    public void SceneOwnsImageReferencesWithoutRecopyingPixels()
    {
        var data = new GpuImageUploadData(new("image", 1), new(1, 1, GpuFormat.Rgba8Unorm), GpuImageColorEncoding.Linear, GpuImageAlphaMode.Straight, [new(0, 0, 4, 4, new byte[] { 1, 2, 3, 4 })]);
        using var builder = new Draw2DSceneBuilder();
        builder.DrawImage(data, new(0, 0, 1, 1));
        builder.DrawImage(data, new(1, 1, 1, 1));

        var scene = builder.Finish();

        Assert.Same(data, Assert.Single(scene.OwnUploads));
        Assert.Same(data, Assert.IsType<Draw2DImageCommand>(scene.Commands[0]).Source.Upload);
    }

    [Fact]
    public void ChildSnapshotsShareTheirRecordedOwnership()
    {
        var graph = new GpuRenderGraph();
        var image = graph.CreateTexture("image", new(1, 1, GpuFormat.Rgba8Unorm));
        using var child = new Draw2DSceneBuilder();
        child.DrawImage(image, new(0, 0, 1, 1));
        var content = child.Finish();
        using var parent = new Draw2DSceneBuilder();
        parent.DrawScene(content);

        var scene = parent.Finish();

        Assert.Empty(scene.OwnTextures);
        Assert.Same(content, Assert.Single(scene.ChildScenes));
        Assert.Same(image, Assert.Single(scene.LogicalTextures));
    }
}
