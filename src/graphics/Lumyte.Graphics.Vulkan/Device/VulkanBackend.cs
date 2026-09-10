using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Native;
using Silk.NET.Core;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

/// <summary>The Native Vulkan device and independently owned GPU allocations.</summary>
public sealed unsafe partial class VulkanBackend : INativeGpuBackend
{
    internal const uint RequiredApiVersion = (1u << 22) | (4u << 12);
    internal static readonly string[] RequiredExtensions =
    [
        "VK_EXT_descriptor_heap",
        "VK_KHR_device_address_commands",
        "VK_KHR_shader_untyped_pointers",
    ];

    private readonly Vk vk;
    private Instance instance;
    private Device device;
    private PhysicalDeviceMemoryProperties memoryProperties;
    private ulong bufferImageGranularity;
    private DebugUtilsMessengerEXT debugMessenger;
    private delegate* unmanaged<Instance, DebugUtilsMessengerEXT, AllocationCallbacks*, void> destroyDebugMessenger;
    private bool disposed;
    private QueueRecord? mainQueue;

    private VulkanBackend(Vk vk) => this.vk = vk;

    public GpuShaderCodeFormat ShaderCodeFormat => GpuShaderCodeFormat.SpirV;

    // Later implementation stages enable capabilities when their corresponding operations exist.
    public NativeGpuCapabilities Capabilities => new();

    internal bool SupportsSeparateDepthStencilLayouts { get; private set; }

    public NativeGpuQueue MainQueue
    {
        get { VerifyNotDisposed(); return mainQueue!; }
    }

    public static VulkanBackend Create(NativeGpuBackendOptions? options = null)
    {
        var backend = new VulkanBackend(Vk.GetApi());
        try
        {
            backend.Initialize(options ?? new());
            return backend;
        }
        catch
        {
            backend.Dispose();
            throw;
        }
    }

    private void Initialize(NativeGpuBackendOptions options)
    {
        uint loaderVersion = 0;
        Check(vk.EnumerateInstanceVersion(&loaderVersion), "vkEnumerateInstanceVersion");
        if (loaderVersion < RequiredApiVersion)
        {
            throw new NotSupportedException("Vulkan Native requires Vulkan 1.4 or newer.");
        }

        using var layerNames = new NativeNames(options.EnableValidation ? ["VK_LAYER_KHRONOS_validation"] : []);
        using var extensionNames = new NativeNames(options.EnableValidation ? ["VK_EXT_debug_utils"] : []);
        if (options.EnableValidation) { RequireValidation(); }
        ApplicationInfo application = new() { SType = StructureType.ApplicationInfo, ApiVersion = RequiredApiVersion };
        InstanceCreateInfo instanceInfo = new()
        {
            SType = StructureType.InstanceCreateInfo,
            PApplicationInfo = &application,
            EnabledLayerCount = layerNames.Count,
            PpEnabledLayerNames = layerNames.Pointer,
            EnabledExtensionCount = extensionNames.Count,
            PpEnabledExtensionNames = extensionNames.Pointer,
        };
        Check(vk.CreateInstance(in instanceInfo, null, out instance), "vkCreateInstance");
        if (options.EnableValidation) { CreateDebugMessenger(); }

        uint count = 0;
        Check(vk.EnumeratePhysicalDevices(instance, &count, null), "vkEnumeratePhysicalDevices");
        PhysicalDevice[] physicalDevices = new PhysicalDevice[count];
        fixed (PhysicalDevice* pointer = physicalDevices)
        {
            Check(vk.EnumeratePhysicalDevices(instance, &count, pointer), "vkEnumeratePhysicalDevices");
        }

        List<string> unsupported = [];
        foreach (PhysicalDevice physicalDevice in physicalDevices)
        {
            vk.GetPhysicalDeviceProperties(physicalDevice, out PhysicalDeviceProperties properties);
            string name = Marshal.PtrToStringUTF8((nint)properties.DeviceName) ?? "Unknown Vulkan device";
            HashSet<string> availableExtensions = GetDeviceExtensions(physicalDevice);
            string[] missing = MissingRequirements(properties.ApiVersion, availableExtensions);
            if (missing.Length != 0)
            {
                unsupported.Add($"{name}: {string.Join(", ", missing)}");
                continue;
            }
            uint? queueFamily = FindQueueFamily(physicalDevice);
            if (queueFamily is null)
            {
                unsupported.Add($"{name}: graphics and compute queue");
                continue;
            }
            if (!TryCreateDevice(physicalDevice, queueFamily.Value, availableExtensions, out string? missingFeatures))
            {
                unsupported.Add($"{name}: {missingFeatures}");
                continue;
            }

            vk.GetPhysicalDeviceMemoryProperties(physicalDevice, out memoryProperties);
            bufferImageGranularity = properties.Limits.BufferImageGranularity;
            vk.GetDeviceQueue(device, queueFamily.Value, 0, out Queue queue);
            mainQueue = new QueueRecord(this, queue, queueFamily.Value);
            return;
        }
        throw new NotSupportedException("No device satisfies Vulkan Native requirements. Missing: "
            + (unsupported.Count == 0 ? "Vulkan physical device" : string.Join("; ", unsupported)));
    }

