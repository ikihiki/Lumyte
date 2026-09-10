using System.Runtime.InteropServices;
using Silk.NET.Core;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

// Vulkan-Headers 1.4.362, VK_EXT_descriptor_heap / VK_KHR_device_address_commands.
// These two feature structures are not provided by Silk.NET.Vulkan 2.23.0.
// https://github.com/KhronosGroup/Vulkan-Headers/blob/v1.4.362/include/vulkan/vulkan_core.h
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeDescriptorHeapFeatures
{
    public StructureType SType;
    public void* PNext;
    public Bool32 DescriptorHeap;
    public Bool32 DescriptorHeapCaptureReplay;

    public static NativeDescriptorHeapFeatures Create()
        => new() { SType = (StructureType)1000135009 };
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeDeviceAddressCommandsFeatures
{
    public StructureType SType;
    public void* PNext;
    public Bool32 DeviceAddressCommands;

    public static NativeDeviceAddressCommandsFeatures Create()
        => new() { SType = (StructureType)1000318006 };
}
