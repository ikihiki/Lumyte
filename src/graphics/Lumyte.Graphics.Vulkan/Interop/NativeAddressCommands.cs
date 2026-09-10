using System.Runtime.InteropServices;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

// VK_KHR_device_address_commands, Vulkan-Headers v1.4.362. Silk.NET 2.23 has no bindings yet.
[StructLayout(LayoutKind.Sequential)]
internal struct NativeDeviceAddressRange
{
    public ulong Address;
    public ulong Size;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeDeviceMemoryCopy
{
    public StructureType SType;
    public void* PNext;
    public NativeDeviceAddressRange Source;
    public uint SourceFlags;
    public NativeDeviceAddressRange Destination;
    public uint DestinationFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeCopyDeviceMemoryInfo
{
    public StructureType SType;
    public void* PNext;
    public uint RegionCount;
    public NativeDeviceMemoryCopy* Regions;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeDeviceMemoryImageCopy
{
    public StructureType SType;
    public void* PNext;
    public NativeDeviceAddressRange AddressRange;
    public uint AddressFlags;
    public uint AddressRowLength;
    public uint AddressImageHeight;
    public ImageSubresourceLayers ImageSubresource;
    public ImageLayout ImageLayout;
    public Offset3D ImageOffset;
    public Extent3D ImageExtent;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeCopyDeviceMemoryImageInfo
{
    public StructureType SType;
    public void* PNext;
    public Image Image;
    public uint RegionCount;
    public NativeDeviceMemoryImageCopy* Regions;
}