    internal static string[] MissingRequirements(uint apiVersion, IReadOnlySet<string> extensions)
    {
        List<string> missing = [];
        if (apiVersion < RequiredApiVersion) { missing.Add("Vulkan 1.4"); }
        foreach (string extension in RequiredExtensions)
        {
            if (!extensions.Contains(extension)) { missing.Add(extension); }
        }
        return missing.ToArray();
    }

    private HashSet<string> GetDeviceExtensions(PhysicalDevice physicalDevice)
    {
        uint count = 0;
        Check(vk.EnumerateDeviceExtensionProperties(physicalDevice, (byte*)null, &count, null), "vkEnumerateDeviceExtensionProperties");
        ExtensionProperties[] properties = new ExtensionProperties[count];
        fixed (ExtensionProperties* pointer = properties)
        {
            Check(vk.EnumerateDeviceExtensionProperties(physicalDevice, (byte*)null, &count, pointer), "vkEnumerateDeviceExtensionProperties");
            HashSet<string> names = new(StringComparer.Ordinal);
            for (int index = 0; index < count; index++)
            {
                names.Add(Marshal.PtrToStringUTF8((nint)pointer[index].ExtensionName)!);
            }
            return names;
        }
    }

    private uint? FindQueueFamily(PhysicalDevice physicalDevice)
    {
        uint count = 0;
        vk.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, &count, null);
        QueueFamilyProperties[] properties = new QueueFamilyProperties[count];
        fixed (QueueFamilyProperties* pointer = properties)
        {
            vk.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, &count, pointer);
        }
        const QueueFlags required = QueueFlags.GraphicsBit | QueueFlags.ComputeBit;
        for (uint index = 0; index < count; index++)
        {
            if (properties[index].QueueCount != 0 && (properties[index].QueueFlags & required) == required) { return index; }
        }
        return null;
    }

    private bool TryCreateDevice(PhysicalDevice physicalDevice, uint queueFamily, HashSet<string> extensions, out string? missing)
    {
        bool supportsUnifiedLayouts = extensions.Contains("VK_KHR_unified_image_layouts");
        PhysicalDeviceUnifiedImageLayoutsFeaturesKHR unified = new()
        {
            SType = StructureType.PhysicalDeviceUnifiedImageLayoutsFeaturesKhr,
        };
        PhysicalDeviceShaderUntypedPointersFeaturesKHR untyped = new()
        {
            SType = StructureType.PhysicalDeviceShaderUntypedPointersFeaturesKhr,
            PNext = supportsUnifiedLayouts ? &unified : null,
        };
        NativeDeviceAddressCommandsFeatures addresses = NativeDeviceAddressCommandsFeatures.Create();
        addresses.PNext = &untyped;
        NativeDescriptorHeapFeatures descriptors = NativeDescriptorHeapFeatures.Create();
        descriptors.PNext = &addresses;
        PhysicalDeviceVulkan14Features features14 = new()
        {
            SType = StructureType.PhysicalDeviceVulkan14Features, PNext = &descriptors,
        };
        PhysicalDeviceVulkan13Features features13 = new()
        {
            SType = StructureType.PhysicalDeviceVulkan13Features, PNext = &features14,
        };
        PhysicalDeviceVulkan12Features features12 = new()
        {
            SType = StructureType.PhysicalDeviceVulkan12Features, PNext = &features13,
        };
        PhysicalDeviceFeatures2 features = new() { SType = StructureType.PhysicalDeviceFeatures2, PNext = &features12 };
        vk.GetPhysicalDeviceFeatures2(physicalDevice, &features);
        List<string> missingFeatures = [];
        if (!descriptors.DescriptorHeap) { missingFeatures.Add("descriptorHeap"); }
        if (!addresses.DeviceAddressCommands) { missingFeatures.Add("deviceAddressCommands"); }
        if (!untyped.ShaderUntypedPointers) { missingFeatures.Add("shaderUntypedPointers"); }
        if (!features12.BufferDeviceAddress) { missingFeatures.Add("bufferDeviceAddress"); }
        if (!features12.TimelineSemaphore) { missingFeatures.Add("timelineSemaphore"); }
        if (!features13.Synchronization2) { missingFeatures.Add("synchronization2"); }
        if (!features13.DynamicRendering) { missingFeatures.Add("dynamicRendering"); }
        if (!features14.Maintenance5) { missingFeatures.Add("maintenance5"); }
        if (!features.Features.ShaderInt64) { missingFeatures.Add("shaderInt64"); }
        if (missingFeatures.Count != 0)
        {
            missing = string.Join(", ", missingFeatures);
            return false;
        }

        // Enable required and explicitly used optional features, never the full query result.
        descriptors.DescriptorHeapCaptureReplay = false;
        unified.UnifiedImageLayoutsVideo = false;
        bool separateDepthStencilLayouts = features12.SeparateDepthStencilLayouts;
        features12 = new()
        {
            SType = StructureType.PhysicalDeviceVulkan12Features,
            PNext = &features13, BufferDeviceAddress = true, TimelineSemaphore = true,
            SeparateDepthStencilLayouts = separateDepthStencilLayouts,
        };
        features13 = new()
        {
            SType = StructureType.PhysicalDeviceVulkan13Features,
            PNext = &features14, Synchronization2 = true, DynamicRendering = true,
        };
        features14 = new()
        {
            SType = StructureType.PhysicalDeviceVulkan14Features,
            PNext = &descriptors, Maintenance5 = true,
        };
        PhysicalDeviceFeatures enabled = new() { ShaderInt64 = true };
        float priority = 1;
        DeviceQueueCreateInfo queueInfo = new()
        {
            SType = StructureType.DeviceQueueCreateInfo,
            QueueFamilyIndex = queueFamily, QueueCount = 1, PQueuePriorities = &priority,
        };
        List<string> enabledExtensions = [.. RequiredExtensions];
        if (supportsUnifiedLayouts && unified.UnifiedImageLayouts) { enabledExtensions.Add("VK_KHR_unified_image_layouts"); }
        else { untyped.PNext = null; }
        using NativeNames names = new(enabledExtensions);
        DeviceCreateInfo deviceInfo = new()
        {
            SType = StructureType.DeviceCreateInfo,
            PNext = &features12, PEnabledFeatures = &enabled,
            QueueCreateInfoCount = 1, PQueueCreateInfos = &queueInfo,
            EnabledExtensionCount = names.Count, PpEnabledExtensionNames = names.Pointer,
        };
        Check(vk.CreateDevice(physicalDevice, in deviceInfo, null, out device), "vkCreateDevice");
        SupportsSeparateDepthStencilLayouts = separateDepthStencilLayouts;
        missing = null;
        return true;
    }

    private void RequireValidation()
    {
        uint count = 0;
        Check(vk.EnumerateInstanceLayerProperties(&count, null), "vkEnumerateInstanceLayerProperties");
        LayerProperties[] layers = new LayerProperties[count];
        bool found = false;
        fixed (LayerProperties* pointer = layers)
        {
            Check(vk.EnumerateInstanceLayerProperties(&count, pointer), "vkEnumerateInstanceLayerProperties");
            for (int index = 0; index < count; index++)
            {
                found |= Marshal.PtrToStringUTF8((nint)pointer[index].LayerName) == "VK_LAYER_KHRONOS_validation";
            }
        }
        if (!found) { throw new NotSupportedException("Requested Vulkan validation is unavailable: VK_LAYER_KHRONOS_validation."); }
    }

    private void CreateDebugMessenger()
    {
        var create = (delegate* unmanaged<Instance, DebugUtilsMessengerCreateInfoEXT*, AllocationCallbacks*, DebugUtilsMessengerEXT*, Result>)
            (nint)vk.GetInstanceProcAddr(instance, "vkCreateDebugUtilsMessengerEXT");
        destroyDebugMessenger = (delegate* unmanaged<Instance, DebugUtilsMessengerEXT, AllocationCallbacks*, void>)
            (nint)vk.GetInstanceProcAddr(instance, "vkDestroyDebugUtilsMessengerEXT");
        if (create == null || destroyDebugMessenger == null)
        {
            throw new NotSupportedException("Requested Vulkan validation is unavailable: VK_EXT_debug_utils.");
        }
        DebugUtilsMessengerCreateInfoEXT info = new()
        {
            SType = StructureType.DebugUtilsMessengerCreateInfoExt,
            MessageSeverity = DebugUtilsMessageSeverityFlagsEXT.WarningBitExt | DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt,
            MessageType = DebugUtilsMessageTypeFlagsEXT.GeneralBitExt | DebugUtilsMessageTypeFlagsEXT.ValidationBitExt | DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt,
            PfnUserCallback = new(&DebugCallback),
        };
        fixed (DebugUtilsMessengerEXT* messenger = &debugMessenger)
        {
            Check(create(instance, &info, null, messenger), "vkCreateDebugUtilsMessengerEXT");
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static Bool32 DebugCallback(DebugUtilsMessageSeverityFlagsEXT severity, DebugUtilsMessageTypeFlagsEXT types,
        DebugUtilsMessengerCallbackDataEXT* data, void* userData)
    {
        try { Trace.WriteLine(Marshal.PtrToStringUTF8((nint)data->PMessage), "Vulkan validation"); }
        catch { /* Exceptions cannot cross the native callback boundary. */ }
        return false;
    }

    internal static void Check(Result result, string operation)
    {
        if (result == Result.ErrorDeviceLost) { throw new GpuDeviceLostException($"{operation}: Vulkan device was lost."); }
        if (result != Result.Success) { throw new NativeGpuException($"{operation}: {result}.", (int)result); }
    }

    private void VerifyNotDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        // Application resources and GPU work must already have been released by the caller.
        mainQueue?.ReleaseInternalObjects();
        if (device.Handle != 0) { vk.DestroyDevice(device, null); }
        if (debugMessenger.Handle != 0) { destroyDebugMessenger(instance, debugMessenger, null); }
        if (instance.Handle != 0) { vk.DestroyInstance(instance, null); }
        vk.Dispose();
    }

    private sealed class NativeNames : IDisposable
    {
        public NativeNames(IEnumerable<string> names)
        {
            string[] values = names.ToArray();
            Count = checked((uint)values.Length);
            Pointer = (byte**)NativeMemory.AllocZeroed((nuint)values.Length, (nuint)sizeof(nint));
            try
            {
                for (int index = 0; index < values.Length; index++) { Pointer[index] = (byte*)Marshal.StringToCoTaskMemUTF8(values[index]); }
            }
            catch
            {
                Dispose();
                throw;
            }
        }
        public uint Count { get; }
        public byte** Pointer { get; }
        public void Dispose()
        {
            for (int index = 0; index < Count; index++) { Marshal.FreeCoTaskMem((nint)Pointer[index]); }
            NativeMemory.Free(Pointer);
        }
    }
}
