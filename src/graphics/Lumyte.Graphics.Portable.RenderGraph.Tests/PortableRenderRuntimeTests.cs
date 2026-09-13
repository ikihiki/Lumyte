using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.Portable.Resources.Tests.Unit.Management;

namespace Lumyte.Graphics.Portable.RenderGraph.Tests;

public sealed class PortableRenderRuntimeTests
{
    [Fact]
    public async Task RequiredPassIsCheckedBeforeCreatingTheBackend()
    {
        bool created = false;
        var provider = new PortableRenderProvider("test", (_, _) =>
        { created = true; return ValueTask.FromResult<IPortableGpuBackend>(new ManagerTestBackend()); }, new());

        await Assert.ThrowsAsync<NotSupportedException>(() => provider.CreateAsync(new()
        { RequiredPasses = [new("missing", 1)] }).AsTask());

        Assert.False(created);
    }

    [Fact]
    public async Task SubmissionReturnsBeforeCompletionAndExportsKeepReleasedStorageAlive()
    {
        var backend = new ManagerTestBackend(); backend.TestQueue.AutoComplete = false;
        await using PortableRenderRuntime runtime = await CreateRuntime(backend, new WritePass());
        (GpuRenderGraphPlan plan, GpuRenderGraphTexture output) = CreatePlan();

        using GpuRenderGraphExecution execution = await runtime.SubmitAsync(plan);
        Assert.False(execution.IsComplete);
        execution.Dispose(); runtime.Resources.Collect();
        Assert.Equal(1, runtime.Resources.Manager.Statistics.ResourceCount);
        backend.TestQueue.Complete(0);
        await execution.WaitForCompletionAsync(); runtime.Resources.Collect();

        Assert.Equal(0, runtime.Resources.Manager.Statistics.ResourceCount);
        Assert.All(backend.TestQueue.Recordings, recording => Assert.True(recording.Disposed));
    }

    [Fact]
    public async Task ExportPinSurvivesExecutionDisposal()
    {
        var backend = new ManagerTestBackend();
        await using PortableRenderRuntime runtime = await CreateRuntime(backend, new WritePass());
        (GpuRenderGraphPlan plan, GpuRenderGraphTexture output) = CreatePlan();
        using GpuRenderGraphExecution execution = await runtime.SubmitAsync(plan);
        await execution.WaitForCompletionAsync();
        GpuGraphTextureRef reference = execution.GetExportedTexture(output);
        using GpuGraphResourcePin pin = runtime.Resources.Pin(reference);

        execution.Dispose(); runtime.Resources.Collect();

        Assert.Equal(1, runtime.Resources.Manager.Statistics.TextureCount);
        Assert.Equal(GpuTextureUsage.ColorAttachment | GpuTextureUsage.CopySource, runtime.Resources.ResolveTexture(reference).Description.Usage);
        pin.Dispose(); runtime.Resources.Collect();
        Assert.Equal(0, runtime.Resources.Manager.Statistics.ResourceCount);
    }

    [Fact]
    public async Task ImportedResourcesAreRetainedBeforeAnAsyncBuildSuspends()
    {
        var backend = new ManagerTestBackend(); var pass = new WritePass();
        await using PortableRenderRuntime runtime = await CreateRuntime(backend, pass);
        (GpuRenderGraphPlan initialPlan, GpuRenderGraphTexture initialOutput) = CreatePlan();
        using GpuRenderGraphExecution initial = await runtime.SubmitAsync(initialPlan);
        await initial.WaitForCompletionAsync();
        var graph = new GpuRenderGraph();
        GpuRenderGraphTexture imported = graph.ImportTexture("import", initial.GetExportedTexture(initialOutput));
        graph.AddPass("write", WriteContract.Instance, imported); graph.MarkOutput(imported);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); pass.Pause = resume.Task;

        Task<GpuRenderGraphExecution> pending = runtime.SubmitAsync(graph.Compile()).AsTask();
        initial.Dispose(); runtime.Resources.Collect();
        Assert.False(pending.IsCompleted);
        Assert.Equal(1, runtime.Resources.Manager.Statistics.TextureCount);
        resume.SetResult();
        using GpuRenderGraphExecution result = await pending;
        await result.WaitForCompletionAsync(); result.Dispose(); runtime.Resources.Collect();

