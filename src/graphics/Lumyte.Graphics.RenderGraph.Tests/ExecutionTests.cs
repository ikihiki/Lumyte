namespace Lumyte.Graphics.RenderGraph.Tests;

public sealed class ExecutionTests
{
    [Fact]
    public async Task GpuUseCompletionAloneDoesNotPublishExport()
    {
        var graph = new GpuRenderGraph();
        var logical = graph.CreateTexture("export", new(1, 1, GpuFormat.Rgba8Unorm));
        var reference = new BindingsTests.TestTexture(Guid.NewGuid(), logical.Description);
        var completion = new GpuGraphCompletion(() => true, _ => ValueTask.CompletedTask);
        using var execution = new GpuRenderGraphExecution(completion,
            new Dictionary<GpuRenderGraphResource, GpuGraphResourceRef> { [logical] = reference }, new PresentationTests.TestLease());

        Assert.True(execution.IsComplete);
        Assert.Throws<InvalidOperationException>(() => execution.GetExportedTexture(logical));
        await execution.WaitForCompletionAsync();

        Assert.Same(reference, execution.GetExportedTexture(logical));
    }

    [Fact]
    public async Task FailedDiagnosticsNeverPublishExport()
    {
        var logical = new GpuRenderGraph().CreateTexture("export", new(1, 1, GpuFormat.Rgba8Unorm));
        var completion = new GpuGraphCompletion(() => true, _ => ValueTask.FromException(new InvalidOperationException("diagnostic")));
        using var execution = new GpuRenderGraphExecution(completion,
            new Dictionary<GpuRenderGraphResource, GpuGraphResourceRef>(), new PresentationTests.TestLease());

        await Assert.ThrowsAsync<InvalidOperationException>(() => execution.WaitForCompletionAsync().AsTask());

        Assert.Throws<InvalidOperationException>(() => execution.GetExportedTexture(logical));
    }

    [Fact]
    public async Task CancellingWaitDoesNotReleaseExecutionOwnership()
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ownership = new PresentationTests.TestLease();
        var completion = new GpuGraphCompletion(() => pending.Task.IsCompleted, token => new(pending.Task.WaitAsync(token)));
        using var execution = new GpuRenderGraphExecution(completion, new Dictionary<GpuRenderGraphResource, GpuGraphResourceRef>(), ownership);
        using var cancellation = new CancellationTokenSource();

        var wait = execution.WaitForCompletionAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait.AsTask());

        Assert.Equal(0, ownership.Disposals);
        pending.SetResult();
        await execution.WaitForCompletionAsync();
    }

    [Fact]
    public void ExecutionDisposalReleasesItsOwnerOnce()
    {
        var owner = new PresentationTests.TestLease();
        var execution = new GpuRenderGraphExecution(new(() => false, _ => ValueTask.CompletedTask),
            new Dictionary<GpuRenderGraphResource, GpuGraphResourceRef>(), owner);

        execution.Dispose();
        execution.Dispose();

        Assert.Equal(1, owner.Disposals);
    }
}
