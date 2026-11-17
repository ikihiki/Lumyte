using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Vulkan;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Skips native mesh acceptance when the selected driver does not enable the feature.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class MeshGpuFactAttribute : FactAttribute
{
    /// <summary>Initializes a new instance of the <see cref="MeshGpuFactAttribute"/> class.</summary>
    public MeshGpuFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("LUMYTE_GRAPHICS_GPU_TESTS") != "1")
        {
            Skip = "Set LUMYTE_GRAPHICS_GPU_TESTS=1 with a working native driver.";
            return;
        }

        using var device = VulkanDevice.Create();
        if ((device.Caps.Features & GraphicsFeatures.MeshShader) == 0)
        {
            Skip = "The selected Vulkan device has no enabled VK_EXT_mesh_shader Mesh support.";
        }
    }
}
