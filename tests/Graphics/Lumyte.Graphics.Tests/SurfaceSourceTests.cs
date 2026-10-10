using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Silk.NET.Vulkan;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks independent device options and backend surface creation ports.</summary>
public sealed class SurfaceSourceTests
{
    /// <summary>Checks device options before any native instance is created.</summary>
    [Fact]
    public void VulkanRejectsInvalidDeviceOptions()
    {
        Assert.Throws<ArgumentNullException>(() => VulkanDevice.Create(null!));
        Assert.Throws<ArgumentNullException>(() => VulkanDevice.Create(new VulkanDeviceDesc { InstanceExtensions = null! }));
        Assert.Throws<ArgumentException>(() => VulkanDevice.Create(new VulkanDeviceDesc { InstanceExtensions = [string.Empty] }));
    }

    /// <summary>Checks target collection validation on an independently created device.</summary>
    [GpuFact]
    public void WgpuRejectsInvalidTargets()
    {
        using var device = WgpuDevice.Create();
        Assert.Throws<ArgumentException>(() => device.CreateSurface(default));
        Assert.Throws<ArgumentNullException>(() => device.CreateSurfaces(null!));
        Assert.Throws<ArgumentException>(() => device.CreateSurfaces([]));
        var source = Ahjo.Wgpu.SurfaceSource.WindowsHwnd(1, 2);
        Assert.Throws<ArgumentException>(() => device.CreateSurfaces([source, default]));
        Assert.Throws<ArgumentException>(() => device.CreateSurfaces([source, source]));
    }

    /// <summary>Checks surface callbacks are never called without enabled presentation support.</summary>
    [GpuFact]
    public void VulkanSurfaceCreationRequiresExplicitExtensions()
    {
        using var device = VulkanDevice.Create();
        bool invoked = false;
        SurfaceKHR Factory(Instance instance)
        {
            invoked = true;
            return default;
        }

        Assert.Throws<ArgumentNullException>(() => device.CreateSurface(null!));
        Assert.Throws<ArgumentNullException>(() => device.CreateSurfaces(null!));
        Assert.Throws<ArgumentException>(() => device.CreateSurfaces([]));
        Assert.Throws<ArgumentNullException>(() => device.CreateSurfaces([Factory, null!]));
        Assert.Throws<NotSupportedException>(() => device.CreateSurface(Factory));
        Assert.Throws<NotSupportedException>(() => device.CreateSurfaces([Factory]));
        Assert.False(invoked);
    }
}
