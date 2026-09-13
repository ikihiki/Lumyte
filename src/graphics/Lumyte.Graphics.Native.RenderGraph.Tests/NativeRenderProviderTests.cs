using Lumyte.Graphics.Native.Resources.Tests;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.Native.RenderGraph;

namespace Lumyte.Graphics.Native.RenderGraph.Tests;

public sealed class NativeRenderProviderTests
{
    [Fact]
    public async Task MissingRequiredPassFailsBeforeDeviceCreation()
    {
        bool created = false;
        var provider = new NativeRenderProvider("test", (_, _) => { created = true; return new(new TestResourceBackend()); }, new());

        await Assert.ThrowsAsync<NotSupportedException>(async () => await provider.CreateAsync(new() { RequiredPasses = [new("missing", 1)] }));

        Assert.False(created);
    }

    [Fact]
    public async Task ProviderSnapshotsRegistrationsBeforeCreation()
    {
        NativeRenderPassRegistry registry = new();
        var provider = new NativeRenderProvider("test", (_, _) => new(new TestResourceBackend()), registry);
        registry.Register(WriteContract.Instance, _ => new WritePass());

        await Assert.ThrowsAsync<NotSupportedException>(async () => await provider.CreateAsync(new() { RequiredPasses = [new("write", 1)] }));
    }

    [Fact]
    public async Task ExecutionDisposalKeepsPendingGpuResourcesAlive()
    {
        TestResourceBackend backend = new() { AutoComplete = false };
        await using var runtime = await Runtime(backend);
        var graph = new GpuRenderGraph();
        var buffer = graph.CreateBuffer("output", new(16));
        graph.AddPass("write", WriteContract.Instance, buffer); graph.ExportBuffer(buffer);

        GpuRenderGraphExecution execution = await runtime.SubmitAsync(graph.Compile());
        execution.Dispose(); runtime.Resources.Collect();

        Assert.Empty(backend.Destroyed.OfType<TestResourceBackend.Region>());
        backend.CompleteAll();
        await runtime.WaitIdleAsync();
        Assert.Single(backend.Destroyed.OfType<TestResourceBackend.Region>());
    }

    [Fact]
    public async Task ExportsRequireObservedSuccessfulCompletion()
    {
        TestResourceBackend backend = new();
        await using var runtime = await Runtime(backend);
        var graph = new GpuRenderGraph();
        var buffer = graph.CreateBuffer("output", new(16));
        graph.AddPass("write", WriteContract.Instance, buffer); graph.ExportBuffer(buffer);
        using GpuRenderGraphExecution execution = await runtime.SubmitAsync(graph.Compile());

        Assert.Throws<InvalidOperationException>(() => execution.GetExportedBuffer(buffer));
        await execution.WaitForCompletionAsync();

        Assert.Equal(runtime.Id, execution.GetExportedBuffer(buffer).RuntimeId);
    }

    [Fact]
    public async Task BuildFailureReturnsRetainedLeaseWithoutSubmitting()
    {
        TestResourceBackend backend = new();
        Lease lease = new();
        NativeRenderPassRegistry registry = new(); registry.Register(WriteContract.Instance, _ => new FailingPass(lease));
        await using IGpuRenderRuntime runtime = await new NativeRenderProvider("test", (_, _) => new(backend), registry).CreateAsync(new());
        var graph = new GpuRenderGraph();
        var buffer = graph.CreateBuffer("output", new(16));
        graph.AddPass("write", WriteContract.Instance, buffer); graph.ExportBuffer(buffer);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await runtime.SubmitAsync(graph.Compile()));

