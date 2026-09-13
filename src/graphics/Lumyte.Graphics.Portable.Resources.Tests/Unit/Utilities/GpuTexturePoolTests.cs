namespace Lumyte.Graphics.Portable.Resources.Tests.Unit.Utilities;

public sealed class GpuTexturePoolTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void TextureReuseIncludesExtentUsageAndMutableFormat(int change)
    {
        var backend = new TestBackend();
        using var pool = new GpuTexturePool(backend);
        var description = new GpuTextureDescription(GpuTextureDimension.Texture2D, 8, 8, 1, 1, 1, 1,
            GpuFormat.Rgba8Unorm, GpuTextureUsage.Sampled);
        GpuTextureDescription other = change switch
        {
            0 => description with { Width = 16 },
            1 => description with { Usage = GpuTextureUsage.Storage },
            _ => description with { MutableFormat = true },
        };
        GpuTextureLease first = pool.Acquire(description);
        GpuTextureHandle firstHandle = first.Handle;
        pool.Release(first);

        GpuTextureLease second = pool.Acquire(other);
        GpuTextureLease original = pool.Acquire(description);

        Assert.NotSame(firstHandle, second.Handle);
        Assert.Equal(other, Assert.IsType<TestBackend.Texture>(second.Handle).Description);
        Assert.Same(firstHandle, original.Handle);
        pool.Release(second);
        pool.Release(original);
    }

    [Fact]
    public void TextureDescriptionReachesBackendWithoutDuplicatingGpuValidation()
    {
        var backend = new TestBackend();
        using var pool = new GpuTexturePool(backend);
        var description = new GpuTextureDescription((GpuTextureDimension)99, 0, 0, 0, 0, 0, 3,
            (GpuFormat)int.MaxValue, (GpuTextureUsage)int.MaxValue, true);

        GpuTextureLease lease = pool.Acquire(description);

        Assert.Equal(description, lease.Description);
        Assert.Equal(description, Assert.IsType<TestBackend.Texture>(lease.Handle).Description);
        pool.Release(lease);
    }
}
