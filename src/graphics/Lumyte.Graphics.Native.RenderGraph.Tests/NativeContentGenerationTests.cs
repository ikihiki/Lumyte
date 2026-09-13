using Lumyte.Graphics.Native.Resources;
using Lumyte.Graphics.Native.Resources.Tests;
using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Native.RenderGraph.Tests;

public sealed class NativeContentGenerationTests
{
    [Fact]
    public async Task PendingAcceptedContentsCanBeReadByTheNextSubmission()
    {
        var backend = new TestResourceBackend { AutoComplete = false };
        var scenario = new Scenario();
        await using var runtime = await NativeGraphPlanningTests.Create(backend, scenario.Build);
        var plan = NativeGraphPlanningTests.Plan();

        using var first = await runtime.SubmitAsync(plan);
        using var second = await runtime.SubmitAsync(plan);

        Assert.Equal([false, true], scenario.Reused);
        Assert.False(second.Completion.IsComplete);
        backend.CompleteAll(); await second.WaitForCompletionAsync();
    }

    [Fact]
    public async Task OwnerDisposalKeepsExistingGpuReadersAlive()
    {
        var backend = new TestResourceBackend { AutoComplete = false };
        var scenario = new Scenario();
        await using var runtime = await NativeGraphPlanningTests.Create(backend, scenario.Build);
        var plan = NativeGraphPlanningTests.Plan();
        var first = await runtime.SubmitAsync(plan);
        var second = await runtime.SubmitAsync(plan);

        scenario.Generation!.Dispose(); first.Dispose(); second.Dispose(); runtime.Resources.Collect();

        Assert.Equal(0, Assert.Single(scenario.Leases).Disposals);
        backend.CompleteAll(); await runtime.WaitIdleAsync();
        Assert.Equal(1, Assert.Single(scenario.Leases).Disposals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedBuildsAndRecordingsInvalidateUnacceptedGenerations(bool duringRecording)
    {
        var backend = new TestResourceBackend();
        var scenario = new Scenario { FailBuild = !duringRecording, FailRecord = duringRecording };
        await using var runtime = await NativeGraphPlanningTests.Create(backend, scenario.Build);
        var plan = NativeGraphPlanningTests.Plan();

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await runtime.SubmitAsync(plan));
        scenario.FailBuild = scenario.FailRecord = false;
        using var retry = await runtime.SubmitAsync(plan);

        Assert.Equal([false, false], scenario.Reused);
        Assert.Equal(1, scenario.Leases[0].Disposals);
        Assert.Equal(1, backend.Queue.SubmitCount);
    }

    [Fact]
    public async Task CullingARequiredWriterInvalidatesTheCachedGeneration()
    {
        var scenario = new Scenario { UnusedWriter = true };
        await using var runtime = await NativeGraphPlanningTests.Create(new(), scenario.Build);
        var plan = NativeGraphPlanningTests.Plan();

        using var first = await runtime.SubmitAsync(plan);
        using var second = await runtime.SubmitAsync(plan);

        Assert.Equal([false, false], scenario.Reused);
        Assert.All(scenario.Leases, lease => Assert.Equal(1, lease.Disposals));
    }

    [Fact]
    public async Task AcceptedPrivateInitializationSurvivesItsOriginBuildFailure()
    {
        var backend = new TestResourceBackend { AutoComplete = false };
        var scenario = new Scenario { PrivateSubmission = true, FailBuild = true };
        await using var runtime = await NativeGraphPlanningTests.Create(backend, scenario.Build);
        var plan = NativeGraphPlanningTests.Plan();

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await runtime.SubmitAsync(plan));
        scenario.FailBuild = false;
        using var retry = await runtime.SubmitAsync(plan);

        Assert.Equal([false, true], scenario.Reused);
        Assert.Equal(0, Assert.Single(scenario.Leases).Disposals);
        backend.CompleteAll(); await retry.WaitForCompletionAsync();
    }

