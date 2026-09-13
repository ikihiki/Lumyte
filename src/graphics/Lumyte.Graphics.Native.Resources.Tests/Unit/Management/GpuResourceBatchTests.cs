namespace Lumyte.Graphics.Native.Resources.Tests.Unit.Management;

public sealed class GpuResourceBatchTests
{
    private static GpuResourceManager Create(TestResourceBackend backend) => new(backend, new(1024, 8, 8));

    [Fact]
    public async Task UseSnapshotsTheScopeBeforeLaterCreations()
    {
        using TestResourceBackend backend = new(); await using GpuResourceManager manager = Create(backend);
        using GpuResourceScope scope = manager.CreateScope(); GpuBufferRef first = scope.CreateBuffer(new(32));
        GpuResourceBatch batch = manager.BeginBatch(); batch.Use(scope);
        GpuBufferRef later = scope.CreateBuffer(new(32)); NativeGpuLinearRegion laterRegion = manager.GetBufferRange(later).Region;
        scope.Dispose();

        manager.Collect();

        Assert.Same(laterRegion, Assert.Single(backend.Destroyed));
        Assert.NotEqual(0ul, manager.GetGpuAddress(first));
        batch.Dispose(); manager.Collect();
    }

    [Fact]
    public async Task OwningAScopeFreezesCallerMutationUntilBatchReleasesIt()
    {
        using TestResourceBackend backend = new(); await using GpuResourceManager manager = Create(backend);
        GpuResourceScope scope = manager.CreateScope(); _ = scope.CreateBuffer(new(32));
        GpuResourceBatch batch = manager.BeginBatch(); batch.Own(scope);

        Assert.Throws<InvalidOperationException>(() => scope.CreateBuffer(new(16)));
        Assert.Throws<InvalidOperationException>(() => scope.Dispose());
        batch.Dispose(); manager.Collect();

        Assert.Single(backend.Destroyed.OfType<TestResourceBackend.Region>());
    }

    [Fact]
    public async Task AcceptedWorkRetainsResourcesUntilCompletionAndBatchEnd()
    {
        using TestResourceBackend backend = new() { AutoComplete = false }; await using GpuResourceManager manager = Create(backend);
        using GpuResourceScope scope = manager.CreateScope(); GpuBufferRef buffer = scope.CreateBuffer(new(32));
        GpuResourceBatch batch = manager.BeginBatch(); batch.Use(buffer); _ = batch.StartCommandRecording();
        GpuSubmissionToken completion = batch.Submit(); scope.Dispose();
        backend.CompleteAll(); await completion.WaitAsync(); manager.Collect();
        Assert.Empty(backend.Destroyed);

        batch.Dispose(); manager.Collect();

        Assert.Single(backend.Destroyed.OfType<TestResourceBackend.Region>());
        Assert.True(completion.IsComplete);
    }

    [Fact]
    public async Task CancelingACpuWaitDoesNotCancelAcceptedOwnership()
    {
        using TestResourceBackend backend = new() { AutoComplete = false }; await using GpuResourceManager manager = Create(backend);
        using GpuResourceScope scope = manager.CreateScope(); GpuBufferRef buffer = scope.CreateBuffer(new(32));
        using GpuResourceBatch batch = manager.BeginBatch(); batch.Use(buffer);
        GpuSubmissionToken completion = batch.Submit(); batch.Dispose(); scope.Dispose();
        using CancellationTokenSource cancellation = new(); cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => completion.WaitAsync(cancellation.Token));
        manager.Collect(); Assert.Empty(backend.Destroyed);
        backend.CompleteAll(); await manager.WaitIdleAsync();

