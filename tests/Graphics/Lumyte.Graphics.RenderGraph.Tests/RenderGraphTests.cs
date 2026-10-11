using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.RenderGraph.Tests;

/// <summary>Verifies graph compilation and synchronization without a GPU or native bindings.</summary>
public sealed class RenderGraphTests
{
    private static BarrierScope ColorWrite => new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite);

    private static BarrierScope CopyWrite => new(PipelineStage.Copy, ResourceAccess.CopyWrite);

    private static BarrierScope CopyRead => new(PipelineStage.Copy, ResourceAccess.CopyRead);

    private static BarrierScope FragmentRead => new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead);

    private static BarrierScope ComputeRead => new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead);

    /// <summary>Checks transitions, whole-allocation ranges and recording without ownership transfer.</summary>
    [Fact]
    public void TextureWritesAndReadsInsertTransitionsBeforeCallbacks()
    {
        var graph = new RenderGraph();
        using var allocation = new TestTexture();
        RenderGraphTexture texture = graph.ImportTexture(allocation, TextureState.Undefined, default);
        graph.AddPass("clear", context =>
        {
            Assert.Same(allocation, context.GetTexture(texture));
            ((RecordingCommands)context.Commands).Events.Add("clear");
        }).UseTexture(texture, TextureState.ColorAttachment, ColorWrite);
        graph.AddPass("sample", context => ((RecordingCommands)context.Commands).Events.Add("sample"))
            .UseTexture(texture, TextureState.Sampled, FragmentRead).KeepAlive();
        using var commands = new RecordingCommands();
        CompiledRenderGraph plan = graph.Compile();
        Assert.Empty(commands.Events);
        plan.Record(commands);
        Assert.Collection(
            commands.Events,
            entry =>
            {
                TextureBarrierDesc barrier = Assert.IsType<TextureBarrierDesc>(entry);
                Assert.Equal(TextureState.Undefined, barrier.BeforeState);
                Assert.Equal(TextureState.ColorAttachment, barrier.AfterState);
                Assert.Equal(new TextureSubresourceRange(0, 3, 0, 2), barrier.Range);
            },
            entry => Assert.Equal("clear", entry),
            entry =>
            {
                TextureBarrierDesc barrier = Assert.IsType<TextureBarrierDesc>(entry);
                Assert.Equal(TextureState.ColorAttachment, barrier.BeforeState);
                Assert.Equal(TextureState.Sampled, barrier.AfterState);
                Assert.Equal(ColorWrite, barrier.Before);
                Assert.Equal(FragmentRead, barrier.After);
            },
            entry => Assert.Equal("sample", entry));
        Assert.Equal(CommandBufferState.Recording, commands.State);
        Assert.False(allocation.Disposed);
    }

    /// <summary>Checks all preceding reader stages participate in the subsequent write dependency.</summary>
    [Fact]
    public void ReadOnlyPassesMergeScopesBeforeAWrite()
    {
        var graph = new RenderGraph();
        using var allocation = new TestBuffer();
        RenderGraphBuffer<uint> buffer = graph.ImportBuffer(allocation, default);
        graph.AddPass("fragment", _ => { }).UseBuffer(buffer, FragmentRead).KeepAlive();
        graph.AddPass("compute", _ => { }).UseBuffer(buffer, ComputeRead).KeepAlive();
        graph.AddPass("overwrite", _ => { }).UseBuffer(buffer, CopyWrite);
        using var commands = new RecordingCommands();
        graph.Compile().Record(commands);
        BufferBarrierDesc<uint> barrier = Assert.IsType<BufferBarrierDesc<uint>>(Assert.Single(commands.Events));
        Assert.Equal(PipelineStage.FragmentShader | PipelineStage.ComputeShader, barrier.Before.Stages);
        Assert.Equal(ResourceAccess.ShaderRead, barrier.Before.Access);
        Assert.Equal(CopyWrite, barrier.After);
        Assert.Same(allocation, barrier.Buffer.Buffer);
        Assert.Equal(16UL, barrier.Buffer.Count);
    }

    /// <summary>Checks a writer makes its results visible to every subsequent reader stage.</summary>
    [Fact]
    public void WriteBarrierTargetsEveryStageInTheFollowingReadPhase()
    {
        var graph = new RenderGraph();
        using var allocation = new TestBuffer();
        RenderGraphBuffer<uint> buffer = graph.ImportBuffer(allocation, CopyWrite);
        graph.AddPass("fragment", _ => { }).UseBuffer(buffer, FragmentRead).KeepAlive();
        graph.AddPass("unrelated", _ => { });
        graph.AddPass("compute", _ => { }).UseBuffer(buffer, ComputeRead).KeepAlive();
        graph.AddPass("overwrite", _ => { }).UseBuffer(buffer, CopyWrite);
        graph.AddPass("copy", _ => { }).UseBuffer(buffer, CopyRead).KeepAlive();
        using var commands = new RecordingCommands();
        graph.Compile().Record(commands);
        Assert.Equal(3, commands.Events.Count);
        BufferBarrierDesc<uint> first = Assert.IsType<BufferBarrierDesc<uint>>(commands.Events[0]);
        Assert.Equal(PipelineStage.FragmentShader | PipelineStage.ComputeShader, first.After.Stages);
        Assert.Equal(ResourceAccess.ShaderRead, first.After.Access);
        BufferBarrierDesc<uint> overwrite = Assert.IsType<BufferBarrierDesc<uint>>(commands.Events[1]);
        Assert.Equal(first.After, overwrite.Before);
        Assert.Equal(CopyWrite, overwrite.After);
        BufferBarrierDesc<uint> last = Assert.IsType<BufferBarrierDesc<uint>>(commands.Events[2]);
        Assert.Equal(CopyRead, last.After);
    }

    /// <summary>Checks final states can be carried to a subsequent graph without losing reader scopes.</summary>
    [Fact]
    public void FinalStateQueriesReturnPlannedStatesAndRejectForeignHandles()
    {
        var graph = new RenderGraph();
        using var allocation = new TestTexture();
        RenderGraphTexture texture = graph.ImportTexture(allocation, TextureState.ColorAttachment, ColorWrite);
        graph.AddPass("fragment", _ => { }).UseTexture(texture, TextureState.Sampled, FragmentRead).KeepAlive();
        graph.AddPass("compute", _ => { }).UseTexture(texture, TextureState.Sampled, ComputeRead).KeepAlive();
        CompiledRenderGraph plan = graph.Compile();
        Assert.Equal(TextureState.Sampled, plan.GetFinalTextureState(texture));
        Assert.Equal(new BarrierScope(PipelineStage.FragmentShader | PipelineStage.ComputeShader, ResourceAccess.ShaderRead), plan.GetFinalScope(texture));
        using var otherAllocation = new TestTexture();
        RenderGraphTexture foreign = new RenderGraph().ImportTexture(otherAllocation, TextureState.Undefined, default);
        Assert.Throws<ArgumentException>(() => plan.GetFinalScope(foreign));
        Assert.Throws<ArgumentException>(() => plan.GetFinalTextureState(foreign));
        using var unusedAllocation = new TestTexture();
        RenderGraphTexture unused = graph.ImportTexture(unusedAllocation, TextureState.CopySource, CopyRead);
        CompiledRenderGraph empty = graph.Compile();
        Assert.Equal(CopyRead, empty.GetFinalScope(unused));
        Assert.Equal(TextureState.CopySource, empty.GetFinalTextureState(unused));
    }

    /// <summary>Checks same-state writes still require a memory dependency.</summary>
    [Fact]
    public void ConsecutiveWritesInTheSameStateHaveABarrier()
    {
        var graph = new RenderGraph();
        using var allocation = new TestTexture();
        RenderGraphTexture texture = graph.ImportTexture(allocation, TextureState.ColorAttachment, ColorWrite);
        graph.AddPass("write", _ => { }).UseTexture(texture, TextureState.ColorAttachment, ColorWrite);
        using var commands = new RecordingCommands();
        graph.Compile().Record(commands);
        TextureBarrierDesc barrier = Assert.IsType<TextureBarrierDesc>(Assert.Single(commands.Events));
        Assert.Equal(barrier.BeforeState, barrier.AfterState);
        Assert.Equal(ColorWrite, barrier.Before);
    }

    /// <summary>Checks declaration order preserves RAW, WAR and WAW hazards.</summary>
    /// <param name="firstWrite">Whether the earlier access writes.</param>
    /// <param name="secondWrite">Whether the later access writes.</param>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ReversingAResourceHazardIsACycle(bool firstWrite, bool secondWrite)
    {
        var graph = new RenderGraph();
        using var allocation = new TestBuffer();
        RenderGraphBuffer<uint> buffer = graph.ImportBuffer(allocation, default);
        RenderGraphPass first = graph.AddPass("first", _ => { }).UseBuffer(buffer, firstWrite ? CopyWrite : CopyRead);
        RenderGraphPass second = graph.AddPass("second", _ => { }).UseBuffer(buffer, secondWrite ? CopyWrite : CopyRead);
        first.DependsOn(second);
        Assert.Throws<InvalidOperationException>(() => graph.Compile());
    }

    /// <summary>Checks read-only passes can be reordered by an explicit dependency.</summary>
    [Fact]
    public void IndependentPassesHaveAStableTopologicalOrder()
    {
        var graph = new RenderGraph();
        RenderGraphPass first = graph.AddPass("first", _ => { }).KeepAlive();
        graph.AddPass("independent", _ => { }).KeepAlive();
        RenderGraphPass last = graph.AddPass("last", _ => { }).KeepAlive();
        first.DependsOn(last);
        Assert.Equal(new[] { "independent", "last", "first" }, graph.Compile().PassNames);
    }

    /// <summary>Checks even two read-only texture states require layout ordering.</summary>
    [Fact]
    public void ReadOnlyLayoutChangesParticipateInDependencies()
    {
        var graph = new RenderGraph();
        using var allocation = new TestTexture();
        RenderGraphTexture texture = graph.ImportTexture(allocation, TextureState.Sampled, FragmentRead);
        RenderGraphPass first = graph.AddPass("sample", _ => { }).UseTexture(texture, TextureState.Sampled, FragmentRead).KeepAlive();
        RenderGraphPass second = graph.AddPass("copy", _ => { }).UseTexture(texture, TextureState.CopySource, CopyRead);
        first.DependsOn(second);
        Assert.Throws<InvalidOperationException>(() => graph.Compile());
    }

    /// <summary>Checks undefined contents cannot be read, including attachment load operations.</summary>
    [Fact]
    public void UndefinedTextureCannotBeReadBeforeAWrite()
    {
        var graph = new RenderGraph();
        using var allocation = new TestTexture();
        RenderGraphTexture texture = graph.ImportTexture(allocation, TextureState.Undefined, default);
        graph.AddPass("read", _ => { }).UseTexture(texture, TextureState.Sampled, FragmentRead).KeepAlive();
        Assert.Throws<InvalidOperationException>(() => graph.Compile());
    }

    /// <summary>Checks compiled uses and ordering do not retain mutable builder lists.</summary>
    [Fact]
    public void CompilationSnapshotsBuilderDeclarations()
    {
        var graph = new RenderGraph();
        using var allocation = new TestTexture();
        RenderGraphTexture texture = graph.ImportTexture(allocation, TextureState.Undefined, default);
        RenderGraphPass pass = graph.AddPass("original", _ => { }).KeepAlive();
        CompiledRenderGraph plan = graph.Compile();
        pass.UseTexture(texture, TextureState.ColorAttachment, ColorWrite);
        graph.AddPass("added", _ => throw new InvalidOperationException());
        using var commands = new RecordingCommands();
        plan.Record(commands);
        Assert.Equal(new[] { "original" }, plan.PassNames);
        Assert.Empty(commands.Events);
    }

    /// <summary>Checks failed callbacks invalidate both the plan and captured pass context.</summary>
    [Fact]
    public void FailedRecordingCannotBeRetriedAndClosesItsContext()
    {
        var graph = new RenderGraph();
        RenderGraphContext? captured = null;
        graph.AddPass("fail", context =>
        {
            captured = context;
            throw new InvalidOperationException("failure");
        }).KeepAlive();
        CompiledRenderGraph plan = graph.Compile();
        using var commands = new RecordingCommands();
        Assert.Throws<InvalidOperationException>(() => plan.Record(commands));
        Assert.NotNull(captured);
        Assert.Throws<InvalidOperationException>(() => captured.Commands);
        Assert.Throws<InvalidOperationException>(() => plan.Record(commands));
    }

    /// <summary>Checks no resource can be resolved without a corresponding access declaration.</summary>
    [Fact]
    public void PassCannotResolveAnUndeclaredResource()
    {
        var graph = new RenderGraph();
        using var allocation = new TestBuffer();
        RenderGraphBuffer<uint> buffer = graph.ImportBuffer(allocation, default);
        graph.AddPass("undeclared", context => context.GetBuffer(buffer)).KeepAlive();
        using var commands = new RecordingCommands();
        Assert.Throws<ArgumentException>(() => graph.Compile().Record(commands));
    }

    /// <summary>Checks validation of aliases, graph identity, duplicate uses and access stages.</summary>
    [Fact]
    public void InvalidResourceDeclarationsAreRejected()
    {
        var graph = new RenderGraph();
        var other = new RenderGraph();
        using var allocation = new TestTexture();
        RenderGraphTexture texture = graph.ImportTexture(allocation, TextureState.Undefined, default);
        Assert.Throws<ArgumentException>(() => graph.ImportTexture(allocation, TextureState.Undefined, default));
        RenderGraphPass pass = graph.AddPass("pass", _ => { });
        Assert.Throws<ArgumentException>(() => other.AddPass("foreign", _ => { }).UseTexture(texture, TextureState.ColorAttachment, ColorWrite));
        Assert.Throws<ArgumentException>(() => pass.UseTexture(texture, TextureState.ColorAttachment, default));
        Assert.Throws<ArgumentException>(() => pass.UseTexture(texture, TextureState.Sampled, ColorWrite));
        Assert.Throws<ArgumentException>(() => pass.UseTexture(texture, TextureState.ColorAttachment, new BarrierScope(PipelineStage.Copy, ResourceAccess.ColorWrite)));
        pass.UseTexture(texture, TextureState.ColorAttachment, ColorWrite);
        Assert.Throws<ArgumentException>(() => pass.UseTexture(texture, TextureState.ColorAttachment, ColorWrite));
        Assert.Throws<ArgumentException>(() => pass.DependsOn(pass));
        Assert.Throws<ArgumentException>(() => pass.DependsOn(other.AddPass("other", _ => { })));
    }

    /// <summary>Checks rejected command state does not consume a plan before any recording.</summary>
    [Fact]
    public void RecordingIsOneShotAndRequiresARecordingCommandBuffer()
    {
        CompiledRenderGraph plan = new RenderGraph().Compile();
        using var executable = new RecordingCommands();
        executable.Finish();
        Assert.Throws<InvalidOperationException>(() => plan.Record(executable));
        using var recording = new RecordingCommands();
        plan.Record(recording);
        Assert.Throws<InvalidOperationException>(() => plan.Record(recording));
    }
}
