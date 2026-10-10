using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Verifies backend target receiving ports before invoking a native window system.</summary>
public sealed class SurfaceSourceTests
{
    /// <summary>Rejects null native handles before wgpu instance or device creation.</summary>
    [Fact]
    public void WgpuRejectsMissingTargetHandles() => Assert.Throws<ArgumentException>(() => WgpuDevice.CreateForPresentation(default, out _));

    /// <summary>Rejects invalid Vulkan source descriptors before creating an instance.</summary>
    [Fact]
    public void VulkanRejectsInvalidSurfaceFactories()
    {
        Assert.Throws<ArgumentNullException>(() => VulkanDevice.CreateForPresentation(null!, out _));
        Assert.Throws<ArgumentNullException>(() => VulkanDevice.CreateForPresentation(new() { InstanceExtensions = [], CreateSurface = null! }, out _));
        bool invoked = false;
        var source = new VulkanSurfaceSource
        {
            InstanceExtensions = [string.Empty],
            CreateSurface = _ =>
            {
                invoked = true;
                return default;
            },
        };
        Assert.Throws<ArgumentException>(() => VulkanDevice.CreateForPresentation(source, out _));
        Assert.False(invoked);
    }
}
