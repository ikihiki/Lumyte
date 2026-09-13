namespace Lumyte.Graphics.Portable.Resources.Tests.Unit.Utilities;

public sealed class GpuBufferPoolTests
{
    [Theory]
    [InlineData(64ul, GpuBufferUsage.Uniform)]
    [InlineData(128ul, GpuBufferUsage.Storage)]
    public void DifferentCompleteDescriptionsUseDifferentResources(ulong size, GpuBufferUsage usage)
    {
        var backend = new TestBackend();
        using var pool = new GpuBufferPool(backend);
        GpuBufferLease first = pool.Acquire(new(64, GpuBufferUsage.Storage));
        GpuBufferHandle firstHandle = first.Handle;
        pool.Release(first);

        GpuBufferLease second = pool.Acquire(new(size, usage));
        GpuBufferLease original = pool.Acquire(new(64, GpuBufferUsage.Storage));

        Assert.NotSame(firstHandle, second.Handle);
        Assert.Same(firstHandle, original.Handle);
        pool.Release(second);
        pool.Release(original);
    }

    [Fact]
    public void BufferDescriptionReachesBackendWithoutDuplicatingGpuValidation()
    {
        var backend = new TestBackend();
        using var pool = new GpuBufferPool(backend);
        var description = new GpuBufferDescription(0, (GpuBufferUsage)int.MaxValue);

        GpuBufferLease lease = pool.Acquire(description);

        Assert.Equal(description, lease.Description);
        Assert.Equal(description, Assert.IsType<TestBackend.Buffer>(lease.Handle).Description);
        pool.Release(lease);
    }
}
