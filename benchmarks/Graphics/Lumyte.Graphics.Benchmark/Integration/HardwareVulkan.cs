using System.Runtime.InteropServices;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Benchmark.Integration;

/// <summary>Rejects unrequested GPU execution and adapters other than the explicitly selected hardware.</summary>
public static class HardwareVulkan
{
    /// <summary>Gets the required absolute hardware ICD manifest.</summary>
    public const string Manifest = "/usr/share/vulkan/icd.d/radeon_icd.json";

    /// <summary>Verifies opt-in, loader selection, and public Vulkan physical-device-zero properties.</summary>
    /// <returns>The independently enumerated hardware identity.</returns>
    public static unsafe string Verify()
    {
        if (Environment.GetEnvironmentVariable("LUMYTE_GRAPHICS_INTEGRATION_GPU") != "1")
        {
            throw new InvalidOperationException("Explicit LUMYTE_GRAPHICS_INTEGRATION_GPU=1 is required.");
        }

        if (Environment.GetEnvironmentVariable("VK_DRIVER_FILES") != Manifest || !File.Exists(Manifest))
        {
            throw new InvalidOperationException($"VK_DRIVER_FILES must select {Manifest}; software fallback is forbidden.");
        }

        using var api = Vk.GetApi();
        var application = new ApplicationInfo { SType = StructureType.ApplicationInfo, ApiVersion = Vk.Version13 };
        var create = new InstanceCreateInfo { SType = StructureType.InstanceCreateInfo, PApplicationInfo = &application };
        if (api.CreateInstance(in create, null, out Instance instance) != Result.Success)
        {
            throw new InvalidOperationException("Cannot enumerate the selected Vulkan hardware.");
        }

        try
        {
            uint count = 0;
            api.EnumeratePhysicalDevices(instance, &count, null);
            if (count != 1)
            {
                throw new InvalidOperationException("The selected manifest must expose exactly one physical adapter.");
            }

            PhysicalDevice physical;
            api.EnumeratePhysicalDevices(instance, &count, &physical);
            api.GetPhysicalDeviceProperties(physical, out PhysicalDeviceProperties properties);
            string name = Marshal.PtrToStringUTF8((nint)properties.DeviceName) ?? string.Empty;
            if (properties.VendorID != 0x1002 || properties.DeviceID != 0x1586 || properties.DeviceType != PhysicalDeviceType.IntegratedGpu || !name.Contains("Radeon 8060S", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Unexpected Vulkan adapter: {name}, {properties.VendorID:x}:{properties.DeviceID:x}, {properties.DeviceType}.");
            }

            return $"{name}; vendor=0x{properties.VendorID:x}; device=0x{properties.DeviceID:x}; {properties.DeviceType}; API={properties.ApiVersion}; driver={properties.DriverVersion}";
        }
        finally
        {
            api.DestroyInstance(instance, null);
        }
    }
}
