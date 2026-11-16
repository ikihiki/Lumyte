using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;
using Xunit;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks private multi-pass storage independently of native allocation and renderer algorithms.</summary>
public sealed class PreparationGraphResourceTests
{
    /// <summary>Unmarked multi-pass private uploads and consumers are all culled rather than becoming imported-write roots.</summary>
    [Fact]
    public void UnusedPrivateProducerAndConsumerAreCulled()
    {
        using var graph = new Graph();
        using IGraphicsBuffer<uint> buffer = new ProfilingDevice().CreateBuffer<uint>(new() { Count = 16, Usage = BufferUsage.CopyDestination | BufferUsage.ShaderRead });
        RenderGraphBuffer<uint> handle = graph.PreparationBuffer(buffer);
        graph.AddPass("copy", _ => { }).UseBuffer(handle, new(PipelineStage.Copy, ResourceAccess.CopyWrite));
        graph.AddPass("consume", _ => { }).UseBuffer(handle, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead));
        using CompiledRenderGraph plan = graph.Compile();
        Assert.Empty(plan.PassNames);
    }

    /// <summary>Preparation-local device storage is uninitialized until an explicit producer is declared.</summary>
    [Fact]
    public void PrivateStorageReadWithoutProducerFails()
    {
        using var graph = new Graph();
        using IGraphicsBuffer<uint> buffer = new ProfilingDevice().CreateBuffer<uint>(new() { Count = 16, Usage = BufferUsage.ShaderRead });
        graph.AddPass("read", _ => { }).UseBuffer(graph.PreparationBuffer(buffer), new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead)).KeepAlive();
        Assert.Throws<InvalidOperationException>(() => graph.Compile());
    }
}
