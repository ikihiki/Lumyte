using System.Numerics;
using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Passes.Tests;

public sealed class ImagePassTests
{
    [Fact]
    public void CopyKeepsItsSourceProducerAlive()
    {
        var graph = new GpuRenderGraph();
        var description = new GpuGraphTextureDescription(8, 4, GpuFormat.Rgba8Unorm);
        GpuRenderGraphTexture source = graph.CreateTexture("source", description);
        GpuRenderGraphTexture target = graph.CreateTexture("target", description);
        graph.AddClearPass("clear", new(source, TextureClearValue.Color(Vector4.One)));
        TexturePassResult copied = graph.AddCopyPass("copy", new(source, target));
        graph.ExportTexture(copied.Target);

        GpuRenderGraphPlan plan = graph.Compile();

        Assert.Collection(plan.Passes,
            pass => Assert.Equal("clear", pass.Name),
            pass => Assert.Equal("copy", pass.Name));
    }

    [Fact]
    public void OutputDeclaresAReadAndACompleteWrite()
    {
        var graph = new GpuRenderGraph();
        var description = new GpuGraphTextureDescription(8, 4, GpuFormat.Rgba8Unorm);
        GpuRenderGraphTexture source = graph.CreateTexture("source", description);
        GpuRenderGraphTexture target = graph.CreateTexture("target", description);
        graph.AddClearPass("clear", new(source, TextureClearValue.Color(Vector4.One)));
        graph.AddOutputPass("output", new(source, target));
        graph.ExportTexture(target);

        GpuRenderGraphPass output = graph.Compile().Passes.Single(pass => pass.Name == "output");

        Assert.Collection(output.Uses,
            use => Assert.Equal(new GpuRenderGraphUse(source, GpuRenderGraphAccess.Read), use),
            use => Assert.Equal(new GpuRenderGraphUse(target, GpuRenderGraphAccess.Write), use));
    }

    [Fact]
    public void ClearAcceptsANewInputWithoutRebuildingThePlan()
    {
        var graph = new GpuRenderGraph();
        GpuRenderGraphTexture target = graph.CreateTexture("target", new(8, 4, GpuFormat.Rgba8Unorm));
        GpuGraphInput<TextureClearValue> input = graph.CreateInput("color", ClearValueInputContract.Instance, TextureClearValue.Color(Vector4.One));
        var request = new ClearPassRequest(target, input);
        graph.AddClearPass("clear", request);
        graph.ExportTexture(target);
        GpuRenderGraphPlan plan = graph.Compile();
        TextureClearValue replacement = TextureClearValue.Color(new(0.25f, 0.5f, 0.75f, 1));

        var builder = plan.CreateBindings();
        builder.Set(input, replacement);
        GpuRenderGraphBindings bindings = builder.Build();

        Assert.Equal(replacement, Assert.Single(plan.Passes).GetInput(request.Value, bindings));
    }

    [Fact]
    public void CopyRejectsAliasingItsOwnTarget()
    {
        var graph = new GpuRenderGraph();
        GpuRenderGraphTexture target = graph.CreateTexture("target", new(8, 4, GpuFormat.Rgba8Unorm));

        ArgumentException exception = Assert.Throws<ArgumentException>(() => graph.AddCopyPass("copy", new(target, target)));

        Assert.Equal("request", exception.ParamName);
        Assert.Contains("distinct", exception.Message);
    }

    [Fact]
    public void CopyRejectsDifferentLogicalExtents()
    {
        var graph = new GpuRenderGraph();
        GpuRenderGraphTexture source = graph.CreateTexture("source", new(8, 4, GpuFormat.Rgba8Unorm));
        GpuRenderGraphTexture target = graph.CreateTexture("target", new(4, 4, GpuFormat.Rgba8Unorm));

        ArgumentException exception = Assert.Throws<ArgumentException>(() => graph.AddCopyPass("copy", new(source, target)));

        Assert.Equal("request", exception.ParamName);
        Assert.Contains("matching logical descriptions", exception.Message);
    }

    [Theory]
    [InlineData(2, 1, 1)]
    [InlineData(1, 2, 1)]
    [InlineData(1, 1, 4)]
    public void OutputRejectsImagesOutsideItsDeclaredDomain(uint layers, uint mips, uint samples)
    {
        var graph = new GpuRenderGraph();
        var description = new GpuGraphTextureDescription(8, 4, GpuFormat.Rgba8Unorm, layers, mips, samples);
        GpuRenderGraphTexture source = graph.CreateTexture("source", description);
        GpuRenderGraphTexture target = graph.CreateTexture("target", description);

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() => graph.AddOutputPass("output", new(source, target)));

        Assert.Contains("one mip, layer, and sample", exception.Message);
    }
}
