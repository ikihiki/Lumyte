using Lumyte.Graphics.Samples;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Validates GPU pipelines through common sample APIs.</summary>
public sealed class PipelineTests
{
    /// <summary>Checks wgpu draw variants, state snapshots and compute programs.</summary>
    /// <returns>The asynchronous verification.</returns>
    [GpuFact]
    public async Task WgpuPipelinesDrawAndDispatchAsync()
    {
        using var device = WgpuDevice.Create();
        using var foreign = WgpuDevice.Create();
        PipelineExercise.CheckForeignDevice(device, foreign);
        Assert.Contains("Pipeline checks passed", await PipelineExercise.RunAsync(device));
    }

    /// <summary>Checks Vulkan native graphics variants and compute programs.</summary>
    /// <returns>The asynchronous verification.</returns>
    [GpuFact]
    public async Task VulkanPipelinesDrawAndDispatchAsync()
    {
        using var device = VulkanDevice.Create();
        using var foreign = VulkanDevice.Create();
        PipelineExercise.CheckForeignDevice(device, foreign);
        Assert.Contains("Pipeline checks passed", await PipelineExercise.RunAsync(device));
    }
}
