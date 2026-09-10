using Lumyte.Graphics.Native;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan.Tests;

public sealed unsafe class VulkanCommandMappingsTests
{
    [Fact]
    public void DescriptorReadCoversResourceAndSamplerHeaps()
    {
        Assert.Equal((AccessFlags2)0x600000000000000UL, VulkanBackend.CommandAccess(GpuAccess.DescriptorRead));
    }

    [Fact]
    public void HostReadbackScopePreservesHostVisibility()
    {
        Assert.Equal((PipelineStageFlags2.HostBit, AccessFlags2.HostReadBit),
            (VulkanBackend.CommandStages(GpuStage.Host), VulkanBackend.CommandAccess(GpuAccess.HostRead)));
    }

    [Theory]
    [InlineData(GpuFormat.Rgba8Unorm, NativeGpuTextureAspect.Color, 4)]
    [InlineData(GpuFormat.R8Unorm, NativeGpuTextureAspect.Color, 1)]
    [InlineData(GpuFormat.Rg8Unorm, NativeGpuTextureAspect.Color, 2)]
    [InlineData(GpuFormat.D32Float, NativeGpuTextureAspect.Depth, 4)]
    [InlineData(GpuFormat.Depth24PlusStencil8, NativeGpuTextureAspect.Depth, 4)]
    [InlineData(GpuFormat.Depth24PlusStencil8, NativeGpuTextureAspect.Stencil, 1)]
    public void TextureCopyConvertsByteStridesAndPreservesRelativeAddress(GpuFormat format, NativeGpuTextureAspect aspect, uint elementSize)
    {
        NativeGpuRange range = new(new TestRegion(), 256, 2048);
        NativeGpuTextureCopyFootprint footprint = new(2, aspect, 3, 2, new(1, 2, 0), new(7, 4, 1), 32 * elementSize, 256 * elementSize);

        NativeDeviceMemoryImageCopy copy = VulkanBackend.TextureCopyRegion(format, range, footprint);

        Assert.Equal((4352UL, 2048UL, 32U, 8U, 2U, 3U, 2U, 1, 2, 7U, 4U),
            (copy.AddressRange.Address, copy.AddressRange.Size, copy.AddressRowLength, copy.AddressImageHeight,
                copy.ImageSubresource.MipLevel, copy.ImageSubresource.BaseArrayLayer, copy.ImageSubresource.LayerCount,
                copy.ImageOffset.X, copy.ImageOffset.Y, copy.ImageExtent.Width, copy.ImageExtent.Height));
    }

    [Theory]
    [InlineData(9UL, 18UL)]
    [InlineData(12UL, 25UL)]
    public void TextureCopyRejectsInexactVulkanStrides(ulong rowPitch, ulong imagePitch)
    {
        NativeGpuRange range = new(new TestRegion(), 0, 2048);
        NativeGpuTextureCopyFootprint footprint = new(0, NativeGpuTextureAspect.Color, 0, 1, default, new(2, 2, 1), rowPitch, imagePitch);

        var error = Assert.Throws<ArgumentException>(() => VulkanBackend.TextureCopyRegion(GpuFormat.Rgba8Unorm, range, footprint));

        Assert.Equal("footprint", error.ParamName);
    }

    private sealed class TestHeap() : NativeGpuHeap(8192, 256, NativeGpuMemoryKind.GpuOnly);
    private sealed class TestRegion() : NativeGpuLinearRegion(new TestHeap(), 0, 4096, 4096, 0);
}
