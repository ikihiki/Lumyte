using Lumyte.Graphics.Samples;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks logical argument tables and opaque references using common APIs.</summary>
public sealed class ArgumentTableTests
{
    /// <summary>Checks wgpu registration generations, typed elements and resource lifetime.</summary>
    [GpuFact]
    public void WgpuArgumentTablesTrackRegistrations()
    {
        using var device = WgpuDevice.Create();
        using var foreign = WgpuDevice.Create();
        Assert.Contains("Argument table checks passed", ArgumentTableExercise.Run(device));
        ArgumentTableExercise.CheckForeignDevice(device, foreign);
    }

    /// <summary>Checks Vulkan registration generations, typed elements and resource lifetime.</summary>
    [GpuFact]
    public void VulkanArgumentTablesTrackRegistrations()
    {
        using var device = VulkanDevice.Create();
        using var foreign = VulkanDevice.Create();
        Assert.Contains("Argument table checks passed", ArgumentTableExercise.Run(device));
        ArgumentTableExercise.CheckForeignDevice(device, foreign);
    }
}
