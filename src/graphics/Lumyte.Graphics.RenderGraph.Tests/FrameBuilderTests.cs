namespace Lumyte.Graphics.RenderGraph.Tests;

public sealed class FrameBuilderTests
{
    [Fact]
    public void ContributorsRunInOrderThenOrdinalName()
    {
        var builder = new GpuRenderGraphFrameBuilder();
        List<string> order = [];
        builder.AddContributor("z", "z", (_, state) => order.Add(state));
        builder.AddContributor("a", "a", (_, state) => order.Add(state));
        builder.AddContributor("first", "first", (_, state) => order.Add(state), -1);
        builder.AddContributor("disabled", "disabled", (_, state) => order.Add(state), enabled: false);

        builder.BuildGraph();

        Assert.Equal(["first", "a", "z"], order);
    }

    [Fact]
    public void ContributorsSharePublishedResourcesAndIsolateLocalNames()
    {
        var builder = new GpuRenderGraphFrameBuilder();
        builder.AddContributor("source", 0, static (context, _) =>
        {
            var target = context.Graph.CreateTexture("color", new(4, 4, GpuFormat.Rgba8Unorm));
            context.Graph.AddPass("draw", GraphTests.Contract.Instance, new(null, target));
            context.PublishTexture("color", target);
        });
        builder.AddContributor("output", 0, static (context, _) =>
        {
            var target = context.Graph.CreateTexture("color", new(4, 4, GpuFormat.Rgba8Unorm));
            context.Graph.AddPass("draw", GraphTests.Contract.Instance, new(context.GetTexture("color"), target));
            context.Graph.ExportTexture(target);
        }, order: 1);

        var plan = builder.Compile();

        Assert.Equal(2, plan.Passes.Count);
        Assert.Equal(2, plan.Resources.Select(resource => resource.Name).Distinct().Count());
        Assert.Same(plan.Resources[0], Assert.IsType<GraphTests.Request>(plan.Passes[1].Request).Source);
    }

    [Fact]
    public void PublicationRejectsResourcesFromAnotherGraph()
    {
        var builder = new GpuRenderGraphFrameBuilder();
        var foreign = new GpuRenderGraph().CreateBuffer("foreign", new(4));
        builder.AddContributor("invalid", foreign, static (context, resource) => context.PublishBuffer("buffer", resource));

        Assert.Throws<ArgumentException>(() => builder.BuildGraph());
    }

    [Fact]
    public void PublishedResourceKindsAreChecked()
    {
        var builder = new GpuRenderGraphFrameBuilder();
        builder.AddContributor("invalid", 0, static (context, _) =>
        {
            context.PublishBuffer("resource", context.Graph.CreateBuffer("buffer", new(4)));
            context.GetTexture("resource");
        });

        Assert.Contains("GpuRenderGraphTexture", Assert.Throws<InvalidOperationException>(() => builder.BuildGraph()).Message);
    }

    [Fact]
    public void ContributionContextCannotEscapeItsCallback()
    {
        var builder = new GpuRenderGraphFrameBuilder();
        GpuRenderGraphContributionContext? saved = null;
        builder.AddContributor("save", 0, (context, _) => saved = context);
        builder.BuildGraph();

        Assert.Throws<ObjectDisposedException>(() => saved!.Graph);
    }
}
