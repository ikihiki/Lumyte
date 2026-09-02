using Lumyte.Graphics;

namespace Lumyte.Graphics.Tests;

public sealed class GpuResourceModelTests
{
    [Fact]
    public void GpuOnlyAllocationRejectsCpuMapping()
    {
        var allocation = new GpuMemoryAllocation(4096, 256, GpuMemoryKind.DeviceLocal, 1, new(0x1000, 0, 4096));

        ArgumentException exception = Assert.Throws<ArgumentException>(() => allocation.Validate());

        Assert.Equal("CpuAddress", exception.ParamName);
    }

    [Fact]
    public void CpuVisibleAllocationRequiresBothAddresses()
    {
        var allocation = new GpuMemoryAllocation(4096, 256, GpuMemoryKind.HostMapped, 0x2000, new(0x1000, 0, 4096));

        GpuMemoryAllocation result = allocation.Validate();

        Assert.Equal((nint)0x2000, result.CpuAddress);
        Assert.Equal(new GpuMemoryAddress(0x1000, 0, 4096), result.MemoryAddress);
    }

    [Fact]
    public void DisjointTransientLifetimesCanAlias()
    {
        var first = new GpuTransientLifetime(0, 2);
        var second = new GpuTransientLifetime(3, 5);

        Assert.False(first.Overlaps(second));
    }

    [Fact]
    public void AttachmentReferencesViewWithoutOwningTexture()
    {
        var view = new GpuTextureView(new(3), new(7), new(GpuFormat.Rgba8Unorm));

        var attachment = new GpuColorAttachment(
            view,
            GpuAttachmentLoadOperation.Clear,
            GpuAttachmentStoreOperation.Store,
            new(0, 0, 0, 1));

        Assert.Equal(new GpuTextureHandle(7), attachment.View.Texture);
    }

    [Fact]
    public unsafe void HostCachedAllocationExposesMappedBytes()
    {
        byte* storage = stackalloc byte[4];
        var allocation = new GpuMemoryAllocation(
            4,
            4,
            GpuMemoryKind.HostCached,
            (nint)storage,
            new(0x1000, 0, 4));

        Span<byte> bytes = allocation.MappedBytes();
        bytes[0] = 42;

        Assert.Equal(42, storage[0]);
    }

    [Fact]
    public void SamplerDescriptionPreservesIndependentReadRules()
    {
        var description = new GpuSamplerDescription(
            GpuSamplerFilter.Linear,
            GpuSamplerFilter.Nearest,
            GpuSamplerAddressMode.Repeat,
            GpuSamplerAddressMode.ClampToEdge);

        GpuSamplerDescription result = description.Validate();

        Assert.Equal(description, result);
    }

    [Fact]
    public void SamplerDescriptionRejectsUnknownValues()
    {
        var description = new GpuSamplerDescription((GpuSamplerFilter)99);

        Assert.Throws<ArgumentOutOfRangeException>(() => description.Validate());
    }
}
