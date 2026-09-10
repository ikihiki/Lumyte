using System.Runtime.InteropServices;
using Lumyte.Graphics.Native;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan.Tests;

public sealed unsafe class VulkanNativeDescriptorMappingsTests
{
    [Fact]
    public void ResourceSlotsCoverBothImageAndBufferDescriptors()
    {
        NativeDescriptorHeapProperties properties = Properties();

        var layout = VulkanBackend.DescriptorLayout(properties, NativeGpuDescriptorHeapKind.Resource, 3);

        Assert.Equal(new VulkanBackend.DescriptorStorageLayout(64, 256, 192, 512, 704, 959), layout);
    }

    [Fact]
    public void SamplerStorageUsesItsOwnStrideAndReservedTail()
    {
        var layout = VulkanBackend.DescriptorLayout(Properties(), NativeGpuDescriptorHeapKind.Sampler, 3);

        Assert.Equal(new VulkanBackend.DescriptorStorageLayout(16, 128, 48, 256, 304, 431), layout);
    }

    [Fact]
    public void CapacityOverflowCannotWrapTheHostAllocation()
    {
        NativeDescriptorHeapProperties properties = Properties();
        properties.ImageDescriptorSize = 1UL << 63;

        Assert.Throws<OverflowException>(() => VulkanBackend.DescriptorLayout(properties, NativeGpuDescriptorHeapKind.Resource, 3));
    }

