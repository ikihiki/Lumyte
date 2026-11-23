using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Mathematics.Colors;
using Xunit;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks fixture-scoped pipeline reuse without sharing graph-local storage.</summary>
public sealed class SharedDrawingResourceTests
{
    /// <summary>Separate renders share fixed pipelines but return all private storage and ownership.</summary>
    /// <returns>The asynchronous preparations.</returns>
    [Fact]
    public async Task SerialGraphsReusePipelinesAndKeepPrivateStorageAsync()
    {
        var device = new ProfilingDevice();
        using var state = new DrawingResourceState2D(device, numericPoolBudget: 0);
        await PrepareAsync(device, state, new(1, 0, 0, 1));
        int first = device.Pipelines.Count;
        Assert.True(first >= 2);
        IGraphicsPipeline[] pipelines = [.. device.Pipelines];
        int buffers = device.NativeBuffers;
        await PrepareAsync(device, state, new(0, 0, 1, 1));
        Assert.Equal(first, device.Pipelines.Count);
        Assert.Equal(pipelines, device.Pipelines);
        Assert.True(device.NativeBuffers > buffers);
        Assert.Equal(0, state.IdleRecordBytes);
        Assert.Equal(0, device.DisposedPipelines);
        state.Dispose();
        Assert.Equal(first, device.DisposedPipelines);
        Assert.Throws<ObjectDisposedException>(() => state.Retain());
    }

    /// <summary>Foreign-device construction fails without acquiring a use or populating another cache.</summary>
    [Fact]
    public void ContextsCannotCrossDevicesAndClosedContextsCannotBeReopened()
    {
        var device = new ProfilingDevice();
        using var state = new DrawingResourceState2D(device);
        using var graph = new Graph(new ProfilingDevice());
        var description = new TextureDesc { Width = 1, Height = 1, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment };
        var canvas = new RenderGraphCanvas2D();
        canvas.Begin();
        RenderGraphDrawList2D list = canvas.End();
        var target = new RenderTargetBinding2D { Texture = graph.CreateTexture(description), Description = description };
        Assert.Throws<ArgumentException>(() => new RenderGraphRenderer2D(DrawingCommandRenderers2D.CreateDefault(), list, target, new(graph), state));
        Assert.Empty(device.Pipelines);
        state.Dispose();
        Assert.Throws<ObjectDisposedException>(() => state.Retain());
        using var sameDeviceGraph = new Graph(device);
        var sameDeviceTarget = new RenderTargetBinding2D { Texture = sameDeviceGraph.CreateTexture(description), Description = description };
        Assert.Throws<ObjectDisposedException>(() => new RenderGraphRenderer2D(DrawingCommandRenderers2D.CreateDefault(), list, sameDeviceTarget, new(sameDeviceGraph), state));
        Assert.Empty(device.Pipelines);
    }

    /// <summary>The root can close before prepared work; its last use retires pipelines exactly once.</summary>
    /// <returns>The asynchronous preparation.</returns>
    [Fact]
    public async Task ClosingRootDoesNotFreePreparedGraphResourcesAsync()
    {
        var device = new ProfilingDevice();
        using var state = new DrawingResourceState2D(device, numericPoolBudget: 0);
        using var graph = new Graph(device);
        using RenderGraphRenderer2D renderer = CreateRenderer(graph, state, new(1, 0, 0, 1));
        using PreparedDrawingPasses2D prepared = await graph.Append2D(renderer);
        renderer.Dispose();
        state.Dispose();
        Assert.Equal(0, device.DisposedPipelines);
        Assert.Throws<ObjectDisposedException>(() => state.Retain());
        prepared.Dispose();
        prepared.Dispose();
        Assert.Equal(device.Pipelines.Count, device.DisposedPipelines);
    }

    /// <summary>Default resources retain the fixed ABI identity; cloned external artifacts remain legacy.</summary>
    [Fact]
    public void DefaultAndReferenceCachesRemainDistinctAndDeviceBound()
    {
        var firstDevice = new ProfilingDevice();
        var secondDevice = new ProfilingDevice();
        using var first = new DrawingResourceState2D(firstDevice);
        using var second = new DrawingResourceState2D(secondDevice);
        Assert.NotSame(first.Resources.Pipeline, second.Resources.Pipeline);
        Assert.NotSame(first.Records(), second.Records());
        Assert.NotNull(first.Records());
        using IGraphicsShader vertex = firstDevice.CreateShader(new ShaderArtifact(DrawingShaders2D.Vertex.GetBinary()));
        using IGraphicsShader fragment = firstDevice.CreateShader(new ShaderArtifact(DrawingShaders2D.Fragment.GetBinary()));
        using IGraphicsPipeline pipeline = firstDevice.CreateGraphicsPipeline(new() { VertexShader = vertex, FragmentShader = fragment });
        using var reference = new DrawingResourceState2D(firstDevice, new(firstDevice, pipeline));
        Assert.Null(reference.Records());
        Assert.Null(reference.Rectangles());
        Assert.NotSame(first.Resources.Pipeline, reference.Resources.Pipeline);
    }

    private static async Task PrepareAsync(ProfilingDevice device, DrawingResourceState2D state, SRgba<float> color)
    {
        using var graph = new Graph(device);
        using RenderGraphRenderer2D renderer = CreateRenderer(graph, state, color);
        using PreparedDrawingPasses2D prepared = await graph.Append2D(renderer);
        renderer.Dispose();
        using CompiledRenderGraph compiled = graph.Compile();
        using var commands = new ProfilingCommands();
        compiled.Record(commands);
        commands.Dispose();
    }

    private static RenderGraphRenderer2D CreateRenderer(Graph graph, DrawingResourceState2D state, SRgba<float> color)
    {
        var description = new TextureDesc { Width = 4, Height = 4, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment };
        RenderGraphTexture target = graph.CreateTexture(description);
        graph.MarkOutput(target);
        var canvas = new RenderGraphCanvas2D();
        canvas.Begin();
        canvas.FillCircle(new(2), 1, color);
        return new(DrawingCommandRenderers2D.CreateDefault(), canvas.End(), new() { Texture = target, Description = description, LoadOp = AttachmentLoadOp.Clear }, new(graph), state);
    }
}
