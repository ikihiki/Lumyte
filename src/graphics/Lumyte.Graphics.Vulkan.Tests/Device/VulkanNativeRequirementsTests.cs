using Lumyte.Graphics.Native;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan.Tests;

public sealed class VulkanNativeRequirementsTests
{
    [Fact]
    public void MissingExtensionsRemainNamedWhenVulkanVersionIsSufficient()
    {
        HashSet<string> available = ["VK_KHR_shader_untyped_pointers"];

        string[] missing = VulkanBackend.MissingRequirements(VulkanBackend.RequiredApiVersion, available);

        Assert.Collection(missing,
            extension => Assert.Equal("VK_EXT_descriptor_heap", extension),
            extension => Assert.Equal("VK_KHR_device_address_commands", extension));
    }

    [Fact]
    public void OlderVulkanVersionIsRejectedEvenWithAllRequiredExtensions()
    {
        HashSet<string> available = [.. VulkanBackend.RequiredExtensions];

        string[] missing = VulkanBackend.MissingRequirements(Vk.Version13, available);

        Assert.Equal("Vulkan 1.4", Assert.Single(missing));
    }

    [Fact]
    public void AllRequiredExtensionsAndVersionPassTheInitialGate()
    {
        HashSet<string> available = [.. VulkanBackend.RequiredExtensions];

        string[] missing = VulkanBackend.MissingRequirements(VulkanBackend.RequiredApiVersion, available);

        Assert.Empty(missing);
    }

    [Fact]
    public void NativeFailurePreservesItsResultCode()
    {
        var exception = Assert.Throws<NativeGpuException>(
            () => VulkanBackend.Check(Result.ErrorOutOfDeviceMemory, "vkAllocateMemory"));

        Assert.Equal((long)Result.ErrorOutOfDeviceMemory, exception.NativeErrorCode);
        Assert.Contains("vkAllocateMemory", exception.Message);
    }

    [Fact]
    public void DeviceLossHasTheCommonDeviceLossException()
    {
        Assert.Throws<GpuDeviceLostException>(
            () => VulkanBackend.Check(Result.ErrorDeviceLost, "vkAllocateMemory"));
    }
}
