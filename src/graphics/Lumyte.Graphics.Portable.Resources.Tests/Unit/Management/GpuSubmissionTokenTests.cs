namespace Lumyte.Graphics.Portable.Resources.Tests.Unit.Management;

public sealed class GpuSubmissionTokenTests
{
    private static readonly GpuBufferDescription Description = new(16, GpuBufferUsage.Storage);

    [Fact]
    public void DefaultTokenHasNoCompletionAuthority()
    {
        GpuSubmissionToken token = default;

        Assert.False(token.IsValid);
        Assert.False(token.IsComplete);
        Assert.Equal(default, token);
        Assert.Throws<InvalidOperationException>(() => token.WaitAsync());
    }

    [Fact]
    public async Task SubmittedBatchRetainsResourcesUntilCompletionAndDisposal()
    {
        var backend = new ManagerTestBackend(); backend.TestQueue.AutoComplete = false;
        await using var manager = new GpuResourceManager(backend);
        var scope = manager.CreateScope(); GpuBufferRef buffer = scope.CreateBuffer(Description);
        var batch = manager.BeginBatch(); batch.Use(buffer); batch.StartCommandRecording();
        GpuSubmissionToken token = batch.Submit(); scope.Dispose();

        manager.Trim(); Assert.Empty(backend.Destroyed);
        backend.TestQueue.Complete(0); await token.WaitAsync(); manager.Trim();

        Assert.True(token.IsComplete);
        Assert.Empty(backend.Destroyed);
        batch.Dispose(); manager.Trim(); Assert.Single(backend.Destroyed);
        Assert.Equal(0, manager.Statistics.PendingSubmissionCount);
    }

    [Fact]
    public async Task RetiredUseReleasesAtItsManagerToken()
    {
        var backend = new ManagerTestBackend(); backend.TestQueue.AutoComplete = false;
        await using var manager = new GpuResourceManager(backend);
        var scope = manager.CreateScope(); GpuBufferRef buffer = scope.CreateBuffer(Description);
        var use = manager.AcquireUse(buffer);
        using var batch = manager.BeginBatch(); batch.StartCommandRecording();
        GpuSubmissionToken token = batch.Submit(); use.Retire(token); scope.Dispose(); batch.Dispose();

        manager.Trim(); Assert.Empty(backend.Destroyed);
        backend.TestQueue.Complete(0); await token.WaitAsync(); manager.Trim();

        Assert.Single(backend.Destroyed);
        Assert.Throws<ObjectDisposedException>(() => use.Retire(token));
    }

    [Fact]
    public async Task CancellingAWaitDoesNotEndGpuUse()
    {
        var backend = new ManagerTestBackend(); backend.TestQueue.AutoComplete = false;
        await using var manager = new GpuResourceManager(backend);
        var scope = manager.CreateScope(); GpuBufferRef buffer = scope.CreateBuffer(Description);
        using var batch = manager.BeginBatch(); batch.Use(buffer); batch.StartCommandRecording();
        GpuSubmissionToken token = batch.Submit(); batch.Dispose(); scope.Dispose();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => token.WaitAsync(cancellation.Token).AsTask());
        manager.Trim();

