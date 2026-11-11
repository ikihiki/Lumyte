using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.RenderGraph.Tests;

/// <summary>Exercises the actual graph EventSource with real recording and lifetime boundaries.</summary>
[Collection("Graph profiling events")]
public sealed class RenderGraphProfilingTests
{
    /// <summary>Distinguishes multiple unattributed graphs by mapping root spans to their graph runs.</summary>
    [Fact]
    public void UnattributedGraphsHaveIndependentRootMappingsAndCounters()
    {
        using var listener = new Capture(0xF);
        var first = new RenderGraph(name: "profile-one");
        first.AddPass("live", _ => { }).KeepAlive();
        first.AddPass("culled", _ => { });
        var second = new RenderGraph(name: "profile-two");
        second.AddPass("live", _ => { }).KeepAlive();
        using CompiledRenderGraph one = first.Compile(42);
        using CompiledRenderGraph two = second.Compile(42);
        using var commands = new RecordingCommands();
        one.Record(commands);
        commands.Complete();
        one.Dispose();
        Entry[] entries = listener.Entries.ToArray();
        Assert.DoesNotContain(entries, entry => entry.Id == 0);
        Entry relationOne = Assert.Single(entries, entry => entry.Id == 4 && entry.Guid("graphRunId") == one.Run.RunId && entry.Number("operation") == 1);
        Entry relationTwo = Assert.Single(entries, entry => entry.Id == 4 && entry.Guid("graphRunId") == two.Run.RunId && entry.Number("operation") == 1);
        Assert.NotEqual(relationOne.Number("rootSpanId"), relationTwo.Number("rootSpanId"));
        Assert.Equal(Guid.Empty, relationOne.Guid("profileRunId"));
        Assert.Equal(0, relationOne.Number("frameId"));
        long root = relationOne.Number("rootSpanId");
        Assert.Contains(entries, entry => entry.Id == 1 && entry.Number("spanId") == root && entry.Number("stage") == 1);
        Assert.Contains(entries, entry => entry.Id == 3 && entry.Number("spanId") == root && entry.Number("counterId") == 1 && entry.Number("value") == 1);
        Assert.Contains(entries, entry => entry.Id == 3 && entry.Number("spanId") == root && entry.Number("counterId") == 2 && entry.Number("value") == 1);
        Assert.Contains(entries, entry => entry.Id == 7 && entry.Guid("graphRunId") == one.Run.RunId && entry.Number("operation") == 2 && entry.Number("outcome") == 0);
    }

    /// <summary>Collects pass intervals even when no ordinary stage or control keyword is selected.</summary>
    [Fact]
    public void PassOnlyKeywordRecordsCallbacks()
    {
        using var listener = new Capture(0x10);
        var graph = new RenderGraph();
        graph.AddPass("live", _ => { }).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new RecordingCommands();
        plan.Record(commands);
        commands.Complete();
        Assert.Single(listener.Entries, entry => entry.Id == 5);
        Assert.Single(listener.Entries, entry => entry.Id == 6);
        Assert.DoesNotContain(listener.Entries, entry => entry.Id is 1 or 2 or 3 or 4);
    }

    /// <summary>Reports transient creation cancellation consistently on the allocation and record stages.</summary>
    [Fact]
    public void TransientAllocationCancellationClosesEveryAffectedStage()
    {
        using var listener = new Capture(0xF);
        var expected = new OperationCanceledException("allocate-cancel");
        var device = new TestDevice { AllocationException = expected };
        var graph = new RenderGraph(device);
        RenderGraphBuffer<uint> temporary = graph.CreateBuffer<uint>(new() { Count = 4, Usage = BufferUsage.CopyDestination });
        graph.AddPass("allocate", _ => { }).UseBuffer(temporary, new(PipelineStage.Copy, ResourceAccess.CopyWrite)).KeepAlive();
        CompiledRenderGraph plan = graph.Compile();
        using var commands = new RecordingCommands();
        Assert.Same(expected, Assert.Throws<OperationCanceledException>(() => plan.Record(commands)));
        commands.Dispose();
        plan.Dispose();
        Assert.Contains(listener.Entries, entry => entry.Id == 2 && entry.Number("stage") == 6 && entry.Number("outcome") == 1);
        Assert.Contains(listener.Entries, entry => entry.Id == 2 && entry.Number("stage") == 5 && entry.Number("outcome") == 1);
    }

