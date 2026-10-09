using Lumyte.Graphics.Samples;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks texture allocation and views on actual devices using common APIs.</summary>
public sealed class TextureTests
{
    /// <summary>Checks wgpu formats, subresource ranges and resource ownership.</summary>
    [GpuFact]
    public void WgpuTexturesRetainViewSources()
    {
        using var device = WgpuDevice.Create();
        Assert.Contains("Texture checks passed", TextureExercise.Run(device));
    }

    /// <summary>Checks Vulkan formats, subresource ranges and resource ownership.</summary>
    [GpuFact]
    public void VulkanTexturesRetainViewSources()
    {
        using var device = VulkanDevice.Create();
        Assert.Contains("Texture checks passed", TextureExercise.Run(device));
    }
}
