using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.RenderGraph.Tests;

/// <summary>Checks partial updates and synchronization across independently compiled graphs.</summary>
public sealed class ReviewRegressionTests
{
    private static BarrierScope CopyRead => new(PipelineStage.Copy, ResourceAccess.CopyRead);

    private static BarrierScope CopyWrite => new(PipelineStage.Copy, ResourceAccess.CopyWrite);

    private static BarrierScope FragmentRead => new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead);

    private static BarrierScope ComputeRead => new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead);

    /// <summary>Checks writes to separate buffer ranges retain every producer of the output.</summary>
    [Fact]
    public void PartialBufferUpdatesRetainPreviousWriters()
    {
        var graph = new RenderGraph(new TestDevice());
        using var source = new TestBuffer();
        RenderGraphBuffer<uint> imported = graph.ImportBuffer(source, CopyRead);
        RenderGraphBuffer<uint> output = graph.CreateBuffer(new BufferDesc<uint> { Count = 4, Usage = BufferUsage.CopyDestination | BufferUsage.CopySource });
        graph.AddPass("first-half", context => context.Commands.CopyBuffer(context.GetBuffer(imported).Slice(0, 2), context.GetBuffer(output).Slice(0, 2)))
            .UseBuffer(imported, CopyRead).UseBuffer(output, CopyWrite);
        graph.AddPass("second-half", context => context.Commands.CopyBuffer(context.GetBuffer(imported).Slice(2, 2), context.GetBuffer(output).Slice(2, 2)))
            .UseBuffer(imported, CopyRead).UseBuffer(output, CopyWrite);
        graph.ExportBuffer(output, CopyRead);
        using CompiledRenderGraph plan = graph.Compile();
        Assert.Equal(new[] { "first-half", "second-half" }, plan.PassNames);
        Assert.Empty(plan.CulledPassNames);
        using var commands = new RecordingCommands();
        plan.Record(commands);
        Assert.Equal(2, commands.Events.Count(item => item is string value && value == "copy"));
        commands.Complete();
    }

    /// <summary>Checks copies to separate array layers preserve the earlier layer producer.</summary>
    [Fact]
    public void PartialTextureUpdatesRetainPreviousWriters()
    {
        var graph = new RenderGraph(new TestDevice());
        using var source = new TestTexture { MipLevels = 1, ArrayLayers = 2 };
        RenderGraphTexture imported = graph.ImportTexture(source, TextureState.CopySource, CopyRead);
        RenderGraphTexture output = graph.CreateTexture(new TextureDesc
        {
            Width = 16,
            Height = 16,
            ArrayLayers = 2,
            Format = TextureFormat.Rgba8Unorm,
            Usage = TextureUsage.CopyDestination | TextureUsage.Sampled,
        });
        graph.AddPass("first-layer", context => context.Commands.CopyTexture(
            new() { Texture = context.GetTexture(imported), Width = 16, Height = 16 },
            new() { Texture = context.GetTexture(output), Width = 16, Height = 16 }))
            .UseTexture(imported, TextureState.CopySource, CopyRead).UseTexture(output, TextureState.CopyDestination, CopyWrite);
        graph.AddPass("second-layer", context => context.Commands.CopyTexture(
            new() { Texture = context.GetTexture(imported), Width = 16, Height = 16, BaseArrayLayer = 1 },
            new() { Texture = context.GetTexture(output), Width = 16, Height = 16, BaseArrayLayer = 1 }))
            .UseTexture(imported, TextureState.CopySource, CopyRead).UseTexture(output, TextureState.CopyDestination, CopyWrite);
        graph.ExportTexture(output, TextureState.Sampled, FragmentRead);
        using CompiledRenderGraph plan = graph.Compile();
        Assert.Equal(new[] { "first-layer", "second-layer" }, plan.PassNames);
        Assert.Empty(plan.CulledPassNames);
        using var commands = new RecordingCommands();
        plan.Record(commands);
        Assert.Equal(2, commands.Events.Count(item => item is string value && value == "texture-copy"));
        commands.Complete();
    }

    /// <summary>Checks discard is rejected for read-only and read/write scopes on each resource API.</summary>
    [Fact]
    public void DiscardRequiresWriteOnlyAccess()
    {
        var graph = new RenderGraph();
        using var textureAllocation = new TestTexture();
        using var bufferAllocation = new TestBuffer();
        using var shaderAllocation = new TestShaderDataBuffer();
        RenderGraphTexture texture = graph.ImportTexture(textureAllocation, TextureState.Undefined, default);
        RenderGraphBuffer<uint> buffer = graph.ImportBuffer(bufferAllocation, default);
        RenderGraphShaderDataBuffer<TestShaderData> shader = graph.ImportShaderDataBuffer(shaderAllocation, default);
        RenderGraphPass pass = graph.AddPass("invalid-discard", _ => { });
        Assert.Throws<ArgumentException>(() => pass.UseTexture(texture, TextureState.Sampled, FragmentRead, discard: true));
        Assert.Throws<ArgumentException>(() => pass.UseTexture(texture, TextureState.ColorAttachment, new(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite), discard: true));
        Assert.Throws<ArgumentException>(() => pass.UseBuffer(buffer, CopyRead, discard: true));
        Assert.Throws<ArgumentException>(() => pass.UseBuffer(buffer, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead | ResourceAccess.ShaderWrite), discard: true));
        Assert.Throws<ArgumentException>(() => pass.UseShaderDataBuffer(shader, FragmentRead, discard: true));
        Assert.Throws<ArgumentException>(() => pass.UseShaderDataBuffer(shader, new(PipelineStage.Copy | PipelineStage.FragmentShader, ResourceAccess.CopyWrite | ResourceAccess.ShaderRead), discard: true));
        pass.UseTexture(texture, TextureState.CopyDestination, CopyWrite, discard: true)
            .UseBuffer(buffer, CopyWrite, discard: true)
            .UseShaderDataBuffer(shader, CopyWrite, discard: true);
        using CompiledRenderGraph plan = graph.Compile();
        Assert.Equal(new[] { "invalid-discard" }, plan.PassNames);
    }

    /// <summary>Checks an imported read phase is synchronized when another shader stage joins it.</summary>
    [Fact]
    public void ImportedBufferReadersSynchronizeAdditionalStages()
    {
        var graph = new RenderGraph();
        using var allocation = new TestBuffer();
        RenderGraphBuffer<uint> buffer = graph.ImportBuffer(allocation, FragmentRead);
        graph.AddPass("compute", _ => { }).UseBuffer(buffer, ComputeRead).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new RecordingCommands();
        plan.Record(commands);
        BufferBarrierDesc<uint> barrier = Assert.IsType<BufferBarrierDesc<uint>>(Assert.Single(commands.Events));
        Assert.Equal(FragmentRead, barrier.Before);
        Assert.Equal(ComputeRead, barrier.After);
        Assert.Equal(new(PipelineStage.FragmentShader | PipelineStage.ComputeShader, ResourceAccess.ShaderRead), plan.GetFinalScope(buffer));
        commands.Complete();
    }

    /// <summary>Checks a write after reader expansion waits for both imported and current readers.</summary>
    [Fact]
    public void WritesAfterExpandedReadersRetainEveryPrecedingStage()
    {
        var graph = new RenderGraph();
        using var allocation = new TestBuffer();
        RenderGraphBuffer<uint> buffer = graph.ImportBuffer(allocation, FragmentRead);
        graph.AddPass("compute", _ => { }).UseBuffer(buffer, ComputeRead).KeepAlive();
        graph.AddPass("overwrite", _ => { }).UseBuffer(buffer, CopyWrite);
        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new RecordingCommands();
        plan.Record(commands);
        Assert.Equal(2, commands.Events.Count);
        BufferBarrierDesc<uint> write = Assert.IsType<BufferBarrierDesc<uint>>(commands.Events[1]);
        Assert.Equal(new(PipelineStage.FragmentShader | PipelineStage.ComputeShader, ResourceAccess.ShaderRead), write.Before);
        Assert.Equal(CopyWrite, write.After);
        commands.Complete();
    }

    /// <summary>Checks a different read access needs visibility even when stage flags do not expand.</summary>
    [Fact]
    public void ImportedBufferReadersSynchronizeAdditionalAccesses()
    {
        var graph = new RenderGraph();
        using var allocation = new TestBuffer();
        var before = new BarrierScope(PipelineStage.AllCommands, ResourceAccess.CopyRead);
        var after = new BarrierScope(PipelineStage.AllCommands, ResourceAccess.ShaderRead);
        RenderGraphBuffer<uint> buffer = graph.ImportBuffer(allocation, before);
        graph.AddPass("shader", _ => { }).UseBuffer(buffer, after).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new RecordingCommands();
        plan.Record(commands);
        BufferBarrierDesc<uint> barrier = Assert.IsType<BufferBarrierDesc<uint>>(Assert.Single(commands.Events));
        Assert.Equal(before, barrier.Before);
        Assert.Equal(after, barrier.After);
        commands.Complete();
    }

    /// <summary>Checks expanding sampled reader stages creates a dependency without changing layout.</summary>
    [Fact]
    public void ImportedTextureReadersSynchronizeWithoutAStateChange()
    {
        var graph = new RenderGraph();
        using var allocation = new TestTexture();
        RenderGraphTexture texture = graph.ImportTexture(allocation, TextureState.Sampled, FragmentRead);
        graph.AddPass("compute", _ => { }).UseTexture(texture, TextureState.Sampled, ComputeRead).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new RecordingCommands();
        plan.Record(commands);
        TextureBarrierDesc barrier = Assert.IsType<TextureBarrierDesc>(Assert.Single(commands.Events));
        Assert.Equal(TextureState.Sampled, barrier.BeforeState);
        Assert.Equal(TextureState.Sampled, barrier.AfterState);
        Assert.Equal(FragmentRead, barrier.Before);
        Assert.Equal(ComputeRead, barrier.After);
        commands.Complete();
    }

    /// <summary>Checks read scope expansion continues batching the current graph's reader phase.</summary>
    [Fact]
    public void ExpandedReadScopesStillBatchAllFollowingReaders()
    {
        var graph = new RenderGraph();
        using var allocation = new TestBuffer();
        RenderGraphBuffer<uint> buffer = graph.ImportBuffer(allocation, CopyRead);
        graph.AddPass("fragment", _ => { }).UseBuffer(buffer, FragmentRead).KeepAlive();
        graph.AddPass("compute", _ => { }).UseBuffer(buffer, ComputeRead).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new RecordingCommands();
        plan.Record(commands);
        BufferBarrierDesc<uint> barrier = Assert.IsType<BufferBarrierDesc<uint>>(Assert.Single(commands.Events));
        Assert.Equal(CopyRead, barrier.Before);
        Assert.Equal(new(PipelineStage.FragmentShader | PipelineStage.ComputeShader, ResourceAccess.ShaderRead), barrier.After);
        commands.Complete();
    }

    /// <summary>Checks final scope handoff preserves synchronization when two graphs share one recording.</summary>
    [Fact]
    public void ConsecutiveGraphsSynchronizeNewReadersAfterAnEarlierWriter()
    {
        using var allocation = new TestBuffer();
        var first = new RenderGraph();
        RenderGraphBuffer<uint> output = first.ImportBuffer(allocation, default);
        first.AddPass("copy", _ => { }).UseBuffer(output, CopyWrite);
        first.AddPass("fragment", _ => { }).UseBuffer(output, FragmentRead).KeepAlive();
        using CompiledRenderGraph firstPlan = first.Compile();
        var second = new RenderGraph();
        RenderGraphBuffer<uint> input = second.ImportBuffer(allocation, firstPlan.GetFinalScope(output));
        second.AddPass("compute", _ => { }).UseBuffer(input, ComputeRead).KeepAlive();
        using CompiledRenderGraph secondPlan = second.Compile();
        using var commands = new RecordingCommands();
        firstPlan.Record(commands);
        secondPlan.Record(commands);
        Assert.Equal(2, commands.Events.Count);
        BufferBarrierDesc<uint> firstBarrier = Assert.IsType<BufferBarrierDesc<uint>>(commands.Events[0]);
        BufferBarrierDesc<uint> secondBarrier = Assert.IsType<BufferBarrierDesc<uint>>(commands.Events[1]);
        Assert.Equal(CopyWrite, firstBarrier.Before);
        Assert.Equal(FragmentRead, firstBarrier.After);
        Assert.Equal(firstBarrier.After, secondBarrier.Before);
        Assert.Equal(ComputeRead, secondBarrier.After);
        commands.Complete();
    }
}
