using System.Runtime.InteropServices;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

// VK_KHR_device_address_commands, Vulkan-Headers v1.4.362.
[StructLayout(LayoutKind.Sequential)]
internal struct NativeStridedDeviceAddressRange
{
    public ulong Address;
    public ulong Size;
    public ulong Stride;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeBindIndexBufferInfo
{
    public StructureType SType;
    public void* PNext;
    public NativeDeviceAddressRange AddressRange;
    public uint AddressFlags;
    public IndexType IndexType;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeDrawIndirectInfo
{
    public StructureType SType;
    public void* PNext;
    public NativeStridedDeviceAddressRange AddressRange;
    public uint AddressFlags;
    public uint DrawCount;
}