        Assert.False(token.IsComplete); Assert.Empty(backend.Destroyed);
        backend.TestQueue.Complete(0); await token.WaitAsync(); manager.Trim(); Assert.Single(backend.Destroyed);
    }

    [Fact]
    public async Task DiagnosticFailureStillAllowsKnownEndedOwnershipToBeReclaimed()
    {
        var backend = new ManagerTestBackend(); backend.TestQueue.AutoComplete = false;
        var manager = new GpuResourceManager(backend);
        var scope = manager.CreateScope(); GpuBufferRef buffer = scope.CreateBuffer(Description);
        var batch = manager.BeginBatch(); batch.Use(buffer); batch.StartCommandRecording();
        GpuSubmissionToken token = batch.Submit(); batch.Dispose(); scope.Dispose();

        backend.TestQueue.Complete(0, new GpuDiagnostic(GpuDiagnosticKind.Validation, "invalid shader output"));
        GpuExecutionException error = await Assert.ThrowsAsync<GpuExecutionException>(() => token.WaitAsync().AsTask());
        manager.Trim(); await manager.DisposeAsync();

        Assert.True(token.IsComplete);
        Assert.Equal("invalid shader output", Assert.Single(error.Diagnostics).Message);
        Assert.Single(backend.Destroyed);
    }

    [Fact]
    public async Task UnknownSubmissionKeepsOwnershipAndFailsDrainWithoutLeakingRawFence()
    {
        var backend = new ManagerTestBackend(); backend.TestQueue.SubmitError = new InvalidOperationException("handoff unknown");
        var manager = new GpuResourceManager(backend);
        var scope = manager.CreateScope(); GpuBufferRef buffer = scope.CreateBuffer(Description);
        var batch = manager.BeginBatch(); batch.Use(buffer); batch.StartCommandRecording();

        GpuSubmissionException error = Assert.Throws<GpuSubmissionException>(() => batch.Submit());
        batch.Dispose(); scope.Dispose();
        await Assert.ThrowsAsync<AggregateException>(() => manager.WaitIdleAsync().AsTask());
        await Assert.ThrowsAsync<AggregateException>(() => manager.DisposeAsync().AsTask());

        Assert.True(error.Completion.IsValid);
        Assert.Same(backend.TestQueue.SubmitError, error.InnerException);
        Assert.Empty(backend.Destroyed);
        Assert.False(Assert.Single(backend.TestQueue.Recordings).Disposed);
        Assert.False(Assert.Single(backend.TestQueue.Timelines).Disposed);
    }

    [Fact]
    public async Task PostHandoffFailureCanLaterEstablishGpuEnd()
    {
        var backend = new ManagerTestBackend();
        backend.TestQueue.AutoComplete = false; backend.TestQueue.RegisterBeforeThrow = true;
        backend.TestQueue.SubmitError = new InvalidOperationException("diagnostic hookup failed");
        var manager = new GpuResourceManager(backend);
        var scope = manager.CreateScope(); GpuBufferRef buffer = scope.CreateBuffer(Description);
        var batch = manager.BeginBatch(); batch.Use(buffer); batch.StartCommandRecording();

        GpuSubmissionException error = Assert.Throws<GpuSubmissionException>(() => batch.Submit());
        scope.Dispose(); batch.Dispose();
        backend.TestQueue.Complete(0); manager.Trim();

        Assert.True(error.Completion.IsComplete);
        Assert.Single(backend.Destroyed);
        Assert.IsNotType<Portable.GpuSubmissionException>(error.InnerException);
        await manager.DisposeAsync();
    }

    [Fact]
    public async Task CollectionDoesNotBlockLaterCompletedWorkBehindEarlierPendingWork()
    {
        var backend = new ManagerTestBackend(); backend.TestQueue.AutoComplete = false;
        await using var manager = new GpuResourceManager(backend);
        var firstScope = manager.CreateScope(); var secondScope = manager.CreateScope();
        GpuBufferRef first = firstScope.CreateBuffer(Description); GpuBufferRef second = secondScope.CreateBuffer(Description with { Size = 32 });
        GpuBufferHandle secondHandle = manager.GetBufferRange(second).Buffer;
        using var firstBatch = manager.BeginBatch(); firstBatch.Use(first); firstBatch.StartCommandRecording();
        using var secondBatch = manager.BeginBatch(); secondBatch.Use(second); secondBatch.StartCommandRecording();
        GpuSubmissionToken firstToken = firstBatch.Submit(); GpuSubmissionToken secondToken = secondBatch.Submit();
        firstScope.Dispose(); secondScope.Dispose(); firstBatch.Dispose(); secondBatch.Dispose();

        backend.TestQueue.Complete(1); await secondToken.WaitAsync(); manager.Trim();

        Assert.Same(secondHandle, Assert.Single(backend.Destroyed));
        Assert.False(firstToken.IsComplete);
        backend.TestQueue.Complete(0); await firstToken.WaitAsync();
    }

    [Fact]
    public async Task EmptyBatchRejectionDoesNotReserveSubmissionIdentity()
    {
        var backend = new ManagerTestBackend();
        await using var manager = new GpuResourceManager(backend);
        using var batch = manager.BeginBatch();

        Assert.Throws<InvalidOperationException>(() => batch.Submit());
        batch.StartCommandRecording(); GpuSubmissionToken token = batch.Submit(); await token.WaitAsync();

        Assert.Equal(1UL, Assert.Single(backend.TestQueue.Submitted).Value);
    }

    [Fact]
    public async Task ForeignTokenRejectionDoesNotTransferUseOwnership()
    {
        var backend = new ManagerTestBackend();
        await using var first = new GpuResourceManager(backend); await using var second = new GpuResourceManager(backend);
        using var scope = first.CreateScope(); GpuBufferRef buffer = scope.CreateBuffer(Description);
        using var use = first.AcquireUse(buffer); using var batch = second.BeginBatch(); batch.StartCommandRecording();
        GpuSubmissionToken token = batch.Submit(); await token.WaitAsync();

        Assert.Equal("token", Assert.Throws<ArgumentException>(() => use.Retire(token)).ParamName);
        scope.Dispose(); first.Trim();

        Assert.Empty(backend.Destroyed);
    }

    [Fact]
    public async Task DrainReportsUnobservedDiagnosticsAfterResourceCollection()
    {
        var backend = new ManagerTestBackend(); backend.TestQueue.AutoComplete = false;
        var manager = new GpuResourceManager(backend); var batch = manager.BeginBatch(); batch.StartCommandRecording();
        batch.Submit(); batch.Dispose();
        backend.TestQueue.Complete(0, new GpuDiagnostic(GpuDiagnosticKind.Validation, "unobserved failure"));
        manager.Collect();

        GpuExecutionException error = await Assert.ThrowsAsync<GpuExecutionException>(() => manager.WaitIdleAsync().AsTask());
        await manager.DisposeAsync();

        Assert.Equal("unobserved failure", Assert.Single(error.Diagnostics).Message);
    }

    [Fact]
    public async Task StaleCompletionQueryCannotEraseAnAsynchronouslyConfirmedGpuEnd()
    {
        var backend = new ManagerTestBackend(); backend.TestQueue.AutoComplete = false;
        await using var manager = new GpuResourceManager(backend); using var batch = manager.BeginBatch(); batch.StartCommandRecording();
        GpuSubmissionToken token = batch.Submit();
        var readStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseRead = new ManualResetEventSlim();
        backend.TestQueue.AfterCompletionRead = () => { readStarted.SetResult(); releaseRead.Wait(); };

        Task<bool> reading = Task.Run(() => token.IsComplete);
        await readStarted.Task;
        backend.TestQueue.Complete(0); await token.WaitAsync();
        releaseRead.Set();

        Assert.True(await reading);
        backend.TestQueue.AfterCompletionRead = null;
        Assert.True(token.IsComplete);
    }
}