    [Fact]
    public void EmptyCapacityIsRejectedBeforeNativeAllocation()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => VulkanBackend.DescriptorLayout(Properties(), NativeGpuDescriptorHeapKind.Resource, 0));

        Assert.Equal("capacity", error.ParamName);
    }

    [Theory]
    [InlineData(NativeGpuBufferAccess.ReadOnly)]
    [InlineData(NativeGpuBufferAccess.ReadWrite)]
    public void BufferAccessUsesStorageDescriptorWithShaderAccessQualifier(NativeGpuBufferAccess access)
    {
        Assert.Equal(DescriptorType.StorageBuffer, VulkanBackend.BufferDescriptorType(access));
    }

    [Theory]
    [InlineData(NativeGpuTextureDescriptorType.Sampled, DescriptorType.SampledImage)]
    [InlineData(NativeGpuTextureDescriptorType.Storage, DescriptorType.StorageImage)]
    public void TextureAccessSelectsNativeDescriptorType(NativeGpuTextureDescriptorType type, DescriptorType expected)
    {
        Assert.Equal(expected, VulkanBackend.TextureDescriptorType(type));
    }

    [Fact]
    public void SamplerPreservesFilterAddressLodAnisotropyAndComparison()
    {
        var description = new NativeGpuSamplerDescription(NativeGpuSamplerFilter.Nearest, NativeGpuSamplerFilter.Linear,
            NativeGpuSamplerFilter.Nearest, NativeGpuSamplerAddressMode.Repeat, NativeGpuSamplerAddressMode.MirrorRepeat,
            NativeGpuSamplerAddressMode.ClampToEdge, 1.25f, 7.5f, 6, true, GpuCompareOp.LessEqual);

        SamplerCreateInfo sampler = VulkanBackend.SamplerDescription(description);

        Assert.Equal((Filter.Nearest, Filter.Linear, SamplerMipmapMode.Nearest, SamplerAddressMode.Repeat,
            SamplerAddressMode.MirroredRepeat, SamplerAddressMode.ClampToEdge, 1.25f, 7.5f, 6f, true, true, CompareOp.LessOrEqual),
            (sampler.MinFilter, sampler.MagFilter, sampler.MipmapMode, sampler.AddressModeU, sampler.AddressModeV,
            sampler.AddressModeW, sampler.MinLod, sampler.MaxLod, sampler.MaxAnisotropy,
            (bool)sampler.AnisotropyEnable, (bool)sampler.CompareEnable, sampler.CompareOp));
    }

    [Theory]
    [InlineData(1f, false)]
    [InlineData(4.5f, true)]
    [InlineData(0f, true)]
    [InlineData(float.NaN, true)]
    public void OnlyUnitAnisotropyDisablesNativeAnisotropicFiltering(float value, bool enabled)
    {
        var sampler = VulkanBackend.SamplerDescription(new(MaxAnisotropy: value));

        Assert.Equal((value, enabled), (sampler.MaxAnisotropy, (bool)sampler.AnisotropyEnable));
    }

    [Theory]
    [InlineData(NativeGpuTextureAspect.Color, NativeGpuRenderViewFlags.DepthReadOnly)]
    [InlineData(NativeGpuTextureAspect.Depth, NativeGpuRenderViewFlags.StencilReadOnly)]
    [InlineData(NativeGpuTextureAspect.Stencil, NativeGpuRenderViewFlags.DepthReadOnly)]
    public void ReadOnlyFlagsCannotReferToAnAbsentAspect(NativeGpuTextureAspect aspect, NativeGpuRenderViewFlags flags)
    {
        var error = Assert.Throws<ArgumentException>(() => VulkanBackend.VerifyRenderViewFlags(aspect, flags));

        Assert.Equal("flags", error.ParamName);
    }

    [Theory]
    [InlineData(NativeGpuTextureViewDimension.OneD, ImageViewType.Type1D)]
    [InlineData(NativeGpuTextureViewDimension.TwoD, ImageViewType.Type2D)]
    [InlineData(NativeGpuTextureViewDimension.TwoDArray, ImageViewType.Type2DArray)]
    [InlineData(NativeGpuTextureViewDimension.ThreeD, ImageViewType.Type3D)]
    [InlineData(NativeGpuTextureViewDimension.Cube, ImageViewType.TypeCube)]
    [InlineData(NativeGpuTextureViewDimension.CubeArray, ImageViewType.TypeCubeArray)]
    public void TextureViewPreservesDimensionAndEverySubresourceField(NativeGpuTextureViewDimension dimension, ImageViewType expected)
    {
        NativeGpuTextureView view = new(new ForeignTexture(), dimension, GpuFormat.Depth24PlusStencil8, NativeGpuTextureAspect.Stencil, 2, 3, 4, 5);

        ImageViewCreateInfo native = VulkanBackend.TextureViewDescription(view, new Image(19));

        Assert.Equal((19UL, expected, Format.D24UnormS8Uint, ImageAspectFlags.StencilBit, 2U, 3U, 4U, 5U),
            (native.Image.Handle, native.ViewType, native.Format, native.SubresourceRange.AspectMask,
            native.SubresourceRange.BaseMipLevel, native.SubresourceRange.LevelCount,
            native.SubresourceRange.BaseArrayLayer, native.SubresourceRange.LayerCount));
    }

    [Fact]
    public void NewCommandSegmentsReceiveBothIndependentHeapSelections()
    {
        NativeDescriptorHeapBindings bindings = new()
        {
            Resource = BindInfo(1000, 256, 128), Sampler = BindInfo(2000, 128, 64),
        };
        // Context travels in the fake command handle; callbacks never touch shared state.
        BindingCalls calls = default;
        CommandBuffer command = new((nint)(&calls));

        bindings.Apply(command, &BindResource, &BindSampler);
        bindings.Resource = BindInfo(3000, 512, 256);
        bindings.Apply(command, &BindResource, &BindSampler);

        Assert.Equal((2, 2, 3000UL, 512UL, 256UL, 2000UL, 128UL, 64UL),
            (calls.ResourceCalls, calls.SamplerCalls, calls.Resource.HeapRange.Address, calls.Resource.HeapRange.Size,
            calls.Resource.ReservedRangeOffset, calls.Sampler.HeapRange.Address, calls.Sampler.HeapRange.Size,
            calls.Sampler.ReservedRangeOffset));
    }

    [Fact]
    public void UnselectedHeapIsNotInventedForNewSegments()
    {
        NativeDescriptorHeapBindings bindings = new() { Sampler = BindInfo(2000, 128, 64) };
        BindingCalls calls = default;

        bindings.Apply(new((nint)(&calls)), &BindResource, &BindSampler);

        Assert.Equal((0, 1), (calls.ResourceCalls, calls.SamplerCalls));
    }

    private static NativeDescriptorHeapProperties Properties() => new()
    {
        ImageDescriptorSize = 32, ImageDescriptorAlignment = 16,
        BufferDescriptorSize = 64, BufferDescriptorAlignment = 64,
        SamplerDescriptorSize = 16, SamplerDescriptorAlignment = 16,
        ResourceHeapAlignment = 256, SamplerHeapAlignment = 128,
        MinResourceHeapReservedRange = 512, MinSamplerHeapReservedRange = 256,
    };
    private static NativeBindHeapInfo BindInfo(ulong address, ulong size, ulong offset) => new()
    {
        HeapRange = new() { Address = address, Size = size }, ReservedRangeOffset = offset, ReservedRangeSize = size - offset,
    };
    [UnmanagedCallersOnly]
    private static void BindResource(CommandBuffer command, NativeBindHeapInfo* info)
    {
        BindingCalls* calls = (BindingCalls*)command.Handle;
        calls->ResourceCalls++;
        calls->Resource = *info;
    }
    [UnmanagedCallersOnly]
    private static void BindSampler(CommandBuffer command, NativeBindHeapInfo* info)
    {
        BindingCalls* calls = (BindingCalls*)command.Handle;
        calls->SamplerCalls++;
        calls->Sampler = *info;
    }
    private struct BindingCalls
    {
        public int ResourceCalls;
        public int SamplerCalls;
        public NativeBindHeapInfo Resource;
        public NativeBindHeapInfo Sampler;
    }
    private sealed class ForeignTexture : NativeGpuTextureHandle;
}