    /// <summary>Checks emitted metadata keeps pass events out of a simultaneous normal collector.</summary>
    [Fact]
    public void PassDetailsRequireTheirOwnKeywordWithConcurrentListeners()
    {
        using var normal = new Capture(0xF);
        using var detail = new Capture(0x1F);
        var graph = new RenderGraph();
        graph.AddPass("live", _ => { }).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new RecordingCommands();
        plan.Record(commands);
        commands.Complete();
        Assert.DoesNotContain(normal.Entries, entry => entry.Id is 5 or 6);
        Assert.Single(detail.Entries, entry => entry.Id == 5);
        Assert.Single(detail.Entries, entry => entry.Id == 6);
        Assert.DoesNotContain(detail.Entries, entry => entry.Id == 0);
    }

    /// <summary>Preserves the original callback cancellation and records a cancelled root and pass.</summary>
    [Fact]
    public void CallbackCancellationClosesRootAndPassWithoutChangingLifetime()
    {
        using var listener = new Capture(0x1F);
        var graph = new RenderGraph();
        var expected = new OperationCanceledException("profile-cancel");
        graph.AddPass("cancel", _ => throw expected).KeepAlive();
        CompiledRenderGraph plan = graph.Compile();
        using var commands = new RecordingCommands();
        Assert.Same(expected, Assert.Throws<OperationCanceledException>(() => plan.Record(commands)));
        Assert.Throws<InvalidOperationException>(plan.Dispose);
        commands.Dispose();
        plan.Dispose();
        Entry[] entries = listener.Entries.ToArray();
        Assert.Contains(entries, entry => entry.Id == 2 && entry.Number("stage") == 5 && entry.Number("outcome") == 1);
        Assert.Contains(entries, entry => entry.Id == 6 && entry.Number("outcome") == 1);
        Assert.Contains(entries, entry => entry.Id == 7 && entry.Guid("graphRunId") == plan.Run.RunId && entry.Number("operation") == 2 && entry.Number("outcome") == 1);
    }

    /// <summary>Uses compile-time correlation after recording on another thread under a different Activity.</summary>
    /// <returns>The asynchronous recording verification.</returns>
    [Fact]
    public async Task DelayedRecordRetainsCompileCorrelationAcrossThreadsAsync()
    {
        using var listener = new Capture(0xF);
        var runId = Guid.NewGuid();
        CompiledRenderGraph plan;
        using (var frame = new Activity("profile-frame"))
        {
            frame.SetCustomProperty("Lumyte.Profiling.RunId", runId);
            frame.SetCustomProperty("Lumyte.Profiling.FrameId", 87L);
            frame.Start();
            using Activity logicalChild = new Activity("logical-child").Start();
            var graph = new RenderGraph();
            graph.AddPass("live", _ => { }).KeepAlive();
            plan = graph.Compile(87);
        }

        await Task.Run(() =>
        {
            using var unrelated = new Activity("unrelated-frame");
            unrelated.SetCustomProperty("Lumyte.Profiling.RunId", Guid.NewGuid());
            unrelated.SetCustomProperty("Lumyte.Profiling.FrameId", 900L);
            unrelated.Start();
            using var commands = new RecordingCommands();
            plan.Record(commands);
            commands.Complete();
            plan.Dispose();
        });
        Entry record = Assert.Single(listener.Entries, entry => entry.Id == 4 && entry.Guid("graphRunId") == plan.Run.RunId && entry.Number("operation") == 2);
        Assert.Equal(runId, record.Guid("profileRunId"));
        Assert.Equal(87, record.Number("frameId"));
        Assert.Equal(1, record.Number("frameMatch"));
    }

    /// <summary>Keeps successful compile work separate from a later cancelled ownership transfer.</summary>
    [Fact]
    public void CancelledLifetimeTransferDoesNotRewriteSuccessfulCompileStage()
    {
        using var listener = new Capture(0xF);
        var lifetime = new RetryLifetime();
        var graph = new RenderGraph();
        graph.Retain(lifetime);
        graph.AddPass("live", _ => { }).KeepAlive();
        lifetime.CancelValidation = true;
        Assert.Throws<OperationCanceledException>(() => graph.Compile());
        Assert.Contains(listener.Entries, entry => entry.Id == 2 && entry.Number("stage") == 1 && entry.Number("outcome") == 0);
        Assert.Contains(listener.Entries, entry => entry.Id == 7 && entry.Number("operation") == 1 && entry.Number("outcome") == 1);
        lifetime.CancelValidation = false;
        graph.Dispose();
    }

