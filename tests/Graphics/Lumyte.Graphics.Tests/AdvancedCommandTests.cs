using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Samples;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks portable indexed, indirect and depth/stencil commands.</summary>
public sealed class AdvancedCommandTests
{
    /// <summary>Checks the GPU command wire layouts including signed baseVertex.</summary>
    [Fact]
    public void IndirectRecordsMatchGpuCommandLayout()
    {
        Assert.Equal(16, Marshal.SizeOf<DrawIndirectArguments>());
        Assert.Equal(20, Marshal.SizeOf<DrawIndexedIndirectArguments>());
        Assert.Equal(12, Marshal.SizeOf<DispatchIndirectArguments>());
        Assert.Equal(12, Marshal.OffsetOf<DrawIndexedIndirectArguments>(nameof(DrawIndexedIndirectArguments.BaseVertex)).ToInt32());
        Assert.Equal(16, Marshal.OffsetOf<DrawIndexedIndirectArguments>(nameof(DrawIndexedIndirectArguments.FirstInstance)).ToInt32());
    }

    /// <summary>Checks wgpu pixels and GPU-generated indirect dispatch.</summary>
    /// <returns>The asynchronous verification.</returns>
    [GpuFact]
    public async Task WgpuAdvancedCommandsAsync()
    {
        using var device = WgpuDevice.Create();
        Assert.Contains("Advanced command checks passed", await AdvancedCommandExercise.RunAsync(device));
    }

    /// <summary>Checks Vulkan pixels and GPU-generated indirect dispatch.</summary>
    /// <returns>The asynchronous verification.</returns>
    [GpuFact]
    public async Task VulkanAdvancedCommandsAsync()
    {
        using var device = VulkanDevice.Create();
        Assert.Contains("Advanced command checks passed", await AdvancedCommandExercise.RunAsync(device));
    }

    /// <summary>Checks the new attachment variants with Vulkan pipeline caching disabled.</summary>
    /// <returns>The asynchronous verification.</returns>
    [GpuFact]
    public async Task VulkanUncachedAdvancedCommandsAsync()
    {
        using var device = VulkanDevice.Create(cacheGraphicsPipelines: false);
        Assert.Contains("Advanced command checks passed", await AdvancedCommandExercise.RunAsync(device));
    }
}