        Assert.True(lease.Disposed);
        Assert.Empty(backend.Regions);
    }

    [Fact]
    public async Task RuntimeOwnsBackendUntilDisposal()
    {
        TestResourceBackend backend = new();
        IGpuRenderRuntime runtime = await Runtime(backend);
        Assert.False(backend.Disposed);

        await runtime.DisposeAsync();

        Assert.True(backend.Disposed);
    }

    [Fact]
    public async Task ShutdownDrainsBuildsAcceptedBeforeClosing()
    {
        TestResourceBackend backend = new();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        NativeRenderPassRegistry registry = new(); registry.Register(WriteContract.Instance, _ => new PausedPass(entered, proceed));
        IGpuRenderRuntime runtime = await new NativeRenderProvider("test", (_, _) => new(backend), registry).CreateAsync(new());
        var graph = new GpuRenderGraph();
        var buffer = graph.CreateBuffer("output", new(16));
        graph.AddPass("write", WriteContract.Instance, buffer); graph.MarkOutput(buffer);

        Task<GpuRenderGraphExecution> submit = runtime.SubmitAsync(graph.Compile()).AsTask();
        await entered.Task;
        Task stop = runtime.DisposeAsync().AsTask();
        Assert.False(stop.IsCompleted);
        Assert.Throws<ObjectDisposedException>(() => runtime.Resources.CreateScope());
        proceed.SetResult();
        using GpuRenderGraphExecution execution = await submit;
        await stop;

        Assert.True(backend.Disposed);
    }

    [Fact]
    public async Task CapturedBuildContextCannotChangeCompletedPreparation()
    {
        TestResourceBackend backend = new();
        CapturePass implementation = new();
        NativeRenderPassRegistry registry = new(); registry.Register(WriteContract.Instance, _ => implementation);
        await using IGpuRenderRuntime runtime = await new NativeRenderProvider("test", (_, _) => new(backend), registry).CreateAsync(new());
        var graph = new GpuRenderGraph();
        var buffer = graph.CreateBuffer("output", new(16));
        graph.AddPass("write", WriteContract.Instance, buffer); graph.MarkOutput(buffer);
        using GpuRenderGraphExecution execution = await runtime.SubmitAsync(graph.Compile());

        Assert.Throws<ObjectDisposedException>(() => implementation.Context!.CreateBuffer("late", new(16)));
    }

    [Fact]
    public async Task ContractGenericTypesAllowNullableRequests()
    {
        NativeRenderPassRegistry registry = new(); registry.Register(new NullableContract(), _ => new NullablePass());
        await using IGpuRenderRuntime runtime = await new NativeRenderProvider("test", (_, _) => new(new TestResourceBackend()), registry).CreateAsync(new());
        var graph = new GpuRenderGraph(); graph.AddPass("nullable", new NullableContract(), null);

        using GpuRenderGraphExecution execution = await runtime.SubmitAsync(graph.Compile());
        await execution.WaitForCompletionAsync();
    }

    private static ValueTask<IGpuRenderRuntime> Runtime(TestResourceBackend backend)
    {
        NativeRenderPassRegistry registry = new(); registry.Register(WriteContract.Instance, _ => new WritePass());
        return new NativeRenderProvider("test", (_, _) => new(backend), registry).CreateAsync(new());
    }
    private sealed class WriteContract : IGpuRenderPassContract<GpuRenderGraphBuffer, GpuRenderGraphBuffer>
    {
        public static WriteContract Instance { get; } = new();
        public string Id => "write";
        public int Version => 1;
        public GpuRenderGraphBuffer Snapshot(GpuRenderGraphBuffer request) => request;
        public GpuRenderGraphBuffer Declare(GpuPassDeclarationContext context, GpuRenderGraphBuffer request)
        { context.Write(request); return request; }
    }
    private sealed class WritePass : INativeRenderPass<GpuRenderGraphBuffer, GpuRenderGraphBuffer>
    {
        public ValueTask BuildAsync(NativePassBuildContext context, GpuRenderGraphBuffer request, GpuRenderGraphBuffer result, CancellationToken cancellationToken)
        {
            NativePassBuffer buffer = context.ImportBuffer(request);
            context.AddPass("write", buffer, static (_, _) => { }).Write(buffer, new(GpuStage.Copy, GpuAccess.CopyWrite));
            return ValueTask.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class FailingPass(Lease lease) : INativeRenderPass<GpuRenderGraphBuffer, GpuRenderGraphBuffer>
    {
        public ValueTask BuildAsync(NativePassBuildContext context, GpuRenderGraphBuffer request, GpuRenderGraphBuffer result, CancellationToken cancellationToken)
        { context.Retain(lease); throw new InvalidOperationException("Build failed."); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class PausedPass(TaskCompletionSource entered, TaskCompletionSource proceed) : INativeRenderPass<GpuRenderGraphBuffer, GpuRenderGraphBuffer>
    {
        public async ValueTask BuildAsync(NativePassBuildContext context, GpuRenderGraphBuffer request, GpuRenderGraphBuffer result, CancellationToken cancellationToken)
        {
            entered.SetResult(); await proceed.Task;
            NativePassBuffer buffer = context.ImportBuffer(request);
            context.AddPass("write", buffer, static (_, _) => { }).Write(buffer, new(GpuStage.Copy, GpuAccess.CopyWrite));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class CapturePass : INativeRenderPass<GpuRenderGraphBuffer, GpuRenderGraphBuffer>
    {
        internal NativePassBuildContext? Context;
        public ValueTask BuildAsync(NativePassBuildContext context, GpuRenderGraphBuffer request, GpuRenderGraphBuffer result, CancellationToken cancellationToken)
        { Context = context; return new WritePass().BuildAsync(context, request, result, cancellationToken); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Lease : IDisposable
    { internal bool Disposed; public void Dispose() => Disposed = true; }
    private sealed class NullableContract : IGpuRenderPassContract<object?, object?>
    {
        public string Id => "nullable";
        public int Version => 1;
        public object? Snapshot(object? request) => request;
        public object? Declare(GpuPassDeclarationContext context, object? request) { context.Preserve(); return null; }
    }
    private sealed class NullablePass : INativeRenderPass<object?, object?>
    {
        public ValueTask BuildAsync(NativePassBuildContext context, object? request, object? result, CancellationToken cancellationToken)
        { Assert.Null(request); Assert.Null(result); return ValueTask.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
