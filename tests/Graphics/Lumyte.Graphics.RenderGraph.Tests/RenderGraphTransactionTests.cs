using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.RenderGraph.Tests;

/// <summary>Exercises detached append identity, atomic failure and coarse lifetime ownership.</summary>
public sealed class RenderGraphTransactionTests
{
    private static readonly BarrierScope _read = new(PipelineStage.Copy, ResourceAccess.CopyRead);
    private static readonly BarrierScope _write = new(PipelineStage.Copy, ResourceAccess.CopyWrite);

    /// <summary>Every injected commit boundary rolls back without consuming ownership or leaving uses.</summary>
    /// <param name="stage">The selected commit boundary.</param>
    /// <param name="retainExisting">Whether the existing coarse lifetime list must be preserved.</param>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public void CommitFailureRollsBackAndAllowsRetry(int stage, bool retainExisting)
    {
        var device = new TestDevice();
        using var graph = new RenderGraph(device);
        var existing = new Owner();
        if (retainExisting)
        {
            graph.Retain(existing);
        }

        var owner = new Owner();
        var texture = new TestTexture();
        RenderGraphTexture resource = graph.ImportTexture(texture, TextureState.CopySource, _read);
        graph.AddPass("existing", _ => { }).KeepAlive();
        RenderGraphPassUse[] uses = [RenderGraphPassUse.Texture(resource, TextureState.CopyDestination, _write)];
        Assert.Throws<InvalidOperationException>(() => graph.AppendTransactional("failed", device, _ => { }, uses, owner, boundary =>
        {
            if ((int)boundary == stage)
            {
                throw new InvalidOperationException("Injected append failure.");
            }
        }));
        Assert.True(graph.IsBuilding);
        Assert.Equal(0, owner.Disposals);
        using (CompiledRenderGraph unchanged = graph.Compile())
        {
            Assert.Equal(1, unchanged.Statistics.LivePasses);
            Assert.Equal(0, unchanged.Statistics.CulledPasses);
            Assert.Equal(0, unchanged.Statistics.Barriers);
        }

        // A retained existing owner transfers on compile; only owner preservation is checked in that branch.
        if (!retainExisting)
        {
            graph.AppendTransactional("retry", device, _ => { }, uses, owner);
            using CompiledRenderGraph compiled = graph.Compile();
            Assert.Equal(2, compiled.Statistics.LivePasses);
            graph.Dispose();
            Assert.Equal(0, owner.Disposals);
            compiled.Dispose();
            Assert.Equal(1, owner.Disposals);
        }
        else
        {
            Assert.Equal(1, existing.Disposals);
            Assert.Equal(0, owner.Disposals);
        }
    }

    /// <summary>Invalid declarations and lifetime validation fail before any graph append.</summary>
    [Fact]
    public void PreflightFailurePreservesGraphAndCallerOwner()
    {
        var device = new TestDevice();
        using var graph = new RenderGraph(device);
        RenderGraphTexture texture = graph.ImportTexture(new TestTexture(), TextureState.CopySource, _read);
        var owner = new Owner();
        Assert.Throws<ArgumentException>(() => graph.AppendTransactional("bad", device, _ => { }, [RenderGraphPassUse.Texture(texture, TextureState.CopySource, _read), RenderGraphPassUse.Texture(texture, TextureState.CopyDestination, _write)], owner));
        Assert.Throws<ArgumentException>(() => graph.AppendTransactional(" ", device, _ => { }, [], owner));
        Assert.Throws<ArgumentException>(() => graph.AppendTransactional("bad-device", new TestDevice(), _ => { }, [], owner));
        owner.Validation = () => throw new InvalidOperationException();
        Assert.Throws<InvalidOperationException>(() => graph.AppendTransactional("bad-owner", device, _ => { }, [], owner));
        owner.Validation = null;
        using CompiledRenderGraph compiled = graph.Compile();
        Assert.Equal(0, compiled.Statistics.LivePasses);
        Assert.Equal(0, compiled.Statistics.CulledPasses);
        Assert.Equal(0, owner.Disposals);
    }

