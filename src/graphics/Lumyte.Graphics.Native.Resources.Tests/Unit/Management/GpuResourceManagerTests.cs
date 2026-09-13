namespace Lumyte.Graphics.Native.Resources.Tests.Unit.Management;

public sealed class GpuResourceManagerTests
{
    private static GpuResourceManager Create(TestResourceBackend backend) => new(backend, new(1024, 8, 8));

    [Fact]
    public async Task PinKeepsAReleasedScopeGenerationAlive()
    {
        using TestResourceBackend backend = new(); await using GpuResourceManager manager = Create(backend);
        using GpuResourceScope scope = manager.CreateScope();
        GpuBufferRef buffer = scope.CreateBuffer(new(32));
        GpuResourcePin pin = manager.Pin(buffer);
        scope.Dispose();

        manager.Collect();
        Assert.Empty(backend.Destroyed);
        pin.Dispose(); manager.Collect();

        Assert.Equal(0, manager.Statistics.ResourceCount);
        Assert.Single(backend.Destroyed.OfType<TestResourceBackend.Region>());
        Assert.Throws<InvalidOperationException>(() => manager.GetGpuAddress(buffer));
    }

    [Fact]
    public async Task RegionRelativeAddressDoesNotAddPlacementTwice()
    {
        using TestResourceBackend backend = new(); await using GpuResourceManager manager = Create(backend);
        using GpuResourceScope scope = manager.CreateScope();
        _ = scope.CreateBuffer(new(64)); GpuBufferRef second = scope.CreateBuffer(new(64));

        NativeGpuRange range = manager.GetBufferRange(second, 3, 5);

        Assert.Equal((64ul, 3ul, 0x100043ul), (range.Region.HeapOffset, range.Offset, range.GpuAddress));
    }

    [Fact]
    public async Task ForeignReferencesCannotAcquireOwnership()
    {
        using TestResourceBackend backend = new();
        await using GpuResourceManager owner = Create(backend); await using GpuResourceManager other = Create(backend);
        using GpuResourceScope scope = owner.CreateScope(); GpuBufferRef buffer = scope.CreateBuffer(new(32));

        ArgumentException error = Assert.Throws<ArgumentException>(() => other.Pin(buffer));

        Assert.Equal("reference", error.ParamName);
    }

    [Fact]
    public async Task AViewRetainsItsBufferAfterTheOriginalScopeReleasesIt()
    {
        using TestResourceBackend backend = new(); await using GpuResourceManager manager = Create(backend);
        using GpuResourceScope scope = manager.CreateScope();
        GpuBufferRef buffer = scope.CreateBuffer(new(64)); GpuViewRef view = scope.GetView(buffer, new GpuBufferViewDescription(8, 16));
        scope.Release(buffer);

        manager.Collect();
        Assert.Empty(backend.Destroyed);
        scope.Release(view); manager.Collect();

        Assert.Single(backend.Destroyed.OfType<TestResourceBackend.Region>());
        Assert.Equal(0u, manager.Statistics.ResourceDescriptorCount);
    }

    [Fact]
    public async Task DescriptorSlotReuseCannotReactivateAnOldView()
    {
        using TestResourceBackend backend = new(); await using GpuResourceManager manager = Create(backend);
        using GpuResourceScope scope = manager.CreateScope();
        GpuBufferRef buffer = scope.CreateBuffer(new(64));
        GpuViewRef first = scope.GetView(buffer, new GpuBufferViewDescription(0, 16));
        uint slot = manager.GetShaderIndex(first); scope.Release(first); manager.Collect();

        GpuViewRef second = scope.GetView(buffer, new GpuBufferViewDescription(0, 16));

        Assert.NotSame(first, second);
        Assert.Equal(slot, manager.GetShaderIndex(second));
        Assert.Throws<InvalidOperationException>(() => manager.GetShaderIndex(first));
    }

