using System.Numerics;
using Lumyte.Graphics.Native.RenderGraph;
using Lumyte.Graphics.Native.Resources.Tests;
using Lumyte.Graphics.Passes;
using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Native.Passes.Tests;

public sealed class NativeImagePassTests
{
    [Fact]
    public async Task CopyInitializesScratchBeforeReadingAllMipLayers()
    {
        TestResourceBackend backend = new();
        NativeRenderPassRegistry registry = new(); registry.AddImageProcessing();
        await using IGpuRenderRuntime runtime = await new NativeRenderProvider("test", (_, _) => new(backend), registry).CreateAsync(new());
        var graph = new GpuRenderGraph();
        var description = new GpuGraphTextureDescription(8, 8, GpuFormat.Rgba8Unorm, DepthOrArrayLayers: 2, MipLevelCount: 3);
        var source = graph.CreateTexture("source", description);
        var target = graph.CreateTexture("target", description);
        graph.AddClearPass("clear", new(source, TextureClearValue.Color(Vector4.One)));
        graph.AddCopyPass("copy", new(source, target));
        graph.ExportTexture(target);

        using GpuRenderGraphExecution execution = await runtime.SubmitAsync(graph.Compile());
        await execution.WaitForCompletionAsync();

        Assert.Equal(["ReadTexture", "ReadTexture", "ReadTexture", "CopyTexture", "CopyTexture", "CopyTexture"],
            backend.Commands.Where(command => command is "ReadTexture" or "CopyTexture"));
    }

    [Fact]
    public async Task ClearPremultipliesLinearColorBeforeRecording()
    {
        TestResourceBackend backend = new();
        NativeRenderPassRegistry registry = new(); registry.AddImageProcessing();
        await using IGpuRenderRuntime runtime = await new NativeRenderProvider("test", (_, _) => new(backend), registry).CreateAsync(new());
        var graph = new GpuRenderGraph();
        var target = graph.CreateTexture("target", new(4, 4, GpuFormat.Rgba8Unorm));
        graph.AddClearPass("clear", new(target, TextureClearValue.Color(new Vector4(0.8f, 0.4f, 0.2f, 0.5f))));
        graph.MarkOutput(target);

        using GpuRenderGraphExecution execution = await runtime.SubmitAsync(graph.Compile());
        await execution.WaitForCompletionAsync();

        Assert.Equal(new GpuClearColor(0.4f, 0.2f, 0.1f, 0.5f), Assert.Single(backend.ColorClears));
    }

    [Fact]
    public async Task ClearInitializesEveryMipAndArrayLayer()
    {
        TestResourceBackend backend = new();
        NativeRenderPassRegistry registry = new(); registry.AddImageProcessing();
        await using IGpuRenderRuntime runtime = await new NativeRenderProvider("test", (_, _) => new(backend), registry).CreateAsync(new());
        var graph = new GpuRenderGraph();
        var target = graph.CreateTexture("target", new(8, 8, GpuFormat.Rgba8Unorm, DepthOrArrayLayers: 2, MipLevelCount: 3));
        graph.AddClearPass("clear", new(target, TextureClearValue.Color(Vector4.One))); graph.MarkOutput(target);

        using GpuRenderGraphExecution execution = await runtime.SubmitAsync(graph.Compile());
        await execution.WaitForCompletionAsync();

        Assert.Equal(6, backend.ColorClears.Count);
    }

    [Fact]
    public async Task ClearPreservesRequestedDepthAndStencilValues()
    {
        TestResourceBackend backend = new();
        NativeRenderPassRegistry registry = new(); registry.AddImageProcessing();
        await using IGpuRenderRuntime runtime = await new NativeRenderProvider("test", (_, _) => new(backend), registry).CreateAsync(new());
        var graph = new GpuRenderGraph();
        var target = graph.CreateTexture("target", new(4, 4, GpuFormat.Depth24PlusStencil8));
        graph.AddClearPass("clear", new(target, TextureClearValue.DepthStencil(0.25f, 17))); graph.MarkOutput(target);

        using GpuRenderGraphExecution execution = await runtime.SubmitAsync(graph.Compile());
        await execution.WaitForCompletionAsync();

        Assert.Equal((0.25f, (byte)17), Assert.Single(backend.DepthStencilClears));
    }
}
