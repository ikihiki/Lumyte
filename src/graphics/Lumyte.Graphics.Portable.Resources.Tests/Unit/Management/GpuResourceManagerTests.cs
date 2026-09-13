namespace Lumyte.Graphics.Portable.Resources.Tests.Unit.Management;

public sealed class GpuResourceManagerTests
{
    private static readonly GpuBufferDescription BufferDescription = new(16, GpuBufferUsage.Storage | GpuBufferUsage.CopySource | GpuBufferUsage.CopyDestination);

    [Fact]
    public async Task ReleasedIdentityCannotAliasAReusedBuffer()
    {
        var backend = new ManagerTestBackend();
        await using var manager = new GpuResourceManager(backend);
        using var scope = manager.CreateScope();
        GpuBufferRef first = scope.CreateBuffer(BufferDescription);
        GpuBufferHandle handle = manager.GetBufferRange(first).Buffer;

        scope.Release(first); manager.Collect();
        GpuBufferRef next = scope.CreateBuffer(BufferDescription);

        Assert.Same(handle, manager.GetBufferRange(next).Buffer);
        Assert.NotSame(first, next);
        Assert.Throws<ObjectDisposedException>(() => manager.GetBufferRange(first));
    }

    [Fact]
    public async Task PinKeepsResourceAfterScopeDisposal()
    {
        var backend = new ManagerTestBackend();
        await using var manager = new GpuResourceManager(backend);
        var scope = manager.CreateScope();
        GpuBufferRef buffer = scope.CreateBuffer(BufferDescription);
        var pin = manager.Pin(buffer);

        scope.Dispose(); manager.Trim();

        Assert.Single(backend.Created);
        Assert.Empty(backend.Destroyed);
        Assert.Equal(16UL, manager.GetBufferRange(buffer).Length);
        pin.Dispose(); manager.Trim();
        Assert.Same(backend.Created[0], Assert.Single(backend.Destroyed));
    }

    [Fact]
    public async Task FailedManagerDisposalLeavesOpenOwnersUsable()
    {
        var backend = new ManagerTestBackend();
        var manager = new GpuResourceManager(backend);
        var scope = manager.CreateScope();

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.DisposeAsync().AsTask());
        scope.CreateBuffer(BufferDescription);
        scope.Dispose();
        await manager.DisposeAsync();

