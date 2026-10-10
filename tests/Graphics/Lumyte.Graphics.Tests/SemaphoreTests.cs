using Lumyte.Graphics.Samples;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks explicit binary GPU synchronization using the common API.</summary>
public sealed class SemaphoreTests
{
    /// <summary>Checks ordered wgpu semaphore submissions and validation.</summary>
    /// <returns>The asynchronous verification.</returns>
    [GpuFact]
    public async Task WgpuExplicitSemaphoresAsync()
    {
        using var device = WgpuDevice.Create();
        Assert.Contains("Semaphore checks passed", await SemaphoreExercise.RunAsync(device));
        using var foreign = WgpuDevice.Create();
        SemaphoreExercise.CheckForeignDevice(device, foreign);
    }

    /// <summary>Checks native Vulkan binary semaphore submissions and validation.</summary>
    /// <returns>The asynchronous verification.</returns>
    [GpuFact]
    public async Task VulkanExplicitSemaphoresAsync()
    {
        using var device = VulkanDevice.Create();
        Assert.Contains("Semaphore checks passed", await SemaphoreExercise.RunAsync(device));
        using var foreign = VulkanDevice.Create();
        SemaphoreExercise.CheckForeignDevice(device, foreign);
    }
}