    /// <summary>Reports only successful releases on each retry without changing the plan's retry contract.</summary>
    [Fact]
    public void DisposeRetryCountersArePerAttempt()
    {
        using var listener = new Capture(0xF);
        var lifetime = new RetryLifetime { FailDisposal = true };
        var graph = new RenderGraph();
        graph.Retain(lifetime);
        graph.AddPass("live", _ => { }).KeepAlive();
        CompiledRenderGraph plan = graph.Compile();
        using var commands = new RecordingCommands();
        plan.Record(commands);
        commands.Complete();
        Assert.Throws<InvalidOperationException>(plan.Dispose);
        plan.Dispose();
        Entry[] results = listener.Entries.Where(entry => entry.Id == 7 && entry.Guid("graphRunId") == plan.Run.RunId && entry.Number("operation") == 3).ToArray();
        Assert.Equal(2, results.Length);
        Assert.Equal(2, results[0].Number("outcome"));
        Assert.Equal(0, results[1].Number("outcome"));
        Assert.Contains(listener.Entries, entry => entry.Id == 3 && entry.Number("spanId") == results[0].Number("rootSpanId") && entry.Number("counterId") == 12 && entry.Number("value") == 0);
        Assert.Contains(listener.Entries, entry => entry.Id == 3 && entry.Number("spanId") == results[1].Number("rootSpanId") && entry.Number("counterId") == 12 && entry.Number("value") == 1);
    }

    /// <summary>Does not attach a previously unobserved plan to a different frame at late subscription.</summary>
    [Fact]
    public void LateSubscriptionKeepsUnobservedPlanUnattributed()
    {
        var graph = new RenderGraph();
        graph.AddPass("live", _ => { }).KeepAlive();
        CompiledRenderGraph plan = graph.Compile(100);
        using var listener = new Capture(0xF);
        using var unrelated = new Activity("late-frame");
        unrelated.SetCustomProperty("Lumyte.Profiling.RunId", Guid.NewGuid());
        unrelated.SetCustomProperty("Lumyte.Profiling.FrameId", 200L);
        unrelated.Start();
        using var commands = new RecordingCommands();
        plan.Record(commands);
        commands.Complete();
        plan.Dispose();
        Entry relation = Assert.Single(listener.Entries, entry => entry.Id == 4 && entry.Guid("graphRunId") == plan.Run.RunId && entry.Number("operation") == 2);
        Assert.Equal(Guid.Empty, relation.Guid("profileRunId"));
        Assert.Equal(0, relation.Number("frameId"));
    }

    private sealed class RetryLifetime : IRenderGraphLifetime
    {
        private bool _disposalFailed;

        internal bool CancelValidation { get; set; }

        internal bool FailDisposal { get; init; }

        public void Validate()
        {
            if (CancelValidation)
            {
                throw new OperationCanceledException("profile-transfer");
            }
        }

        public void Dispose()
        {
            if (FailDisposal && !_disposalFailed)
            {
                _disposalFailed = true;
                throw new InvalidOperationException("profile-cleanup");
            }
        }
    }

    /// <summary>Captures only the provider under test without reaching through friend assembly access.</summary>
    private sealed class Capture : EventListener
    {
        private readonly EventKeywords _keywords;
        private bool _ready;

        internal Capture(long keywords)
        {
            _keywords = (EventKeywords)keywords;
            _ready = true;
            foreach (EventSource source in EventSource.GetSources())
            {
                Enable(source);
            }
        }

        internal ConcurrentQueue<Entry> Entries { get; } = new();

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (_ready)
            {
                Enable(eventSource);
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
            => Entries.Enqueue(new(eventData.EventId, eventData.PayloadNames?.ToArray() ?? [], eventData.Payload?.ToArray() ?? []));

        private void Enable(EventSource source)
        {
            if (source.Name == "Lumyte-RenderGraph-Profiling")
            {
                EnableEvents(source, EventLevel.Informational, _keywords);
            }
        }
    }

    private sealed record Entry(int Id, string[] Names, object?[] Values)
    {
        internal long Number(string name) => Convert.ToInt64(Values[Array.IndexOf(Names, name)]);

        internal Guid Guid(string name) => (Guid)Values[Array.IndexOf(Names, name)]!;
    }
}
