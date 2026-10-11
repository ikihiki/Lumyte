using Lumyte.Graphics.Samples;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks automatic graph barriers and temporary ownership on native devices.</summary>
public sealed class RenderGraphTests
{
    /// <summary>Verifies wgpu graph execution with exact buffer and texture readback.</summary>
    /// <returns>The asynchronous verification.</returns>
    [GpuFact]
    public async Task WgpuRenderGraphReadbackAsync()
    {
        using var device = WgpuDevice.Create();
        Assert.Contains("Render graph checks passed", await RenderGraphExercise.RunAsync(device));
    }

    /// <summary>Verifies Vulkan graph execution with exact buffer and texture readback.</summary>
    /// <returns>The asynchronous verification.</returns>
    [GpuFact]
    public async Task VulkanRenderGraphReadbackAsync()
    {
        using var device = VulkanDevice.Create();
        Assert.Contains("Render graph checks passed", await RenderGraphExercise.RunAsync(device));
    }
}