        Assert.Single(backend.Destroyed.OfType<TestResourceBackend.Region>());
    }

    [Fact]
    public async Task UnknownSubmissionExposesOnlyManagedIdentityAndRetainsStorage()
    {
        InvalidOperationException cause = new("Queue acceptance is unknown.");
        using TestResourceBackend backend = new() { UnknownSubmission = true, SubmitError = cause };
        GpuResourceManager manager = Create(backend); using GpuResourceScope scope = manager.CreateScope();
        GpuBufferRef buffer = scope.CreateBuffer(new(32)); using GpuResourceBatch batch = manager.BeginBatch(); batch.Use(buffer);

        GpuSubmissionException error = Assert.Throws<GpuSubmissionException>(() => batch.Submit());
        batch.Dispose(); scope.Dispose(); manager.Collect();

        Assert.Same(cause, error.InnerException);
        Assert.True(error.Completion.IsValid); Assert.False(error.Completion.IsComplete);
        Assert.Same(cause, await Assert.ThrowsAsync<InvalidOperationException>(() => error.Completion.WaitAsync()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.DisposeAsync().AsTask());
        Assert.Empty(backend.Destroyed);
    }

    [Fact]
    public async Task DefinitelyRejectedWorkReleasesItsRecordedUses()
    {
        NativeGpuException cause = new("Submission was rejected.", -1);
        using TestResourceBackend backend = new() { SemaphoreCreationError = cause }; await using GpuResourceManager manager = Create(backend);
        using GpuResourceScope scope = manager.CreateScope(); GpuBufferRef buffer = scope.CreateBuffer(new(32));
        using GpuResourceBatch batch = manager.BeginBatch(); batch.Use(buffer); _ = batch.StartCommandRecording(); scope.Dispose();

        NativeGpuException error = Assert.Throws<NativeGpuException>(() => batch.Submit()); manager.Collect();

        Assert.Same(cause, error);
        Assert.Contains("DisposeRecording", backend.Commands);
        Assert.Single(backend.Destroyed.OfType<TestResourceBackend.Region>());
        Assert.Equal(0, backend.Queue.SubmitCount);
    }

    [Fact]
    public async Task RetiredRawUseFollowsTheManagersPrivateCompletion()
    {
        using TestResourceBackend backend = new() { AutoComplete = false }; await using GpuResourceManager manager = Create(backend);
        using GpuResourceScope scope = manager.CreateScope(); GpuBufferRef buffer = scope.CreateBuffer(new(32));
        GpuResourceUse use = manager.AcquireUse(buffer);
        using GpuResourceBatch batch = manager.BeginBatch(); GpuSubmissionToken completion = batch.Submit();
        use.Retire(completion); use.Dispose(); batch.Dispose(); scope.Dispose(); manager.Collect();
        Assert.Empty(backend.Destroyed);

        backend.CompleteAll(); await manager.WaitIdleAsync();

        Assert.Single(backend.Destroyed.OfType<TestResourceBackend.Region>());
    }

    [Fact]
    public async Task DefaultAndForeignTokensCannotRetireAUse()
    {
        using TestResourceBackend backend = new(); await using GpuResourceManager manager = Create(backend);
        await using GpuResourceManager other = Create(backend);
        using GpuResourceScope scope = manager.CreateScope(); GpuBufferRef buffer = scope.CreateBuffer(new(32));
        using GpuResourceUse use = manager.AcquireUse(buffer); using GpuResourceBatch batch = other.BeginBatch();
        GpuSubmissionToken foreign = batch.Submit();

        Assert.False(default(GpuSubmissionToken).IsValid); Assert.False(default(GpuSubmissionToken).IsComplete);
        Assert.Throws<InvalidOperationException>(() => use.Retire(default));
        Assert.Throws<ArgumentException>(() => use.Retire(foreign));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateFailureKeepsItsDiagnosticAfterCompletionCanBeProven(bool wrapped)
    {
        InvalidOperationException cause = new("A backend threw after signaling.");
        using TestResourceBackend backend = new() { SubmitError = cause, ThrowAfterHandoff = true, UnknownSubmission = wrapped };
        await using GpuResourceManager manager = Create(backend); using GpuResourceScope scope = manager.CreateScope();
        GpuBufferRef buffer = scope.CreateBuffer(new(32)); using GpuResourceBatch batch = manager.BeginBatch(); batch.Use(buffer);

        GpuSubmissionException error = Assert.Throws<GpuSubmissionException>(() => batch.Submit());
        batch.Dispose(); scope.Dispose(); manager.Collect();

        Assert.True(error.Completion.IsComplete);
        Assert.Same(cause, await Assert.ThrowsAsync<InvalidOperationException>(() => error.Completion.WaitAsync()));
        Assert.Single(backend.Destroyed.OfType<TestResourceBackend.Region>());
    }

    [Fact]
    public async Task AFailedLeaseCannotReleaseTheResourceItMayStillReference()
    {
        using TestResourceBackend backend = new(); GpuResourceManager manager = Create(backend);
        using GpuResourceScope scope = manager.CreateScope(); GpuBufferRef buffer = scope.CreateBuffer(new(32));
        FailingLease lease = new(); using GpuResourceBatch batch = manager.BeginBatch(); batch.Use(buffer); batch.Retain(lease);
        await batch.Submit().WaitAsync(); batch.Dispose(); scope.Dispose();

        Assert.Throws<AggregateException>(() => manager.Collect());
        Assert.Throws<AggregateException>(() => manager.Collect());
        await Assert.ThrowsAsync<AggregateException>(() => manager.DisposeAsync().AsTask());

        Assert.Equal(1, lease.Calls);
        Assert.Empty(backend.Destroyed);
    }

    [Fact]
    public async Task AFailedDrainDoesNotWaitForAnotherUnsignaledSubmission()
    {
        using TestResourceBackend backend = new() { AutoComplete = false }; GpuResourceManager manager = Create(backend);
        using GpuResourceScope scope = manager.CreateScope();
        GpuBufferRef first = scope.CreateBuffer(new(32)); GpuBufferRef second = scope.CreateBuffer(new(32));
        using GpuResourceBatch pending = manager.BeginBatch(); pending.Use(first); GpuSubmissionToken pendingToken = pending.Submit(); pending.Dispose();
        InvalidOperationException cause = new("Another submission cannot be observed."); backend.SubmitError = cause;
        using GpuResourceBatch failed = manager.BeginBatch(); failed.Use(second);
        Assert.Throws<GpuSubmissionException>(() => failed.Submit()); failed.Dispose(); scope.Dispose();

        Task drain = manager.WaitIdleAsync();
        Assert.True(drain.IsCompleted);
        Assert.Same(cause, await Assert.ThrowsAsync<InvalidOperationException>(() => drain));
        backend.CompleteAll(); await pendingToken.WaitAsync(); manager.Collect();

        Assert.Single(backend.Destroyed.OfType<TestResourceBackend.Region>());
    }

    private sealed class FailingLease : IDisposable
    {
        internal int Calls;
        public void Dispose() { Calls++; throw new InvalidOperationException("Unmapping has an unknown effect."); }
    }

    [Fact]
    public async Task FailedCommandDestructionCannotReturnAUseHeldOnlyByALease()
    {
        using TestResourceBackend backend = new() { RecordingDisposalError = new InvalidOperationException("Command destruction is uncertain.") };
        GpuResourceManager manager = Create(backend); using GpuResourceScope scope = manager.CreateScope();
        GpuBufferRef buffer = scope.CreateBuffer(new(32)); GpuResourceUse use = manager.AcquireUse(buffer);
        using GpuResourceBatch batch = manager.BeginBatch(); batch.Retain(use); _ = batch.StartCommandRecording();
        await batch.Submit().WaitAsync(); batch.Dispose(); scope.Dispose();

        Assert.Throws<AggregateException>(() => manager.Collect());

        Assert.Empty(backend.Destroyed);
        Assert.NotEqual(0ul, manager.GetGpuAddress(buffer));
    }

    [Fact]
    public void NestedBackendErrorsDoNotExposePrivateTimelineAuthority()
    {
        InvalidOperationException cause = new("Native call failed.");
        using TestResourceBackend backend = new() { SubmitError = cause, UnknownSubmission = true, NestSubmissionException = true };
        GpuResourceManager manager = Create(backend); using GpuResourceBatch batch = manager.BeginBatch();

        GpuSubmissionException failure = Assert.Throws<GpuSubmissionException>(() => batch.Submit());

        AggregateException aggregate = Assert.IsType<AggregateException>(failure.InnerException);
        InvalidOperationException wrapper = Assert.IsType<InvalidOperationException>(Assert.Single(aggregate.InnerExceptions));
        Assert.Same(cause, wrapper.InnerException);
    }

    [Fact]
    public async Task RetainedUseOwnershipAllowsManagerDisposalToDrainTheBatch()
    {
        using TestResourceBackend backend = new(); GpuResourceManager manager = Create(backend);
        using GpuResourceScope scope = manager.CreateScope(); GpuBufferRef buffer = scope.CreateBuffer(new(32));
        GpuResourceUse use = manager.AcquireUse(buffer); using GpuResourceBatch batch = manager.BeginBatch(); batch.Retain(use);
        Assert.Throws<InvalidOperationException>(() => use.Dispose());
        await batch.Submit().WaitAsync(); batch.Dispose(); scope.Dispose();

        await manager.DisposeAsync();

        Assert.Single(backend.Destroyed.OfType<TestResourceBackend.Region>());
        Assert.False(backend.Disposed);
    }

    [Fact]
    public async Task FailedRecordingSetupRemainsOwnedUntilTheBatchIsDisposed()
    {
        InvalidOperationException cause = new("Descriptor heap setup failed.");
        using TestResourceBackend backend = new() { RecordingSetupError = cause };
        await using GpuResourceManager manager = Create(backend); using GpuResourceScope scope = manager.CreateScope();
        GpuBufferRef buffer = scope.CreateBuffer(new(32)); using GpuResourceBatch batch = manager.BeginBatch(); batch.Use(buffer);

        Assert.Same(cause, Assert.Throws<InvalidOperationException>(() => batch.StartCommandRecording()));
        scope.Dispose(); manager.Collect();

        Assert.DoesNotContain("DisposeRecording", backend.Commands);
        Assert.Empty(backend.Destroyed);
        Assert.Throws<InvalidOperationException>(() => batch.Submit());
        batch.Dispose(); manager.Collect();
        Assert.Single(backend.Destroyed.OfType<TestResourceBackend.Region>());
    }

    [Fact]
    public void FailedRecordingSetupCannotLoseUncertainRecordingOwnership()
    {
        using TestResourceBackend backend = new()
        {
            RecordingSetupError = new InvalidOperationException("Descriptor heap setup failed."),
            RecordingDisposalError = new InvalidOperationException("Recording destruction failed."),
        };
        GpuResourceManager manager = Create(backend); using GpuResourceScope scope = manager.CreateScope();
        GpuBufferRef buffer = scope.CreateBuffer(new(32)); GpuResourceBatch batch = manager.BeginBatch(); batch.Use(buffer);
        Assert.Throws<InvalidOperationException>(() => batch.StartCommandRecording()); scope.Dispose();

        Assert.Throws<AggregateException>(() => batch.Dispose()); manager.Collect();

        Assert.Empty(backend.Destroyed);
        Assert.Equal(1, backend.Commands.Count(command => command == "DisposeRecording"));
        Assert.NotEqual(0ul, manager.GetGpuAddress(buffer));
    }

    [Fact]
    public async Task ReturningARetainedUseCannotReleaseResourcesBeforeOtherLeasesEnd()
    {
        using TestResourceBackend backend = new(); GpuResourceManager manager = Create(backend);
        using GpuResourceScope scope = manager.CreateScope(); GpuBufferRef buffer = scope.CreateBuffer(new(32));
        GpuResourceUse use = manager.AcquireUse(buffer); using GpuResourceBatch batch = manager.BeginBatch();
        batch.Retain(use); batch.Retain(new FailingLease());
        await batch.Submit().WaitAsync(); batch.Dispose(); scope.Dispose();

        Assert.Throws<AggregateException>(() => manager.Collect());

        Assert.Empty(backend.Destroyed);
        Assert.NotEqual(0ul, manager.GetGpuAddress(buffer));
    }
}
