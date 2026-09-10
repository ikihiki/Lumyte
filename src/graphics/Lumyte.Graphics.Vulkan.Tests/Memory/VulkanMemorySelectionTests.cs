using Lumyte.Graphics.Native;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan.Tests;

public sealed class VulkanMemorySelectionTests
{
    [Theory]
    [InlineData(65ul, 64ul, 1024ul, 1024ul, 1024ul)]
    [InlineData(1025ul, 64ul, 1024ul, 2048ul, 1024ul)]
    [InlineData(4097ul, 4096ul, 1024ul, 8192ul, 4096ul)]
    public void ReservationIncludesTheLargerPlacementBoundary(ulong size, ulong alignment, ulong granularity, ulong expectedSize, ulong expectedAlignment)
    {
        var actual = VulkanBackend.ReserveMemory(size, alignment, granularity);

        Assert.Equal((expectedSize, expectedAlignment), actual);
    }

    [Fact]
    public void ReservationOverflowCannotProduceASmallerAllocation()
    {
        Assert.Throws<OverflowException>(() => VulkanBackend.ReserveMemory(ulong.MaxValue, 64, 1024));
    }

    [Fact]
    public void ReadbackPrefersCachedCoherentMemory()
    {
        MemoryPropertyFlags coherent = MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit;
        MemoryPropertyFlags[] properties = [coherent, coherent | MemoryPropertyFlags.HostCachedBit];

        uint selected = VulkanBackend.SelectMemoryType(0b11, properties, NativeGpuMemoryKind.Readback);

        Assert.Equal(1u, selected);
    }

    [Fact]
    public void ReadbackCannotChooseNoncoherentMemory()
    {
        MemoryPropertyFlags[] properties = [MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCachedBit];

        Assert.Throws<NotSupportedException>(() => VulkanBackend.SelectMemoryType(1, properties, NativeGpuMemoryKind.Readback));
    }

    [Fact]
    public void MemorySelectionRespectsTheResourceCompatibilityMask()
    {
        MemoryPropertyFlags[] properties = [MemoryPropertyFlags.DeviceLocalBit, MemoryPropertyFlags.DeviceLocalBit];

        uint selected = VulkanBackend.SelectMemoryType(0b10, properties, NativeGpuMemoryKind.GpuOnly);

        Assert.Equal(1u, selected);
    }

    [Fact]
    public void EmptyCompatibilityCannotSelectAnotherMemoryKind()
    {
        MemoryPropertyFlags[] properties = [MemoryPropertyFlags.DeviceLocalBit];

        Assert.Throws<NotSupportedException>(() => VulkanBackend.SelectMemoryType(0, properties, NativeGpuMemoryKind.GpuOnly));
    }
}
