using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Samples;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Verifies raw element layouts and common buffer behavior on actual devices.</summary>
public sealed class BufferTests
{
    /// <summary>Checks byte and element alignment conversion without rounding storage.</summary>
    /// <param name="offsetAlignment">The GPU copy offset alignment.</param>
    /// <param name="sizeAlignment">The GPU copy length alignment.</param>
    /// <param name="expectedOffset">The smallest valid element offset.</param>
    /// <param name="expectedCount">The smallest valid element count.</param>
    [Theory]
    [InlineData(4UL, 4UL, 2UL, 2UL)]
    [InlineData(8UL, 4UL, 4UL, 2UL)]
    [InlineData(1UL, 1UL, 1UL, 1UL)]
    public void LayoutExpressesAlignmentForElementType(ulong offsetAlignment, ulong sizeAlignment, ulong expectedOffset, ulong expectedCount)
    {
        var layout = new BufferLayout<ushort>(2, 2, offsetAlignment, sizeAlignment);
        Assert.Equal(expectedOffset, layout.CopyOffsetAlignmentInElements);
        Assert.Equal(expectedCount, layout.CopyCountAlignment);
        Assert.Equal(6UL, layout.GetSizeInBytes(3));
        Assert.Throws<OverflowException>(() => layout.GetSizeInBytes(ulong.MaxValue));
        Assert.Throws<InvalidOperationException>(() => default(BufferLayout<ushort>).GetSizeInBytes(1));
    }

    /// <summary>Rejects padding and invalid backend-provided layout values.</summary>
    [Fact]
    public void LayoutRejectsPaddingAndUnknownElementSizes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BufferLayout<uint>(4, 8, 4, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BufferLayout<uint>(8, 8, 4, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BufferLayout<uint>(4, 4, 0, 4));
    }

    /// <summary>Checks wgpu allocation, mapping, CPU copies and lifetime through common interfaces.</summary>
    /// <returns>A task that completes when common buffer checks finish.</returns>
    [GpuFact]
    public async Task WgpuBuffersUseExplicitCpuAccessAsync()
    {
        using var device = WgpuDevice.Create();
        Assert.Contains("Buffer checks passed", await BufferExercise.RunAsync(device));
    }

    /// <summary>Checks Vulkan allocation, mapping, CPU copies and lifetime through common interfaces.</summary>
    /// <returns>A task that completes when common buffer checks finish.</returns>
    [GpuFact]
    public async Task VulkanBuffersUseExplicitCpuAccessAsync()
    {
        using var device = VulkanDevice.Create();
        Assert.Contains("Buffer checks passed", await BufferExercise.RunAsync(device));
    }
}
