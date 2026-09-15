using System.Numerics;

using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.TwoD;

namespace Lumyte.Graphics.Passes.Tests;

public sealed class Draw2DPassTests
{
    [Fact]
    public void ConstantSceneKeepsImageProducersAndPriorTargetContent()
    {
        var graph = new GpuRenderGraph();
        var target = Target(graph, "target");
        var image = Target(graph, "image");
        graph.Add2DPass("draw", new(Scene(image), target));
        graph.ExportTexture(target);

        var plan = graph.Compile();
        var pass = plan.Passes[^1];

        Assert.Equal(new[] { "clear target", "clear image", "draw" }, plan.Passes.Select(p => p.Name));
        Assert.Collection(pass.Uses, use => Assert.Equal(new GpuRenderGraphUse(target, GpuRenderGraphAccess.ReadWrite), use), use => Assert.Equal(new GpuRenderGraphUse(image, GpuRenderGraphAccess.Read), use));
    }

    [Fact]
    public void SlotCanSwitchAmongItsFixedImages()
    {
        var graph = new GpuRenderGraph();
        var target = Target(graph, "target");
        var first = Target(graph, "first");
        var second = Target(graph, "second");
        var input = graph.CreateInput("scene", Draw2DSceneInputContract.Instance, Scene(first));
        var request = new Draw2DPassRequest(input, target, [first, second]);
        graph.Add2DPass("draw", request);
        graph.ExportTexture(target);
        var plan = graph.Compile();
        var replacement = Scene(second);

        var builder = plan.CreateBindings();
        builder.Set(input, replacement);
        var bindings = builder.Build();

        Assert.Same(replacement, plan.Passes[^1].GetInput(request.Scene, bindings));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SlotRejectsImagesOutsideItsFixedReadSet(bool ownTarget)
    {
        var graph = new GpuRenderGraph();
        var target = Target(graph, "target");
        var declared = Target(graph, "declared");
        var hidden = Target(graph, "hidden");
        var input = graph.CreateInput("scene", Draw2DSceneInputContract.Instance, Scene(declared));
        graph.Add2DPass("draw", new(input, target, [declared]));
        graph.ExportTexture(target);
        var builder = graph.Compile().CreateBindings();
        builder.Set(input, Scene(ownTarget ? target : hidden));

        var exception = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("read-only declaration", exception.Message);
        Assert.Contains(ownTarget ? "target" : "hidden", exception.Message);
    }

    [Fact]
    public void ConstantSceneCannotSampleItsOwnTarget()
    {
        var graph = new GpuRenderGraph();
        var target = Target(graph, "target");

        Assert.Equal("request", Assert.Throws<ArgumentException>(() => graph.Add2DPass("draw", new(Scene(target), target))).ParamName);
    }

    [Fact]
    public void DrawRequiresAnInitializedTarget()
    {
        var graph = new GpuRenderGraph();
        var target = graph.CreateTexture("target", new(1, 1, GpuFormat.Rgba8Unorm));
        using var builder = new Draw2DSceneBuilder();
        graph.Add2DPass("draw", new(builder.Finish(), target));
        graph.ExportTexture(target);

        Assert.Throws<InvalidOperationException>(() => graph.Compile());
    }

    [Fact]
    public void RequestOwnsItsReadSet()
    {
        var graph = new GpuRenderGraph();
        var target = Target(graph, "target");
        var image = Target(graph, "image");
        GpuRenderGraphTexture[] reads = [image];
        var request = new Draw2DPassRequest(Scene(image), target, reads);

        reads[0] = target;

        Assert.Same(image, Assert.Single(request.ReadTextures));
    }

    private static GpuRenderGraphTexture Target(GpuRenderGraph graph, string name)
    { var target = graph.CreateTexture(name, new(4, 4, GpuFormat.Rgba8Unorm)); graph.AddClearPass("clear " + name, new(target, TextureClearValue.Color(Vector4.Zero))); return target; }
    private static Draw2DScene Scene(GpuRenderGraphTexture image)
    { using var builder = new Draw2DSceneBuilder(); builder.DrawImage(image, new(0, 0, 4, 4)); return builder.Finish(); }

    [Fact]
    public void DrawRejectsAnSrgbTargetBeforeOutputEncoding()
    {
        var graph = new GpuRenderGraph();
        var target = graph.CreateTexture("target", new(1, 1, GpuFormat.Rgba8UnormSrgb));
        using var builder = new Draw2DSceneBuilder();

        var exception = Assert.Throws<NotSupportedException>(() => graph.Add2DPass("draw", new(builder.Finish(), target)));

        Assert.Contains("linear", exception.Message);
    }
}
