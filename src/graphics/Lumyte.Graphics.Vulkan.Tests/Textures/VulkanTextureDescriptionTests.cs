using Lumyte.Graphics.Native;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan.Tests;

public sealed unsafe class VulkanTextureDescriptionTests
{
    [Theory]
    [InlineData(GpuFormat.Rgba8Unorm, Format.R8G8B8A8Unorm)]
    [InlineData(GpuFormat.Bgra8Unorm, Format.B8G8R8A8Unorm)]
    [InlineData(GpuFormat.Rgba8UnormSrgb, Format.R8G8B8A8Srgb)]
    [InlineData(GpuFormat.Bgra8UnormSrgb, Format.B8G8R8A8Srgb)]
    [InlineData(GpuFormat.R8Unorm, Format.R8Unorm)]
    [InlineData(GpuFormat.Rg8Unorm, Format.R8G8Unorm)]
    [InlineData(GpuFormat.R32Float, Format.R32Sfloat)]
    [InlineData(GpuFormat.D32Float, Format.D32Sfloat)]
    [InlineData(GpuFormat.Depth24PlusStencil8, Format.D24UnormS8Uint)]
    public void FormatsKeepTheirNativeRepresentation(GpuFormat format, Format expected)
    {
        ImageCreateInfo actual = VulkanBackend.TextureImageDescription(Description() with { Format = format });

        Assert.Equal(expected, actual.Format);
    }

    [Theory]
    [InlineData(NativeGpuTextureUsage.Sampled, ImageUsageFlags.SampledBit)]
    [InlineData(NativeGpuTextureUsage.Storage, ImageUsageFlags.StorageBit)]
    [InlineData(NativeGpuTextureUsage.ColorAttachment, ImageUsageFlags.ColorAttachmentBit)]
    [InlineData(NativeGpuTextureUsage.DepthStencilAttachment, ImageUsageFlags.DepthStencilAttachmentBit)]
    [InlineData(NativeGpuTextureUsage.CopySource, ImageUsageFlags.TransferSrcBit)]
    [InlineData(NativeGpuTextureUsage.CopyDestination, ImageUsageFlags.TransferDstBit)]
    public void UsageIsPassedWithoutAddingOtherResourceRoles(NativeGpuTextureUsage usage, ImageUsageFlags expected)
    {
        ImageCreateInfo actual = VulkanBackend.TextureImageDescription(Description() with { Usage = usage });

        Assert.Equal(expected, actual.Usage);
    }

    [Theory]
    [InlineData(NativeGpuTextureDimension.OneD, ImageType.Type1D)]
    [InlineData(NativeGpuTextureDimension.TwoD, ImageType.Type2D)]
    [InlineData(NativeGpuTextureDimension.ThreeD, ImageType.Type3D)]
    public void DimensionsKeepTheirNativeImageType(NativeGpuTextureDimension dimension, ImageType expected)
    {
        ImageCreateInfo actual = VulkanBackend.TextureImageDescription(Description() with { Dimension = dimension });

        Assert.Equal(expected, actual.ImageType);
    }

    [Fact]
    public void CreationPreservesExtentMipsLayersAndSamples()
    {
        var description = new NativeGpuTextureDescription(NativeGpuTextureDimension.ThreeD,
            32, 16, 8, 4, 1, 1, GpuFormat.Rgba8Unorm, NativeGpuTextureUsage.Sampled);

        ImageCreateInfo actual = VulkanBackend.TextureImageDescription(description);

        Assert.Equal((32u, 16u, 8u, 4u, 1u, SampleCountFlags.Count1Bit),
            (actual.Extent.Width, actual.Extent.Height, actual.Extent.Depth, actual.MipLevels, actual.ArrayLayers, actual.Samples));
    }

    [Theory]
    [InlineData(1u, SampleCountFlags.Count1Bit)]
    [InlineData(4u, SampleCountFlags.Count4Bit)]
    [InlineData(8u, SampleCountFlags.Count8Bit)]
    public void SampleCountIsNotReduced(uint samples, SampleCountFlags expected)
    {
        ImageCreateInfo actual = VulkanBackend.TextureImageDescription(Description() with { SampleCount = samples });

        Assert.Equal(expected, actual.Samples);
    }

    [Theory]
    [InlineData(16u, 16u, 6u, 1u, true)]
    [InlineData(16u, 16u, 7u, 1u, true)]
    [InlineData(16u, 16u, 12u, 1u, true)]
    [InlineData(16u, 8u, 6u, 1u, false)]
    [InlineData(16u, 16u, 5u, 1u, false)]
    [InlineData(16u, 16u, 6u, 4u, false)]
    public void EligibleArraysRetainTheCubeViewOption(uint width, uint height, uint layers, uint samples, bool expected)
    {
        var description = Description() with { Width = width, Height = height, LayerCount = layers, SampleCount = samples };

        ImageCreateInfo actual = VulkanBackend.TextureImageDescription(description);

        Assert.Equal(expected, (actual.Flags & ImageCreateFlags.CreateCubeCompatibleBit) != 0);
    }

    [Theory]
    [InlineData(NativeGpuTextureDimension.ThreeD, NativeGpuTextureUsage.ColorAttachment, true)]
    [InlineData(NativeGpuTextureDimension.ThreeD, NativeGpuTextureUsage.Sampled, false)]
    [InlineData(NativeGpuTextureDimension.TwoD, NativeGpuTextureUsage.ColorAttachment, false)]
    public void VolumeAttachmentsRetainTheSliceViewOption(NativeGpuTextureDimension dimension, NativeGpuTextureUsage usage, bool expected)
    {
        ImageCreateInfo actual = VulkanBackend.TextureImageDescription(Description() with { Dimension = dimension, Usage = usage });

        Assert.Equal(expected, (actual.Flags & ImageCreateFlags.Create2DArrayCompatibleBit) != 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MutableFormatRemainsAnExplicitChoice(bool mutable)
    {
        ImageCreateInfo actual = VulkanBackend.TextureImageDescription(Description() with { MutableFormat = mutable });

        Assert.Equal(mutable, (actual.Flags & ImageCreateFlags.CreateMutableFormatBit) != 0);
    }

    [Fact]
    public void NewTexturesAreOptimalAndAwaitInitialization()
    {
        ImageCreateInfo actual = VulkanBackend.TextureImageDescription(Description());

        Assert.Equal((ImageTiling.Optimal, ImageLayout.Undefined, SharingMode.Exclusive, ImageCreateFlags.CreateAliasBit),
            (actual.Tiling, actual.InitialLayout, actual.SharingMode, actual.Flags));
    }

    [Fact]
    public void UnrecognizedUsageIsNotSilentlyDiscarded()
    {
        var description = Description() with { Usage = (NativeGpuTextureUsage)(1 << 20) };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => VulkanBackend.TextureImageDescription(description));

        Assert.Equal("usage", exception.ParamName);
    }

    private static NativeGpuTextureDescription Description()
        => new(NativeGpuTextureDimension.TwoD, 16, 16, 1, 1, 1, 1, GpuFormat.Rgba8Unorm, NativeGpuTextureUsage.Sampled);
}
