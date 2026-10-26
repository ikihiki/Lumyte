using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.RenderGraph.Tests;

/// <summary>Checks optional capture does not change execution or retain mutable builder descriptions.</summary>
public sealed class RenderGraphCaptureTests
{
    /// <summary>Checks disabled diagnostics skip every callback while compiling and recording normally.</summary>
    [Fact]
    public void DisabledObserverIsNotInvoked()
    {
        var graph = new RenderGraph(observer: new DisabledObserver());
        graph.AddPass("work", _ => { }).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        Assert.Null(plan.Snapshot);
        using var commands = new RecordingCommands();
        plan.Record(commands);
        commands.Complete();
    }

    /// <summary>Checks captures remain detached and normal compilations do not create detailed metadata.</summary>
    [Fact]
    public void ExplicitCaptureFreezesOneCompilation()
    {
        var graph = new RenderGraph(new TestDevice(), "capture");
        RenderGraphBuffer<uint> buffer = graph.CreateBuffer<uint>(new() { Count = 4, Usage = BufferUsage.CopyDestination | BufferUsage.CopySource }, "data");
        graph.AddPass("produce", _ => { }).UseBuffer(buffer, new(PipelineStage.Copy, ResourceAccess.CopyWrite));
        graph.AddPass("read", _ => { }).UseBuffer(buffer, new(PipelineStage.Copy, ResourceAccess.CopyRead)).KeepAlive();
        graph.AddPass("dead", _ => { });
        using CompiledRenderGraph ordinary = graph.Compile(100);
        using CompiledRenderGraph captured = graph.Compile(101, new());
        Assert.Null(ordinary.Snapshot);
        RenderGraphSnapshot snapshot = Assert.IsType<RenderGraphSnapshot>(captured.Snapshot);
        Assert.Equal(ordinary.Statistics, captured.Statistics);
        Assert.Equal(2, snapshot.Passes.Count(pass => pass.ExecutionIndex >= 0));
        Assert.Equal(-1, snapshot.Passes[2].ExecutionIndex);
        RenderGraphResourceInfo resource = Assert.Single(snapshot.Resources);
        Assert.Equal(0, resource.FirstPass);
        Assert.Equal(1, resource.LastPass);
        Assert.Single(snapshot.Dependencies);
        Assert.Single(snapshot.Barriers);
        Assert.Equal(graph.Id, captured.Run.GraphId);
        Assert.Equal(ordinary.Run.GraphId, captured.Run.GraphId);
        Assert.NotEqual(ordinary.Run.RunId, captured.Run.RunId);
        Assert.Equal(101, captured.Run.FrameId);
        graph.AddPass("later", _ => { }).KeepAlive();
        using CompiledRenderGraph next = graph.Compile(102);
        Assert.Equal(3, next.Statistics.LivePasses);
        Assert.Equal(3, snapshot.Passes.Count);
        Assert.Equal(2, captured.Statistics.LivePasses);
    }

    /// <summary>Checks capture budgeting rejects metadata without rejecting a valid executable plan.</summary>
    [Fact]
    public void SnapshotBudgetIsCheckedBeforeMaterializingDescriptions()
    {
        var graph = new RenderGraph();
        graph.AddPass("work", _ => { }).KeepAlive();
        using CompiledRenderGraph reference = graph.Compile(capture: new());
        long bytes = Assert.IsType<RenderGraphSnapshot>(reference.Snapshot).EstimatedBytes;
        using CompiledRenderGraph rejected = graph.Compile(capture: new() { MaxBytes = bytes - 1 });
        using CompiledRenderGraph accepted = graph.Compile(capture: new() { MaxBytes = bytes });
        Assert.Null(rejected.Snapshot);
        Assert.NotNull(accepted.Snapshot);
        Assert.Equal(reference.PassNames, rejected.PassNames);
        using var commands = new RecordingCommands();
        rejected.Record(commands);
        commands.Complete();
        Assert.Throws<ArgumentOutOfRangeException>(() => graph.Compile(capture: new() { MaxBytes = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => graph.Compile(-1));
    }

    private sealed class DisabledObserver : IRenderGraphObserver
    {
        public bool IsEnabled => false;

        public RenderGraphCaptureOptions? RequestCapture(RenderGraphRun run) => throw new InvalidOperationException();

        public IDisposable? Begin(RenderGraphRun run, string operation) => throw new InvalidOperationException();

        public void Compiled(RenderGraphRun run, RenderGraphStatistics statistics, RenderGraphSnapshot? snapshot, double durationMilliseconds) => throw new InvalidOperationException();

        public void Recorded(RenderGraphRun run, double durationMilliseconds) => throw new InvalidOperationException();

        public void Failed(RenderGraphRun run, string operation, Exception exception) => throw new InvalidOperationException();
    }
}