    /// <summary>Native texture/view and typed range identity reject impostors and nonimports.</summary>
    [Fact]
    public void ImportedIdentityIsExactAndBuildingOnly()
    {
        var device = new TestDevice();
        using var graph = new RenderGraph(device);
        using var foreign = new RenderGraph(device);
        var allocation = new TestTexture();
        RenderGraphTexture texture = graph.ImportTexture(allocation, TextureState.CopySource, _read);
        Assert.Same(allocation, graph.GetImportedTexture(texture, device));
        using IGraphicsTextureView view = allocation.CreateView();
        Assert.Equal(view.Info, graph.GetImportedTextureView(texture, view, device));
        using IGraphicsTextureView impostor = new TestTexture().CreateView();
        Assert.Throws<ArgumentException>(() => graph.GetImportedTextureView(texture, impostor, device));
        Assert.Throws<ArgumentException>(() => foreign.GetImportedTexture(texture, device));
        Assert.Throws<ArgumentException>(() => graph.GetImportedTexture(graph.CreateTexture(new() { Width = 1, Height = 1, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.Sampled }), device));
        IGraphicsBuffer<int> buffer = device.CreateBuffer(new BufferDesc<int> { Count = 8, Usage = BufferUsage.CopySource });
        RenderGraphBuffer<int> handle = graph.ImportBuffer(buffer, _read);
        BufferSlice<int> slice = graph.GetImportedBuffer(handle, 2, 3, device);
        Assert.Same(buffer, slice.Buffer);
        Assert.Equal(2UL, slice.Offset);
        Assert.Equal(3UL, slice.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => graph.GetImportedBuffer(handle, 7, 2, device));
        Assert.Throws<ArgumentOutOfRangeException>(() => graph.GetImportedBuffer(handle, 0, 0, device));
        graph.Dispose();
        Assert.Throws<ObjectDisposedException>(() => graph.GetImportedTexture(texture, device));
    }

    /// <summary>Reentrant owner validation cannot mutate or dispose the graph during append.</summary>
    [Fact]
    public void ValidationBlocksReentrantMutation()
    {
        var device = new TestDevice();
        using var graph = new RenderGraph(device);
        var owner = new Owner
        {
            Validation = () =>
            {
                Assert.False(graph.IsBuilding);
                Assert.Throws<InvalidOperationException>(() => graph.AddPass("reentrant", _ => { }));
                Assert.Throws<InvalidOperationException>(() => graph.Compile());
                Assert.Throws<InvalidOperationException>(() => graph.Dispose());
            },
        };
        graph.AppendTransactional("accepted", device, _ => { }, [], owner);
        owner.Validation = null;
        graph.Dispose();
        Assert.Equal(1, owner.Disposals);
    }

    /// <summary>Committed imports preserve dependency ordering, one-shot recording and completion retention.</summary>
    [Fact]
    public void CommittedPassRecordsOnceAndRetainsThroughSubmission()
    {
        var device = new TestDevice();
        using var graph = new RenderGraph(device);
        var owner = new Owner();
        var order = new List<string>();
        RenderGraphTexture texture = graph.ImportTexture(new TestTexture(), TextureState.CopySource, _read);
        RenderGraphPass first = graph.AddPass("first", _ => order.Add("first")).KeepAlive();
        graph.AppendTransactional("pipeline", device, _ => order.Add("pipeline"), [RenderGraphPassUse.Texture(texture, TextureState.CopyDestination, _write)], owner).DependsOn(first);
        using CompiledRenderGraph compiled = graph.Compile();
        graph.Dispose();
        using var commands = new RecordingCommands();
        compiled.Record(commands);
        Assert.Equal(["first", "pipeline"], order);
        Assert.Throws<InvalidOperationException>(() => compiled.Record(commands));
        commands.Finish();
        commands.Submit();
        Assert.Throws<InvalidOperationException>(() => compiled.Dispose());
        Assert.Equal(0, owner.Disposals);
        commands.Complete();
        commands.Dispose();
        compiled.Dispose();
        Assert.Equal(1, owner.Disposals);
    }

    /// <summary>Culled callbacks never run and their lease survives until compiled disposal with retry.</summary>
    [Fact]
    public void CulledLifetimeTransfersAndCleanupRetries()
    {
        var device = new TestDevice();
        using var graph = new RenderGraph(device);
        int records = 0;
        var owner = new Owner { FailDisposeOnce = true };
        graph.AppendTransactional("culled", device, _ => records++, [], owner);
        using CompiledRenderGraph compiled = graph.Compile();
        graph.Dispose();
        Assert.Equal(0, owner.Disposals);
        using var commands = new RecordingCommands();
        compiled.Record(commands);
        Assert.Equal(0, records);
        Assert.Equal(1, compiled.Statistics.CulledPasses);
        commands.Dispose();
        Assert.Throws<InvalidOperationException>(() => compiled.Dispose());
        compiled.Dispose();
        Assert.Equal(2, owner.Disposals);
    }

    private sealed class Owner : IRenderGraphLifetime
    {
        internal int Disposals { get; private set; }

        internal Action? Validation { get; set; }

        internal bool FailDisposeOnce { get; init; }

        public void Validate() => Validation?.Invoke();

        public void Dispose()
        {
            Disposals++;
            if (FailDisposeOnce && Disposals == 1)
            {
                throw new InvalidOperationException("Retry cleanup.");
            }
        }
    }
}
