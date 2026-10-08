using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks the common capability contract against actual backend devices.</summary>
public sealed class DeviceCapsTests
{
    /// <summary>Checks a native wgpu device through the common interface.</summary>
    [GpuFact]
    public void WgpuReportsStableCapabilities()
    {
        using var device = WgpuDevice.Create();
        AssertCapabilities(device);
    }

    /// <summary>Checks a Vulkan device through the common interface.</summary>
    [GpuFact]
    public void VulkanReportsStableCapabilities()
    {
        using var device = VulkanDevice.Create();
        AssertCapabilities(device);
    }

    private static void AssertCapabilities(IGraphicDevice device)
    {
        DeviceCaps caps = device.Caps;
        Assert.True(caps.MaxBufferSize > 0);
        Assert.InRange(caps.MaxStorageBufferBindingSize, 1UL, caps.MaxBufferSize);
        Assert.True(caps.MaxTextureDimension2D > 0);
        Assert.True(caps.MaxColorAttachments > 0);
        Assert.True(caps.MaxSampledTexturesPerStage > 0);
        Assert.True(caps.MaxSamplersPerStage > 0);
        Assert.True(caps.MaxUniformBuffersPerStage > 0);
        Assert.True(caps.MaxStorageBuffersPerStage > 0);
        Assert.True(caps.MaxComputeInvocationsPerWorkgroup > 0);
        AssertPowerOfTwo(caps.CopyBufferOffsetAlignment);
        AssertPowerOfTwo(caps.CopyBufferSizeAlignment);
        AssertPowerOfTwo(caps.CopyBytesPerRowAlignment);
        AssertPowerOfTwo(caps.StorageBufferOffsetAlignment);
        Assert.Equal(GraphicsFeatures.None, caps.Features & ~(GraphicsFeatures.IndirectDraw | GraphicsFeatures.AnisotropicFiltering | GraphicsFeatures.DepthBiasClamp | GraphicsFeatures.MeshShader));

        _ = device.Caps;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1024; i++)
        {
            DeviceCaps current = device.Caps;
            if (!ReferenceEquals(caps, current))
            {
                throw new InvalidOperationException("Caps allocated a new snapshot.");
            }
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        DeviceCaps changed = caps with { MaxBufferSize = 0 };
        Assert.Equal(0UL, changed.MaxBufferSize);
        Assert.Same(caps, device.Caps);
        Assert.True(device.Caps.MaxBufferSize > 0);
    }

    private static void AssertPowerOfTwo(uint value)
    {
        Assert.True(value > 0);
        Assert.Equal(0U, value & (value - 1));
    }
}