        Assert.False(backend.Disposed);
        Assert.Single(backend.Destroyed);
    }

    [Fact]
    public async Task MappingRetainsItsBufferUntilUnmap()
    {
        var backend = new ManagerTestBackend();
        await using var manager = new GpuResourceManager(backend);
        var scope = manager.CreateScope();
        GpuBufferRef buffer = scope.CreateBuffer(BufferDescription);
        GpuMappedBufferRange mapping = await manager.MapBufferAsync(buffer, GpuMapMode.Write);

        scope.Dispose(); manager.Trim(); mapping.Memory.Span[0] = 91;

        Assert.Empty(backend.Destroyed);
        mapping.Dispose(); manager.Trim();
        Assert.Equal(new[] { "Unmap", "Destroy:Buffer" }, backend.Events);
    }

    [Fact]
    public async Task FailedUnmapKeepsTheManagedBufferUnavailable()
    {
        var backend = new ManagerTestBackend { MappingDisposeError = new InvalidOperationException("unmap uncertain") };
        var manager = new GpuResourceManager(backend);
        var scope = manager.CreateScope();
        GpuBufferRef buffer = scope.CreateBuffer(BufferDescription);
        GpuMappedBufferRange mapping = await manager.MapBufferAsync(buffer, GpuMapMode.Write);
        scope.Dispose();

        Assert.Throws<InvalidOperationException>(() => mapping.Dispose());
        manager.Trim();

        Assert.Empty(backend.Destroyed);
        Assert.Throws<ObjectDisposedException>(() => mapping.Memory);
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task OwnedScopeStopsAcceptingResources()
    {
        var backend = new ManagerTestBackend();
        await using var manager = new GpuResourceManager(backend);
        var scope = manager.CreateScope();
        scope.CreateBuffer(BufferDescription);
        using var batch = manager.BeginBatch();

        batch.Own(scope);

        Assert.Throws<ObjectDisposedException>(() => scope.CreateBuffer(BufferDescription));
        manager.Trim(); Assert.Empty(backend.Destroyed);
        batch.Dispose(); manager.Trim(); Assert.Single(backend.Destroyed);
    }

    [Fact]
    public async Task ScopeUseSnapshotsOnlyItsCurrentOwnership()
    {
        var backend = new ManagerTestBackend();
        await using var manager = new GpuResourceManager(backend);
        var scope = manager.CreateScope();
        GpuBufferRef first = scope.CreateBuffer(BufferDescription);
        using var batch = manager.BeginBatch(); batch.Use(scope);
        GpuBufferRef later = scope.CreateBuffer(BufferDescription with { Size = 32 });
        GpuBufferHandle laterHandle = manager.GetBufferRange(later).Buffer;

        scope.Dispose(); manager.Trim();

        Assert.Same(laterHandle, Assert.Single(backend.Destroyed));
        Assert.Equal(16UL, manager.GetBufferRange(first).Length);
    }

    [Fact]
    public void CleanupAttemptsIndependentRecordingsBeforeReturningDependentLeases()
    {
        var backend = new ManagerTestBackend();
        var manager = new GpuResourceManager(backend);
        var scope = manager.CreateScope();
        GpuBufferRef buffer = scope.CreateBuffer(BufferDescription);
        var batch = manager.BeginBatch(); batch.Use(buffer);
        var first = (ManagerTestBackend.Recording)batch.StartCommandRecording();
        var second = (ManagerTestBackend.Recording)batch.StartCommandRecording();
        first.DisposalError = new InvalidOperationException("recording retained");
        var external = new TestLease(); batch.Retain(external);
        scope.Dispose();

        Assert.Throws<InvalidOperationException>(() => batch.Dispose());
        Assert.Throws<InvalidOperationException>(() => manager.Collect());

        Assert.Equal((1, 1, 0), (first.DisposalAttempts, second.DisposalAttempts, external.Attempts));
        Assert.Empty(backend.Destroyed);
        Assert.Equal(16UL, manager.GetBufferRange(buffer).Length);
    }

    [Fact]
    public async Task ForeignResourceCannotBeHeldByAnotherManager()
    {
        var backend = new ManagerTestBackend();
        await using var first = new GpuResourceManager(backend);
        await using var second = new GpuResourceManager(backend);
        using var scope = first.CreateScope();
        GpuBufferRef buffer = scope.CreateBuffer(BufferDescription);

        ArgumentException error = Assert.Throws<ArgumentException>(() => second.Pin(buffer));

        Assert.Equal("resource", error.ParamName);
        Assert.Equal(0, second.Statistics.ResourceCount);
    }

    [Fact]
    public async Task ExplicitDependencyRetainsTargetAfterItsScopeReleasesIt()
    {
        var backend = new ManagerTestBackend(); await using var manager = new GpuResourceManager(backend); using var scope = manager.CreateScope();
        GpuBufferRef resource = scope.CreateBuffer(BufferDescription);
        GpuBufferRef dependency = scope.CreateBuffer(BufferDescription with { Size = 32 });
        scope.AddDependency(resource, dependency); scope.AddDependency(resource, dependency);

        scope.Release(dependency); manager.Trim();

        Assert.Empty(backend.Destroyed);
        Assert.Equal(32UL, manager.GetBufferRange(dependency).Length);
        scope.Release(resource); manager.Trim(); Assert.Equal(2, backend.Destroyed.Count);
    }

    [Fact]
    public async Task DependencyCycleIsRejectedWithoutChangingOwnership()
    {
        var backend = new ManagerTestBackend(); await using var manager = new GpuResourceManager(backend); using var scope = manager.CreateScope();
        GpuBufferRef first = scope.CreateBuffer(BufferDescription); GpuBufferRef second = scope.CreateBuffer(BufferDescription);
        scope.AddDependency(first, second);

        ArgumentException error = Assert.Throws<ArgumentException>(() => scope.AddDependency(second, first));
        scope.Dispose(); manager.Trim();

        Assert.Contains("cycle", error.Message);
        Assert.Equal(2, backend.Destroyed.Count);
    }

    [Fact]
    public async Task BatchCanOwnAManagedMappingWithoutLeavingAnExternalOwner()
    {
        var backend = new ManagerTestBackend(); var manager = new GpuResourceManager(backend); var scope = manager.CreateScope();
        GpuBufferRef buffer = scope.CreateBuffer(BufferDescription);
        GpuMappedBufferRange mapping = await manager.MapBufferAsync(buffer, GpuMapMode.Write);
        var batch = manager.BeginBatch(); batch.Own(scope); batch.Retain(mapping);

        batch.Dispose(); await manager.DisposeAsync();

        Assert.Equal(new[] { "Unmap", "Destroy:Buffer" }, backend.Events);
    }

    [Fact]
    public async Task FailedRecordingCleanupKeepsItsRetainedMappingAndBufferAlive()
    {
        var backend = new ManagerTestBackend(); var manager = new GpuResourceManager(backend); var scope = manager.CreateScope();
        GpuBufferRef buffer = scope.CreateBuffer(BufferDescription);
        GpuMappedBufferRange mapping = await manager.MapBufferAsync(buffer, GpuMapMode.Write);
        var batch = manager.BeginBatch(); batch.Retain(mapping);
        var recording = (ManagerTestBackend.Recording)batch.StartCommandRecording();
        recording.DisposalError = new InvalidOperationException("recording still owns mapped memory");
        scope.Dispose();

        Assert.Throws<InvalidOperationException>(() => batch.Dispose());
        Assert.Throws<InvalidOperationException>(() => manager.Collect());

        Assert.DoesNotContain("Unmap", backend.Events);
        Assert.Empty(backend.Destroyed);
        Assert.Equal(16UL, manager.GetBufferRange(buffer).Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetainedManagedLeaseAllowsManagerDrainWithoutAnExternalOwner(bool retainPin)
    {
        var backend = new ManagerTestBackend(); backend.TestQueue.AutoComplete = false;
        var manager = new GpuResourceManager(backend); var scope = manager.CreateScope();
        GpuBufferRef buffer = scope.CreateBuffer(BufferDescription);
        IDisposable lease = retainPin ? manager.Pin(buffer) : manager.AcquireUse(buffer);
        var batch = manager.BeginBatch(); batch.Retain(lease); batch.StartCommandRecording();
        GpuSubmissionToken token = batch.Submit(); scope.Dispose(); batch.Dispose();

        Assert.Throws<InvalidOperationException>(() => lease.Dispose());
        Task draining = manager.DisposeAsync().AsTask();
        Assert.False(draining.IsCompleted);
        backend.TestQueue.Complete(0); await draining;

        Assert.True(token.IsComplete);
        Assert.Single(backend.Destroyed);
    }

    [Fact]
    public void FailedExternalLeaseKeepsSuccessfullyClosedManagedLeaseResourcesRetained()
    {
        var backend = new ManagerTestBackend(); var manager = new GpuResourceManager(backend); var scope = manager.CreateScope();
        GpuBufferRef buffer = scope.CreateBuffer(BufferDescription);
        var batch = manager.BeginBatch(); batch.Retain(manager.Pin(buffer));
        var external = new TestLease(new InvalidOperationException("external dependency is uncertain")); batch.Retain(external);
        scope.Dispose();

        Assert.Throws<InvalidOperationException>(() => batch.Dispose());
        Assert.Throws<InvalidOperationException>(() => manager.Collect());

        Assert.Equal(1, external.Attempts);
        Assert.Equal(16UL, manager.GetBufferRange(buffer).Length);
        Assert.Empty(backend.Destroyed);
    }

    [Fact]
    public async Task RetainRejectsDuplicateIdentityWithoutChangingResourceOwnership()
    {
        var backend = new ManagerTestBackend(); await using var manager = new GpuResourceManager(backend);
        var scope = manager.CreateScope(); GpuBufferRef buffer = scope.CreateBuffer(BufferDescription);
        using var batch = manager.BeginBatch(); batch.Use(buffer);
        var first = new EqualLease(); var distinct = new EqualLease(); batch.Retain(first);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => batch.Retain(first));
        batch.Retain(distinct); // Value equality cannot merge independently owned leases.
        scope.Dispose(); manager.Trim();

        Assert.Contains("already retained", error.Message);
        Assert.Empty(backend.Destroyed);
        Assert.Equal(16UL, manager.GetBufferRange(buffer).Length);
        batch.Dispose(); manager.Trim();
        Assert.Equal((1, 1), (first.Attempts, distinct.Attempts));
        Assert.Single(backend.Destroyed);
    }

    private sealed class EqualLease : IDisposable
    {
        internal int Attempts { get; private set; }
        public void Dispose() => Attempts++;
        public override bool Equals(object? obj) => obj is EqualLease;
        public override int GetHashCode() => 0;
    }

    internal sealed class TestLease(Exception? error = null) : IDisposable
    {
        internal int Attempts { get; private set; }
        public void Dispose() { Attempts++; if (error is not null) { throw error; } }
    }
}
