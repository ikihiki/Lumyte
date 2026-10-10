using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Verifies backend target receiving ports before invoking a native window system.</summary>
public sealed class SurfaceSourceTests
{
    /// <summary>Rejects null native handles before wgpu instance or device creation.</summary>
    [Fact]
    public void WgpuRejectsMissingTargetHandles() => Assert.Throws<ArgumentException>(() => WgpuDevice.CreateForPresentation(default(Ahjo.Wgpu.SurfaceSource), out _));

    /// <summary>Rejects invalid Vulkan source descriptors before creating an instance.</summary>
    [Fact]
    public void VulkanRejectsInvalidSurfaceFactories()
    {
        Assert.Throws<ArgumentNullException>(() => VulkanDevice.CreateForPresentation((VulkanSurfaceSource)null!, out _));
        Assert.Throws<ArgumentNullException>(() => VulkanDevice.CreateForPresentation(new VulkanSurfaceSource { InstanceExtensions = [], CreateSurface = null! }, out _));
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

    /// <summary>Validates all wgpu targets and duplicates before invoking a native window system.</summary>
    [Fact]
    public void WgpuRejectsInvalidTargetCollections()
    {
        Assert.Throws<ArgumentNullException>(() => WgpuDevice.CreateForPresentation((IReadOnlyList<Ahjo.Wgpu.SurfaceSource>)null!, out _));
        Assert.Throws<ArgumentException>(() => WgpuDevice.CreateForPresentation(Array.Empty<Ahjo.Wgpu.SurfaceSource>(), out _));
        var validShape = Ahjo.Wgpu.SurfaceSource.WindowsHwnd(1, 2);
        Assert.Throws<ArgumentException>(() => WgpuDevice.CreateForPresentation(new[] { validShape, default }, out _));
        Assert.Throws<ArgumentException>(() => WgpuDevice.CreateForPresentation(new[] { validShape, validShape }, out _));
    }

    /// <summary>Validates every Vulkan descriptor before invoking any callback or creating an instance.</summary>
    [Fact]
    public void VulkanRejectsInvalidTargetCollections()
    {
        Assert.Throws<ArgumentNullException>(() => VulkanDevice.CreateForPresentation((IReadOnlyList<VulkanSurfaceSource>)null!, out _));
        Assert.Throws<ArgumentException>(() => VulkanDevice.CreateForPresentation(Array.Empty<VulkanSurfaceSource>(), out _));
        bool invoked = false;
        var source = new VulkanSurfaceSource
        {
            InstanceExtensions = [],
            CreateSurface = _ =>
            {
                invoked = true;
                return default;
            },
        };
        Assert.Throws<ArgumentNullException>(() => VulkanDevice.CreateForPresentation(new[] { source, null! }, out _));
        Assert.Throws<ArgumentException>(() => VulkanDevice.CreateForPresentation(new[] { source, source with { InstanceExtensions = [string.Empty] } }, out _));
        Assert.False(invoked);
    }
}
