namespace Lumyte.Graphics.RenderGraph.Tests;

public sealed class PlanCacheTests
{
    [Fact]
    public void CachedScheduleRebindsCurrentResourcesAndRequests()
    {
        var cache = new GpuRenderGraphPlanCache(2);
        var first = CreateGraph(4);
        var second = CreateGraph(8);

        var original = first.Graph.Compile(cache);
        var current = second.Graph.Compile(cache);

        Assert.Equal(1, cache.Count);
        Assert.Same(second.Target, Assert.Single(current.Resources));
        Assert.Same(second.Target, Assert.IsType<GraphTests.Request>(Assert.Single(current.Passes).Request).Target);
        Assert.Same(first.Target, Assert.Single(original.Resources));
    }

    [Fact]
    public void CacheRemainsBoundedAndCanBeCleared()
    {
        var cache = new GpuRenderGraphPlanCache(2);
        for (var count = 1; count <= 3; count++)
        {
            var graph = CreateGraph(4);
            for (var index = 1; index < count; index++)
            { graph.Graph.AddPass($"write{index}", GraphTests.Contract.Instance, new(null, graph.Target)); }
            graph.Graph.MarkOutput(graph.Target);
            graph.Graph.Compile(cache);
        }

        Assert.Equal(2, cache.Count);
        cache.Clear();
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void CacheDistinguishesInitializedImportsFromUndefinedTransients()
    {
        var cache = new GpuRenderGraphPlanCache();
        var valid = new GpuRenderGraph();
        var description = new GpuGraphTextureDescription(1, 1, GpuFormat.Rgba8Unorm);
        var imported = valid.ImportTexture("source", new BindingsTests.TestTexture(Guid.NewGuid(), description));
        var output = valid.CreateTexture("output", description);
        valid.AddPass("copy", GraphTests.Contract.Instance, new(imported, output));
        valid.MarkOutput(output);
        valid.Compile(cache);
        var invalid = new GpuRenderGraph();
        var undefined = invalid.CreateTexture("source", description);
        var invalidOutput = invalid.CreateTexture("output", description);
        invalid.AddPass("copy", GraphTests.Contract.Instance, new(undefined, invalidOutput));
        invalid.MarkOutput(invalidOutput);

        Assert.Contains("uninitialized", Assert.Throws<InvalidOperationException>(() => invalid.Compile(cache)).Message);
    }

    [Fact]
    public void LifetimesReferToSurvivingFeatureIndices()
    {
        var graph = CreateGraph(4);
        var output = graph.Graph.CreateTexture("copy", graph.Target.Description);
        graph.Graph.AddPass("copy", GraphTests.Contract.Instance, new(graph.Target, output));
        graph.Graph.ExportTexture(output);

        var plan = graph.Graph.Compile();

        Assert.Equal([
            new GpuRenderGraphResourceLifetime(graph.Target, 0, 1),
            new GpuRenderGraphResourceLifetime(output, 1, 1)], plan.ResourceLifetimes);
    }

    private static (GpuRenderGraph Graph, GpuRenderGraphTexture Target) CreateGraph(uint width)
    {
        var graph = new GpuRenderGraph();
        var target = graph.CreateTexture("target", new(width, 4, GpuFormat.Rgba8Unorm));
        graph.AddPass("clear", GraphTests.Contract.Instance, new(null, target));
        graph.MarkOutput(target);
        return (graph, target);
    }
}