    [Fact]
    public async Task SamplerCacheSharesOnlyLiveGenerations()
    {
        using TestResourceBackend backend = new(); await using GpuResourceManager manager = Create(backend);
        using GpuResourceScope first = manager.CreateScope(); using GpuResourceScope second = manager.CreateScope();
        GpuSamplerRef one = first.GetSampler(new()); GpuSamplerRef two = second.GetSampler(new());

        first.Dispose(); manager.Collect();

        Assert.Same(one, two);
        Assert.Single(backend.DescriptorWrites);
        Assert.Equal(0u, manager.GetShaderIndex(two));
    }

    [Fact]
    public async Task ExplicitDependenciesKeepTargetsAliveAndRejectCycles()
    {
        using TestResourceBackend backend = new(); await using GpuResourceManager manager = Create(backend);
        using GpuResourceScope scope = manager.CreateScope();
        GpuBufferRef owner = scope.CreateBuffer(new(32)); GpuBufferRef target = scope.CreateBuffer(new(32));
        scope.AddDependency(owner, target);
        Assert.Throws<ArgumentException>(() => scope.AddDependency(target, owner));
        scope.Release(target);

        manager.Collect();

        Assert.Empty(backend.Destroyed);
        scope.Release(owner); manager.Collect();
        Assert.Equal(2, backend.Destroyed.OfType<TestResourceBackend.Region>().Count());
    }

    [Fact]
    public async Task DisposalRejectsLiveOwnersWithoutClosingTheManager()
    {
        using TestResourceBackend backend = new(); GpuResourceManager manager = Create(backend);
        using GpuResourceScope scope = manager.CreateScope(); _ = scope.CreateBuffer(new(16));

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.DisposeAsync().AsTask());
        _ = scope.CreateBuffer(new(16)); scope.Dispose(); await manager.DisposeAsync();

        Assert.Equal(2, backend.Destroyed.OfType<TestResourceBackend.Region>().Count());
        Assert.False(backend.Disposed);
    }

    [Fact]
    public async Task FailedResourceDestructionPreservesItsAllocationAndIndependentCleanup()
    {
        using TestResourceBackend backend = new(); GpuResourceManager manager = Create(backend);
        using GpuResourceScope scope = manager.CreateScope();
        GpuBufferRef failed = scope.CreateBuffer(new(32)); _ = scope.CreateBuffer(new(32));
        NativeGpuLinearRegion failedRegion = manager.GetBufferRange(failed).Region;
        InvalidOperationException cause = new("Resource destruction has an unknown effect.");
        backend.DestructionError = resource => ReferenceEquals(resource, failedRegion) ? cause : null;
        scope.Dispose();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => manager.Collect());
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.DisposeAsync().AsTask());

        Assert.Same(cause, error);
        Assert.Equal(2, backend.Destroyed.OfType<TestResourceBackend.Region>().Count());
        Assert.Empty(backend.Destroyed.OfType<TestResourceBackend.Heap>());
    }

    [Fact]
    public async Task FailedTrimmingKeepsUnconfirmedAllocationAndPreventsFalseDisposalSuccess()
    {
        using TestResourceBackend backend = new(); GpuResourceManager manager = Create(backend);
        using GpuResourceScope scope = manager.CreateScope(); _ = scope.CreateBuffer(new(32));
        scope.Dispose(); manager.Collect();
        ulong allocated = manager.Statistics.AllocatedBytes;
        InvalidOperationException cause = new("Heap destruction has an unknown effect.");
        backend.DestructionError = resource => resource is TestResourceBackend.Heap ? cause : null;

        Assert.Same(cause, Assert.Throws<InvalidOperationException>(() => manager.Trim()));
        Assert.Same(cause, await Assert.ThrowsAsync<InvalidOperationException>(() => manager.DisposeAsync().AsTask()));

        Assert.Equal(allocated, manager.Statistics.AllocatedBytes);
        Assert.Single(backend.Destroyed.OfType<TestResourceBackend.Heap>());
    }
}
