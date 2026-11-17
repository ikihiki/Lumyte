using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Vulkan;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Reports unavailable native Task execution separately from Mesh support.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class MeshTaskGpuFactAttribute : FactAttribute
{
    /// <summary>Initializes a new instance of the <see cref="MeshTaskGpuFactAttribute"/> class.</summary>
    public MeshTaskGpuFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("LUMYTE_GRAPHICS_GPU_TESTS") != "1")
        {
            Skip = "Set LUMYTE_GRAPHICS_GPU_TESTS=1 with a working native driver.";
            return;
        }

        using var device = VulkanDevice.Create();
        if ((device.Caps.Features & (GraphicsFeatures.MeshShader | GraphicsFeatures.TaskShader)) != (GraphicsFeatures.MeshShader | GraphicsFeatures.TaskShader))
        {
            Skip = "The selected Vulkan device does not enable Task and Mesh execution.";
        }
    }
}
