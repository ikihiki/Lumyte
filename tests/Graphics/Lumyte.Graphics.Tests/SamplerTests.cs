using Lumyte.Graphics.Samples;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks sampler allocation and state using common APIs on actual devices.</summary>
public sealed class SamplerTests
{
    /// <summary>Checks wgpu filtering, addressing, LOD, anisotropy and ownership.</summary>
    [GpuFact]
    public void WgpuSamplersPreserveRequestedState()
    {
        using var device = WgpuDevice.Create();
        Assert.Contains("Sampler checks passed", SamplerExercise.Run(device));
    }

    /// <summary>Checks Vulkan filtering, addressing, LOD, anisotropy and ownership.</summary>
    [GpuFact]
    public void VulkanSamplersPreserveRequestedState()
    {
        using var device = VulkanDevice.Create();
        Assert.Contains("Sampler checks passed", SamplerExercise.Run(device));
    }
}
