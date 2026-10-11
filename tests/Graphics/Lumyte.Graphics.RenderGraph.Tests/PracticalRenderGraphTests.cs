using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.RenderGraph.Tests;

/// <summary>Checks culling, temporary lifetime, exports and shader data integration.</summary>
public sealed class PracticalRenderGraphTests
{
    private static BarrierScope CopyWrite => new(PipelineStage.Copy, ResourceAccess.CopyWrite);

    private static BarrierScope CopyRead => new(PipelineStage.Copy, ResourceAccess.CopyRead);

    private static BarrierScope ShaderRead => new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead);

    /// <summary>Checks temporary resources are allocated only for live work and released after completion.</summary>
    [Fact]
    public void TemporaryAllocationsAreLazyAndUnusedBranchesAreCulled()
    {
        var device = new TestDevice();
        var graph = new RenderGraph(device);
        RenderGraphBuffer<uint> temporary = graph.CreateBuffer(new BufferDesc<uint> { Count = 16, Usage = BufferUsage.CopyDestination | BufferUsage.CopySource });
        RenderGraphTexture unused = graph.CreateTexture(new TextureDesc { Width = 8, Height = 8, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.CopyDestination });
        using var output = new TestBuffer();
        RenderGraphBuffer<uint> destination = graph.ImportBuffer(output, default);
        graph.AddPass("produce", _ => { }).UseBuffer(temporary, CopyWrite);
        graph.AddPass("unused", _ => throw new InvalidOperationException()).UseTexture(unused, TextureState.CopyDestination, CopyWrite);
        graph.AddPass("consume", context => context.Commands.CopyBuffer(context.GetBuffer(temporary).Slice(0, 16), context.GetBuffer(destination).Slice(0, 16)))
            .UseBuffer(temporary, CopyRead).UseBuffer(destination, CopyWrite);
        using CompiledRenderGraph plan = graph.Compile();
        Assert.Equal(new[] { "produce", "consume" }, plan.PassNames);
        Assert.Equal(new[] { "unused" }, plan.CulledPassNames);
        Assert.Equal(0, device.Buffers);
        Assert.Equal(0, device.Textures);
        using var commands = new RecordingCommands();
        plan.Record(commands);
        Assert.Equal(1, device.Buffers);
        Assert.Equal(0, device.Textures);
        Assert.Throws<InvalidOperationException>(plan.Dispose);
        commands.Complete();
        plan.Dispose();
        Assert.Equal(new[] { "buffer" }, device.Lifetime);
        plan.Dispose();
        Assert.Equal(new[] { "buffer" }, device.Lifetime);
    }

    /// <summary>Checks a pure overwrite does not retain the obsolete producer.</summary>
    [Fact]
    public void CullingDistinguishesValueDependenciesFromWriteHazards()
    {
        var graph = new RenderGraph(new TestDevice());
        RenderGraphBuffer<uint> output = graph.CreateBuffer(new BufferDesc<uint> { Count = 4, Usage = BufferUsage.CopyDestination });
        graph.AddPass("obsolete", _ => throw new InvalidOperationException()).UseBuffer(output, CopyWrite);
        graph.AddPass("final", _ => { }).UseBuffer(output, CopyWrite);
        graph.MarkOutput(output);
        using CompiledRenderGraph plan = graph.Compile();
        Assert.Equal(new[] { "final" }, plan.PassNames);
        Assert.Equal(new[] { "obsolete" }, plan.CulledPassNames);
    }

    /// <summary>Checks read/write mutations retain the prior value producer.</summary>
    [Fact]
    public void ReadWriteOutputsRetainTheirProducer()
    {
        var graph = new RenderGraph(new TestDevice());
        RenderGraphBuffer<uint> output = graph.CreateBuffer(new BufferDesc<uint> { Count = 4, Usage = BufferUsage.ShaderRead | BufferUsage.ShaderWrite });
        graph.AddPass("first", _ => { }).UseBuffer(output, new(PipelineStage.ComputeShader, ResourceAccess.ShaderWrite));
        graph.AddPass("modify", _ => { }).UseBuffer(output, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead | ResourceAccess.ShaderWrite));
        graph.MarkOutput(output);
        using CompiledRenderGraph plan = graph.Compile();
        Assert.Equal(new[] { "first", "modify" }, plan.PassNames);
    }

    /// <summary>Checks imported writes and explicitly retained side effects survive with no declared output.</summary>
    [Fact]
    public void SideEffectsAndExplicitDependenciesAreRetained()
    {
        var graph = new RenderGraph();
        using var allocation = new TestBuffer();
        RenderGraphBuffer<uint> imported = graph.ImportBuffer(allocation, default);
        RenderGraphPass prerequisite = graph.AddPass("prerequisite", _ => { });
        graph.AddPass("external-write", _ => { }).UseBuffer(imported, CopyWrite).DependsOn(prerequisite);
        graph.AddPass("effect", _ => { }).KeepAlive();
        graph.AddPass("dead", _ => { });
        using CompiledRenderGraph plan = graph.Compile();
        Assert.Equal(new[] { "prerequisite", "external-write", "effect" }, plan.PassNames);
        Assert.Equal(new[] { "dead" }, plan.CulledPassNames);
    }

    /// <summary>Checks temporary allocation contents must be initialized before a live read.</summary>
    [Fact]
    public void UninitializedTemporaryReadsAndOutputsAreRejected()
    {
        var graph = new RenderGraph(new TestDevice());
        RenderGraphBuffer<uint> buffer = graph.CreateBuffer(new BufferDesc<uint> { Count = 4, Usage = BufferUsage.CopySource });
        graph.AddPass("read", _ => { }).UseBuffer(buffer, CopyRead).KeepAlive();
        Assert.Throws<InvalidOperationException>(() => graph.Compile());
        var empty = new RenderGraph(new TestDevice());
        RenderGraphBuffer<uint> output = empty.CreateBuffer(new BufferDesc<uint> { Count = 4, Usage = BufferUsage.CopySource });
        empty.MarkOutput(output);
        Assert.Throws<InvalidOperationException>(() => empty.Compile());
    }

    /// <summary>Checks all owned views are released before their texture allocations.</summary>
    [Fact]
    public void ViewsAndAllocationsRemainAliveUntilCompletedOrDiscarded()
    {
        var device = new TestDevice();
        var graph = new RenderGraph(device);
        RenderGraphTexture texture = graph.CreateTexture(new TextureDesc { Width = 4, Height = 4, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.RenderAttachment });
        graph.AddPass("write", context => context.CreateTextureView(texture)).UseTexture(texture, TextureState.ColorAttachment, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite));
        graph.MarkOutput(texture);
        using CompiledRenderGraph plan = graph.Compile();
        Assert.Throws<InvalidOperationException>(() => plan.GetTexture(texture));
        using var commands = new RecordingCommands();
        plan.Record(commands);
        Assert.NotNull(plan.GetTexture(texture));
        commands.Submit();
        Assert.Throws<InvalidOperationException>(plan.Dispose);
        Assert.Empty(device.Lifetime);
        commands.Complete();
        plan.Dispose();
        Assert.Equal(new[] { "view", "texture" }, device.Lifetime);
        Assert.Throws<ObjectDisposedException>(() => plan.GetTexture(texture));
    }

    /// <summary>Checks allocations are separately owned by independent compilations of one builder.</summary>
    [Fact]
    public void CompiledPlansDoNotShareTemporaryAllocations()
    {
        var device = new TestDevice();
        var graph = new RenderGraph(device);
        RenderGraphBuffer<uint> buffer = graph.CreateBuffer(new BufferDesc<uint> { Count = 4, Usage = BufferUsage.CopyDestination });
        graph.AddPass("write", _ => { }).UseBuffer(buffer, CopyWrite);
        graph.MarkOutput(buffer);
        using CompiledRenderGraph first = graph.Compile();
        using CompiledRenderGraph second = graph.Compile();
        using var a = new RecordingCommands();
        using var b = new RecordingCommands();
        first.Record(a);
        second.Record(b);
        Assert.NotSame(first.GetBuffer(buffer), second.GetBuffer(buffer));
        Assert.Equal(2, device.Buffers);
        a.Complete();
        b.Complete();
    }

    /// <summary>Checks partial allocation failure can be cleaned up after discarding the recording.</summary>
    [Fact]
    public void FailedAllocationDoesNotLeakPreviouslyCreatedResources()
    {
        var device = new TestDevice { FailureOnAllocation = 2 };
        var graph = new RenderGraph(device);
        RenderGraphBuffer<uint> first = graph.CreateBuffer(new BufferDesc<uint> { Count = 4, Usage = BufferUsage.CopyDestination });
        RenderGraphBuffer<uint> second = graph.CreateBuffer(new BufferDesc<uint> { Count = 4, Usage = BufferUsage.CopyDestination });
        graph.AddPass("write", _ => { }).UseBuffer(first, CopyWrite).UseBuffer(second, CopyWrite).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new RecordingCommands();
        Assert.Throws<InvalidOperationException>(() => plan.Record(commands));
        Assert.Throws<InvalidOperationException>(plan.Dispose);
        commands.Dispose();
        plan.Dispose();
        Assert.Equal(new[] { "buffer" }, device.Lifetime);
    }

    /// <summary>Checks exporting a borrowed frame image inserts a final Present transition.</summary>
    [Fact]
    public void PresentationExportsInsertAFinalBarrierWithoutSubmitting()
    {
        var graph = new RenderGraph();
        using var allocation = new TestTexture();
        RenderGraphTexture frame = graph.ImportTexture(allocation, TextureState.Undefined, default);
        graph.AddPass("color", _ => { }).UseTexture(frame, TextureState.ColorAttachment, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite));
        graph.ExportTexture(frame, TextureState.Present, default);
        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new RecordingCommands();
        plan.Record(commands);
        TextureBarrierDesc barrier = Assert.IsType<TextureBarrierDesc>(commands.Events[^1]);
        Assert.Equal(TextureState.ColorAttachment, barrier.BeforeState);
        Assert.Equal(TextureState.Present, barrier.AfterState);
        Assert.Equal(default, barrier.After);
        Assert.Equal(TextureState.Present, plan.GetFinalTextureState(frame));
        Assert.Equal(CommandBufferState.Recording, commands.State);
        commands.Complete();
    }

    /// <summary>Checks a final external reader stage is included in the preceding writer dependency.</summary>
    [Fact]
    public void ExportReadersAreIncludedInVisibilityPlanning()
    {
        var graph = new RenderGraph();
        using var allocation = new TestTexture();
        RenderGraphTexture texture = graph.ImportTexture(allocation, TextureState.ColorAttachment, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite));
        graph.AddPass("sample", _ => { }).UseTexture(texture, TextureState.Sampled, ShaderRead).KeepAlive();
        graph.ExportTexture(texture, TextureState.Sampled, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead));
        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new RecordingCommands();
        plan.Record(commands);
        TextureBarrierDesc barrier = Assert.IsType<TextureBarrierDesc>(Assert.Single(commands.Events));
        Assert.Equal(PipelineStage.FragmentShader | PipelineStage.ComputeShader, barrier.After.Stages);
        commands.Complete();
    }

    /// <summary>Checks exported readback is synchronized for host access after GPU completion.</summary>
    [Fact]
    public void BufferExportAddsHostReadDependency()
    {
        var graph = new RenderGraph(new TestDevice());
        RenderGraphBuffer<byte> readback = graph.CreateBuffer(new BufferDesc<byte> { Count = 64, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        graph.AddPass("readback", _ => { }).UseBuffer(readback, CopyWrite);
        var host = new BarrierScope(PipelineStage.Host, ResourceAccess.HostRead);
        graph.ExportBuffer(readback, host);
        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new RecordingCommands();
        plan.Record(commands);
        BufferBarrierDesc<byte> barrier = Assert.IsType<BufferBarrierDesc<byte>>(Assert.Single(commands.Events));
        Assert.Equal(CopyWrite, barrier.Before);
        Assert.Equal(host, barrier.After);
        Assert.Equal(host, plan.GetFinalScope(readback));
        commands.Complete();
    }

    /// <summary>Checks shader data barriers use the typed metadata-preserving command overload.</summary>
    [Fact]
    public void ShaderDataIsResolvedAndSynchronizedThroughItsOwnApi()
    {
        var graph = new RenderGraph();
        using var allocation = new TestShaderDataBuffer();
        RenderGraphShaderDataBuffer<TestShaderData> buffer = graph.ImportShaderDataBuffer(allocation, CopyWrite);
        graph.AddPass("shader", context => Assert.Same(allocation, context.GetShaderDataBuffer(buffer)))
            .UseShaderDataBuffer(buffer, ShaderRead).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new RecordingCommands();
        plan.Record(commands);
        ShaderDataBufferBarrierDesc<TestShaderData> barrier = Assert.IsType<ShaderDataBufferBarrierDesc<TestShaderData>>(Assert.Single(commands.Events));
        Assert.Same(allocation, barrier.Buffer.Buffer);
        Assert.Equal(4UL, barrier.Buffer.Count);
        Assert.Equal(CopyWrite, barrier.Before);
        Assert.Equal(ShaderRead, barrier.After);
        commands.Complete();
    }
}
