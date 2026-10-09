using Lumyte.Graphics.Samples;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks GPU command execution on native devices through common sample APIs.</summary>
public sealed class CommandTests
{
    /// <summary>Checks wgpu buffer and texture copies, clear and submission lifecycle.</summary>
    /// <returns>The asynchronous validation.</returns>
    [GpuFact]
    public async Task WgpuCommandsCopyAndClearAsync()
    {
        using var device = WgpuDevice.Create();
        Assert.Contains("Command checks passed", await CommandExercise.RunAsync(device));
    }

    /// <summary>Checks Vulkan explicit barriers, copy, dynamic rendering and completion.</summary>
    /// <returns>The asynchronous validation.</returns>
    [GpuFact]
    public async Task VulkanCommandsCopyAndClearAsync()
    {
        using var device = VulkanDevice.Create();
        Assert.Contains("Command checks passed", await CommandExercise.RunAsync(device));
    }
}