        Assert.Equal(0, runtime.Resources.Manager.Statistics.TextureCount);
    }

    [Fact]
    public async Task FailedBuildReleasesUnsubmittedOwnership()
    {
        var backend = new ManagerTestBackend();
        await using PortableRenderRuntime runtime = await CreateRuntime(backend, new WritePass { Fail = true });
        var (plan, _) = CreatePlan();

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.SubmitAsync(plan).AsTask());

        Assert.Equal("build failed", error.Message);
        Assert.Empty(backend.TestQueue.Submitted);
        Assert.Equal(0, runtime.Resources.Manager.Statistics.ResourceCount);
    }

    [Fact]
    public async Task StopAcceptingAllowsPreviouslyQueuedBuildsToFinish()
    {
        var backend = new ManagerTestBackend();
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pass = new WritePass { Pause = resume.Task };
        PortableRenderRuntime runtime = await CreateRuntime(backend, pass);
        var (plan, _) = CreatePlan();
        Task<GpuRenderGraphExecution> first = runtime.SubmitAsync(plan).AsTask();
        Task<GpuRenderGraphExecution> queued = runtime.SubmitAsync(plan).AsTask();

        runtime.StopAccepting();
        Assert.False(first.IsCompleted); Assert.False(queued.IsCompleted); Assert.False(backend.Disposed);
        resume.SetResult();
        using GpuRenderGraphExecution firstExecution = await first;
        using GpuRenderGraphExecution queuedExecution = await queued;
        await firstExecution.WaitForCompletionAsync(); await queuedExecution.WaitForCompletionAsync();
        firstExecution.Dispose(); queuedExecution.Dispose(); await runtime.DisposeAsync();

        Assert.True(backend.Disposed);
        Assert.Equal(2, backend.TestQueue.Submitted.Count);
    }

    [Fact]
    public async Task ShutdownRequiresCallerToReturnItsExecutionOwnership()
    {
        var backend = new ManagerTestBackend();
        PortableRenderRuntime runtime = await CreateRuntime(backend, new WritePass());
        var (plan, _) = CreatePlan();
        using GpuRenderGraphExecution execution = await runtime.SubmitAsync(plan);
        await execution.WaitForCompletionAsync();

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.DisposeAsync().AsTask());

        Assert.Contains("executions", error.Message, StringComparison.Ordinal);
        Assert.False(backend.Disposed);
        execution.Dispose(); await runtime.DisposeAsync();
        Assert.True(backend.Disposed);
    }

    [Fact]
    public async Task UnknownSubmissionAcceptanceKeepsResourcesUntilConfirmedCompletion()
    {
        var backend = new ManagerTestBackend();
        backend.TestQueue.AutoComplete = false; backend.TestQueue.RegisterBeforeThrow = true;
        backend.TestQueue.SubmitError = new InvalidOperationException("submission failed");
        await using PortableRenderRuntime runtime = await CreateRuntime(backend, new WritePass());
        var (plan, _) = CreatePlan();

        GpuRenderGraphSubmissionException error = await Assert.ThrowsAsync<GpuRenderGraphSubmissionException>(() => runtime.SubmitAsync(plan).AsTask());
        Assert.False(error.Completion.IsComplete);
        Assert.Equal(1, runtime.Resources.Manager.Statistics.TextureCount);
        backend.TestQueue.Complete(0);
        Assert.True(error.Completion.IsComplete);
        await Assert.ThrowsAnyAsync<Exception>(() => error.Completion.WaitAsync().AsTask());
        runtime.Resources.Collect();

        Assert.Equal(0, runtime.Resources.Manager.Statistics.TextureCount);
    }

    private static (GpuRenderGraphPlan, GpuRenderGraphTexture) CreatePlan()
    {
        var graph = new GpuRenderGraph();
        GpuRenderGraphTexture texture = graph.CreateTexture("output", new(4, 4, GpuFormat.Rgba8Unorm));
        graph.AddPass("write", WriteContract.Instance, texture); graph.ExportTexture(texture);
        return (graph.Compile(), texture);
    }
    private static async Task<PortableRenderRuntime> CreateRuntime(ManagerTestBackend backend, WritePass pass)
    {
        var registry = new PortableRenderPassRegistry(); registry.Register(WriteContract.Instance, _ => pass);
        var provider = new PortableRenderProvider("test", (_, _) => ValueTask.FromResult<IPortableGpuBackend>(backend), registry);
        return (PortableRenderRuntime)await provider.CreateAsync(new());
    }
    private sealed class WriteContract : IGpuRenderPassContract<GpuRenderGraphTexture, GpuRenderGraphTexture>
    {
        internal static readonly WriteContract Instance = new();
        public string Id => "test.write";
        public int Version => 1;
        public GpuRenderGraphTexture Snapshot(GpuRenderGraphTexture request) => request;
        public GpuRenderGraphTexture Declare(GpuPassDeclarationContext context, GpuRenderGraphTexture request)
        { context.Write(request); return request; }
    }
    private sealed class WritePass : IPortableRenderPass<GpuRenderGraphTexture, GpuRenderGraphTexture>
    {
        internal Task? Pause { get; set; }
        internal bool Fail { get; init; }
        public async ValueTask BuildAsync(PortablePassBuildContext context, GpuRenderGraphTexture request,
            GpuRenderGraphTexture result, CancellationToken cancellationToken)
        {
            if (Pause is not null) { await Pause.WaitAsync(cancellationToken); }
            if (Fail) { throw new InvalidOperationException("build failed"); }
            PortablePassTexture texture = context.ImportTexture(request);
            context.AddPass("write", 0, static (_, _) => { }).Write(texture, PortablePassUsage.ColorAttachment);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
