using System.Runtime.InteropServices;
using Silk.NET.Core;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

// VK_EXT_descriptor_heap, Vulkan-Headers v1.4.362. Silk.NET 2.23 has no bindings yet.
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeDescriptorHeapProperties
{
    public StructureType SType;
    public void* PNext;
    public ulong SamplerHeapAlignment;
    public ulong ResourceHeapAlignment;
    public ulong MaxSamplerHeapSize;
    public ulong MaxResourceHeapSize;
    public ulong MinSamplerHeapReservedRange;
    public ulong MinSamplerHeapReservedRangeWithEmbedded;
    public ulong MinResourceHeapReservedRange;
    public ulong SamplerDescriptorSize;
    public ulong ImageDescriptorSize;
    public ulong BufferDescriptorSize;
    public ulong SamplerDescriptorAlignment;
    public ulong ImageDescriptorAlignment;
    public ulong BufferDescriptorAlignment;
    public ulong MaxPushDataSize;
    public nuint ImageCaptureReplayOpaqueDataSize;
    public uint MaxDescriptorHeapEmbeddedSamplers;
    public uint SamplerYcbcrConversionCount;
    public Bool32 SparseDescriptorHeaps;
    public Bool32 ProtectedDescriptorHeaps;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeHostAddressRange
{
    public void* Address;
    public nuint Size;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeImageDescriptorInfo
{
    public StructureType SType;
    public void* PNext;
    public ImageViewCreateInfo* View;
    public ImageLayout Layout;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeResourceDescriptorInfo
{
    public StructureType SType;
    public void* PNext;
    public DescriptorType Type;
    // VkResourceDescriptorDataEXT is a union whose alternatives are all pointers.
    public void* Data;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeBindHeapInfo
{
    public StructureType SType;
    public void* PNext;
    public NativeDeviceAddressRange HeapRange;
    public ulong ReservedRangeOffset;
    public ulong ReservedRangeSize;
}
