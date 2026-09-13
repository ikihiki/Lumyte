using System.Numerics;
using Lumyte.Graphics.Passes;
using Lumyte.Graphics.Portable.RenderGraph;
using Lumyte.Graphics.Portable.Resources.Tests.Unit.Management;
using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Portable.Passes.Tests;

public sealed class PortableImageProcessingPassesTests
{
    [Fact]
    public async Task StartupRegistrationCreatesNoShaderObjects()
    {
        var backend = new ManagerTestBackend();
        var registry = new PortableRenderPassRegistry().AddImageProcessing();
        var provider = new PortableRenderProvider("test", (_, _) => ValueTask.FromResult<IPortableGpuBackend>(backend), registry);

        await using IGpuRenderRuntime runtime = await provider.CreateAsync(new());

        Assert.Empty(backend.Created);
    }

    [Fact]
    public async Task RepeatedOutputReusesItsPipelineDefinitionAndReclaimsTransientBindings()
    {
        var backend = new ManagerTestBackend();
        var registry = new PortableRenderPassRegistry().AddImageProcessing();
        var provider = new PortableRenderProvider("test", (_, _) => ValueTask.FromResult<IPortableGpuBackend>(backend), registry);
        await using var runtime = (PortableRenderRuntime)await provider.CreateAsync(new());
        var graph = new GpuRenderGraph();
        GpuRenderGraphTexture source = graph.CreateTexture("source", new(4, 4, GpuFormat.Rgba8Unorm));
        GpuRenderGraphTexture target = graph.CreateTexture("target", source.Description);
        graph.AddClearPass("clear", new(source, TextureClearValue.Color(Vector4.One)));
        graph.AddOutputPass("output", new(source, target)); graph.MarkOutput(target);
        GpuRenderGraphPlan plan = graph.Compile();
        using (GpuRenderGraphExecution first = await runtime.SubmitAsync(plan)) { await first.WaitForCompletionAsync(); }
        runtime.Resources.Collect();
        int pipelineCount = backend.Created.OfType<GpuRasterPipelineHandle>().Count();

        using (GpuRenderGraphExecution second = await runtime.SubmitAsync(plan)) { await second.WaitForCompletionAsync(); }
        runtime.Resources.Collect();

        Assert.Equal(1, pipelineCount);
        Assert.Single(backend.Created.OfType<GpuRasterPipelineHandle>());
        Assert.Equal(0, runtime.Resources.Manager.Statistics.BindingsCount);
    }
}
