using System.Runtime.InteropServices;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

// Vulkan-Headers v1.4.362; these extension structures are absent from Silk.NET 2.23.
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativePushDataInfo
{
    public StructureType SType;
    public void* PNext;
    public uint Offset;
    public NativeHostAddressRange Data;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeDispatchIndirectInfo
{
    public StructureType SType;
    public void* PNext;
    public NativeDeviceAddressRange AddressRange;
    public uint AddressFlags;
}
