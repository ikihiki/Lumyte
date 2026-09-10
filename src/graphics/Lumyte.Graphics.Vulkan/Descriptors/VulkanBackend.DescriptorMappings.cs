using Lumyte.Graphics.Native;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

public sealed unsafe partial class VulkanBackend
{
    internal readonly record struct DescriptorStorageLayout(ulong Stride, ulong HeapAlignment,
        ulong ReservedOffset, ulong ReservedSize, ulong BindSize, ulong BackingSize);

    internal static DescriptorStorageLayout DescriptorLayout(NativeDescriptorHeapProperties properties, NativeGpuDescriptorHeapKind kind, uint capacity)
    {
        if (capacity == 0) { throw new ArgumentOutOfRangeException(nameof(capacity)); }
        bool sampler = kind switch
        {
            NativeGpuDescriptorHeapKind.Resource => false,
            NativeGpuDescriptorHeapKind.Sampler => true,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        ulong slotAlignment = sampler ? properties.SamplerDescriptorAlignment
            : Math.Max(properties.ImageDescriptorAlignment, properties.BufferDescriptorAlignment);
        ulong size = sampler ? properties.SamplerDescriptorSize : Math.Max(properties.ImageDescriptorSize, properties.BufferDescriptorSize);
        ulong stride = AlignDescriptorBytes(size, slotAlignment);
        ulong heapAlignment = Math.Max(slotAlignment, sampler ? properties.SamplerHeapAlignment : properties.ResourceHeapAlignment);
        ulong reservedOffset = checked(capacity * stride);
        ulong reservedSize = sampler ? properties.MinSamplerHeapReservedRange : properties.MinResourceHeapReservedRange;
        ulong bindSize = checked(reservedOffset + reservedSize);
        return new(stride, heapAlignment, reservedOffset, reservedSize, bindSize, checked(bindSize + heapAlignment - 1));
    }

    private static ulong AlignDescriptorBytes(ulong bytes, ulong alignment) => checked((bytes + alignment - 1) / alignment * alignment);

    internal static DescriptorType TextureDescriptorType(NativeGpuTextureDescriptorType type) => type switch
    {
        NativeGpuTextureDescriptorType.Sampled => DescriptorType.SampledImage,
        NativeGpuTextureDescriptorType.Storage => DescriptorType.StorageImage,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    // Vulkan storage-buffer descriptors encode the range; SPIR-V controls its read/write access.
    internal static DescriptorType BufferDescriptorType(NativeGpuBufferAccess access) => access switch
    {
        NativeGpuBufferAccess.ReadOnly or NativeGpuBufferAccess.ReadWrite => DescriptorType.StorageBuffer,
        _ => throw new ArgumentOutOfRangeException(nameof(access)),
    };

    internal static SamplerCreateInfo SamplerDescription(NativeGpuSamplerDescription description) => new()
    {
        SType = StructureType.SamplerCreateInfo,
        MinFilter = SamplerFilter(description.MinFilter), MagFilter = SamplerFilter(description.MagFilter),
        MipmapMode = description.MipFilter switch
        {
            NativeGpuSamplerFilter.Nearest => SamplerMipmapMode.Nearest,
            NativeGpuSamplerFilter.Linear => SamplerMipmapMode.Linear,
            _ => throw new ArgumentOutOfRangeException(nameof(description)),
        },
        AddressModeU = SamplerAddress(description.AddressU), AddressModeV = SamplerAddress(description.AddressV),
        AddressModeW = SamplerAddress(description.AddressW),
        MinLod = description.MinLod, MaxLod = description.MaxLod,
        AnisotropyEnable = description.MaxAnisotropy != 1, MaxAnisotropy = description.MaxAnisotropy,
        CompareEnable = description.CompareEnabled,
        CompareOp = description.CompareOp switch
        {
            GpuCompareOp.Never => CompareOp.Never, GpuCompareOp.Less => CompareOp.Less,
            GpuCompareOp.Equal => CompareOp.Equal, GpuCompareOp.LessEqual => CompareOp.LessOrEqual,
            GpuCompareOp.Greater => CompareOp.Greater, GpuCompareOp.NotEqual => CompareOp.NotEqual,
            GpuCompareOp.GreaterEqual => CompareOp.GreaterOrEqual, GpuCompareOp.Always => CompareOp.Always,
            _ => throw new ArgumentOutOfRangeException(nameof(description)),
        },
    };

    private static Filter SamplerFilter(NativeGpuSamplerFilter filter) => filter switch
    {
        NativeGpuSamplerFilter.Nearest => Filter.Nearest, NativeGpuSamplerFilter.Linear => Filter.Linear,
        _ => throw new ArgumentOutOfRangeException(nameof(filter)),
    };

    private static SamplerAddressMode SamplerAddress(NativeGpuSamplerAddressMode address) => address switch
    {
        NativeGpuSamplerAddressMode.Repeat => SamplerAddressMode.Repeat,
        NativeGpuSamplerAddressMode.MirrorRepeat => SamplerAddressMode.MirroredRepeat,
        NativeGpuSamplerAddressMode.ClampToEdge => SamplerAddressMode.ClampToEdge,
        _ => throw new ArgumentOutOfRangeException(nameof(address)),
    };
}
