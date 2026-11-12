using System.Reflection;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Vulkan;
using Silk.NET.Vulkan;

internal static unsafe class HardwareIdentity
{
    internal static string Verify(VulkanDevice owner)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var api = (Vk)(typeof(VulkanDevice).GetProperty("Api", flags)?.GetValue(owner)
            ?? throw new InvalidOperationException("Vulkan Api metadata changed."));
        var physical = (PhysicalDevice)(typeof(VulkanDevice).GetProperty("PhysicalDevice", flags)?.GetValue(owner)
            ?? throw new InvalidOperationException("PhysicalDevice metadata changed."));
        var driver = new PhysicalDeviceDriverProperties { SType = StructureType.PhysicalDeviceDriverProperties };
        var properties = new PhysicalDeviceProperties2 { SType = StructureType.PhysicalDeviceProperties2, PNext = &driver };
        api.GetPhysicalDeviceProperties2(physical, &properties);
        var identity = properties.Properties;
        string name = Marshal.PtrToStringUTF8((nint)identity.DeviceName) ?? "unknown";
        string driverName = Marshal.PtrToStringUTF8((nint)driver.DriverName) ?? "unknown";
        string driverInfo = Marshal.PtrToStringUTF8((nint)driver.DriverInfo) ?? "unknown";
        if (identity.VendorID != 0x1002 || identity.DeviceID != 0x1586 || identity.DeviceType == PhysicalDeviceType.Cpu)
            throw new InvalidOperationException($"Radeon 8060S (1002:1586) required; got {name}.");
        return $"{name}; vendor={identity.VendorID:x4}; device={identity.DeviceID:x4}; driver={driverName}; {driverInfo}";
    }
}