    [Fact]
    public async Task SubmissionFailureNeverPublishesGeneratedContents()
    {
        var backend = new TestResourceBackend { ThrowAfterHandoff = true, SubmitError = new InvalidOperationException("diagnostic") };
        var scenario = new Scenario();
        var runtime = await NativeGraphPlanningTests.Create(backend, scenario.Build);
        var plan = NativeGraphPlanningTests.Plan();

        var error = await Assert.ThrowsAsync<GpuRenderGraphSubmissionException>(async () => await runtime.SubmitAsync(plan));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await error.Completion.WaitAsync());
        runtime.Resources.Collect();
        backend.SubmitError = null;
        using (var retry = await runtime.SubmitAsync(plan)) { await retry.WaitForCompletionAsync(); }

        Assert.Equal([false, false], scenario.Reused);
        Assert.Equal(1, scenario.Leases[0].Disposals);
        await runtime.DisposeAsync();
    }

    [Fact]
    public async Task GenerationsFromAnotherRuntimeAreRejected()
    {
        var scenario = new Scenario();
        await using var source = await NativeGraphPlanningTests.Create(new(), scenario.Build);
        using var first = await source.SubmitAsync(NativeGraphPlanningTests.Plan());
        await using var other = await NativeGraphPlanningTests.Create(new(), (context, request) => context.TryUseContent(scenario.Generation, out _));

        var error = await Assert.ThrowsAsync<ArgumentException>(async () => await other.SubmitAsync(NativeGraphPlanningTests.Plan()));

        Assert.Equal("generation", error.ParamName);
    }

    [Fact]
    public async Task RegistrationRejectsForeignSubmissionWithoutTakingTheLease()
    {
        using var backend = new TestResourceBackend();
        await using var manager = new GpuResourceManager(backend);
        using var batch = manager.BeginBatch(); batch.StartCommandRecording();
        var token = batch.Submit(); batch.Dispose();
        var lease = new TrackedLease();
        await using var runtime = await NativeGraphPlanningTests.Create(new(), (context, _) => context.RegisterContent(42, lease, token));

        var error = await Assert.ThrowsAsync<ArgumentException>(async () => await runtime.SubmitAsync(NativeGraphPlanningTests.Plan()));

        Assert.Equal("submission", error.ParamName);
        Assert.Equal(0, lease.Disposals); lease.Dispose();
    }

    private sealed class Scenario
    {
        internal NativePassContentGeneration<Content>? Generation;
        internal bool FailBuild, FailRecord, PrivateSubmission, UnusedWriter;
        internal List<bool> Reused { get; } = [];
        internal List<TrackedLease> Leases { get; } = [];
        internal void Build(NativePassBuildContext context, NativeGraphPlanningTests.Request request)
        {
            bool reused = context.TryUseContent(Generation, out Content? content); Reused.Add(reused);
            NativePassBuffer source;
            if (reused) { source = context.ImportBuffer(content!.Buffer); }
            else
            {
                var scope = context.Services.Resources.CreateScope();
                var buffer = scope.CreateBuffer(new(16));
                var lease = new TrackedLease(scope); Leases.Add(lease);
                content = new(buffer); source = context.ImportBuffer(buffer);
                Generation?.Dispose();
                if (PrivateSubmission)
                {
                    using var batch = context.Services.Resources.BeginBatch(); batch.Use(buffer); batch.StartCommandRecording();
                    var token = batch.Submit(); batch.Dispose(); Generation = context.RegisterContent(content, lease, token);
                }
                else
                {
                    var writer = context.AddPass("initialize", 0, (_, _) => { }).Write(source, new(GpuStage.Copy, GpuAccess.CopyWrite));
                    Generation = context.RegisterContent(content, lease, writer);
                }
            }
            if (FailBuild) { throw new InvalidOperationException("preparation"); }
            var output = context.ImportBuffer(request.Target);
            bool fail = FailRecord;
            var pass = context.AddPass("output", 0, (_, _) => { if (fail) { throw new InvalidOperationException("recording"); } })
                .Write(output, new(GpuStage.Copy, GpuAccess.CopyWrite));
            if (!UnusedWriter) { pass.Read(source, new(GpuStage.Copy, GpuAccess.CopyRead)); }
        }
    }
    private sealed record Content(GpuBufferRef Buffer);
    private sealed class TrackedLease(IDisposable? value = null) : IDisposable
    {
        internal int Disposals;
        public void Dispose() { Disposals++; value?.Dispose(); }
    }
}
