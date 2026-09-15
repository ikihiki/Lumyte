using System.Numerics;

using Lumyte.Graphics.Native.RenderGraph;
using Lumyte.Graphics.Native.Resources.Tests;
using Lumyte.Graphics.Passes;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.TwoD;

namespace Lumyte.Graphics.Native.Passes.Tests;

public sealed class NativeDraw2DPassTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CollapsedDrawingPreservesTheInitializedTarget(bool layer)
    {
        TestResourceBackend backend = new();
        NativeRenderPassRegistry registry = new();
        registry.AddImageProcessing().Add2DRendering();
        await using IGpuRenderRuntime runtime = await new NativeRenderProvider("test", (_, _) => new(backend), registry).CreateAsync(new());
        using var scene = new Draw2DSceneBuilder();
        scene.Transform(Matrix3x2.CreateScale(0));
        if (layer)
        {
            using var group = scene.BeginLayer(new(CompositeMode: CompositeMode.Clear));
            scene.FillRectangle(new(0, 0, 4, 4), Brush.Solid(Color.White));
        }
        else
        { scene.FillRectangle(new(0, 0, 4, 4), Brush.Solid(Color.White)); }
        var graph = new GpuRenderGraph();
        var target = graph.CreateTexture("target", new(4, 4, GpuFormat.Rgba8Unorm));
        graph.AddClearPass("clear", new(target, TextureClearValue.Color(Vector4.One)));
        graph.Add2DPass("collapsed", new(scene.Finish(), target));
        graph.MarkOutput(target);

        using GpuRenderGraphExecution execution = await runtime.SubmitAsync(graph.Compile());
        await execution.WaitForCompletionAsync();

        Assert.Equal(new GpuClearColor(1, 1, 1, 1), Assert.Single(backend.ColorClears));
    }

    [Fact]
    public async Task EmptyScenePreservesTheInitializedTarget()
    {
        TestResourceBackend backend = new();
        NativeRenderPassRegistry registry = new();
        registry.AddImageProcessing().Add2DRendering();
        await using IGpuRenderRuntime runtime = await new NativeRenderProvider("test", (_, _) => new(backend), registry).CreateAsync(new());
        using var scene = new Draw2DSceneBuilder();
        var graph = new GpuRenderGraph();
        var target = graph.CreateTexture("target", new(4, 4, GpuFormat.Rgba8Unorm));
        graph.AddClearPass("clear", new(target, TextureClearValue.Color(Vector4.One)));
        graph.Add2DPass("empty", new(scene.Finish(), target));
        graph.MarkOutput(target);

        using GpuRenderGraphExecution execution = await runtime.SubmitAsync(graph.Compile());
        await execution.WaitForCompletionAsync();

        Assert.Equal(new GpuClearColor(1, 1, 1, 1), Assert.Single(backend.ColorClears));
    }
}
