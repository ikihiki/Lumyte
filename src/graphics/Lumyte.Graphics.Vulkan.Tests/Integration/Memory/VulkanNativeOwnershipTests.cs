using Lumyte.Graphics.Native;

namespace Lumyte.Graphics.Vulkan.Tests;

[Collection("GpuBackend")]
public sealed class VulkanNativeOwnershipTests
{
    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void RequirementsFromAnotherBackendAreRejected()
    {
        using var backend = VulkanBackend.Create();

        var exception = Assert.Throws<ArgumentException>(() => backend.CreateGpuHeap(
            4096, 256, NativeGpuMemoryKind.GpuOnly, [new ForeignCompatibility()]));

        Assert.Equal("compatibilities", exception.ParamName);
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void RequirementsFromAnotherVulkanDeviceAreRejected()
    {
        using var backend = VulkanBackend.Create();
        using var other = VulkanBackend.Create();
        var requirements = other.GetLinearMemoryRequirements(256, NativeGpuMemoryKind.GpuOnly);

        var exception = Assert.Throws<ArgumentException>(() => backend.CreateGpuHeap(
            requirements.Size, requirements.Alignment, NativeGpuMemoryKind.GpuOnly, [requirements.Compatibility]));

        Assert.Equal("compatibilities", exception.ParamName);
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void ForeignHeapsCannotReceiveVulkanRegions()
    {
        using var backend = VulkanBackend.Create();

        var exception = Assert.Throws<ArgumentException>(() => backend.CreateLinearRegion(256, new ForeignHeap(), 0));

        Assert.Equal("heap", exception.ParamName);
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void ForeignHeapsCannotBeDestroyed()
    {
        using var backend = VulkanBackend.Create();

        var exception = Assert.Throws<ArgumentException>(() => backend.DestroyGpuHeap(new ForeignHeap()));

        Assert.Equal("heap", exception.ParamName);
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void ForeignRegionsCannotBeDestroyed()
    {
        using var backend = VulkanBackend.Create();

        var exception = Assert.Throws<ArgumentException>(() => backend.DestroyLinearRegion(new ForeignRegion()));

        Assert.Equal("region", exception.ParamName);
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void AnotherVulkanDevicesHeapCannotBeDestroyed()
    {
        using var backend = VulkanBackend.Create();
        using var other = VulkanBackend.Create();
        var requirements = other.GetLinearMemoryRequirements(256, NativeGpuMemoryKind.GpuOnly);
        NativeGpuHeap heap = other.CreateGpuHeap(requirements.Size, requirements.Alignment,
            NativeGpuMemoryKind.GpuOnly, [requirements.Compatibility]);
        try
        {
            var exception = Assert.Throws<ArgumentException>(() => backend.DestroyGpuHeap(heap));

            Assert.Equal("heap", exception.ParamName);
        }
        finally { other.DestroyGpuHeap(heap); }
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void AnotherVulkanDevicesRegionCannotBeDestroyed()
    {
        using var backend = VulkanBackend.Create();
        using var other = VulkanBackend.Create();
        var requirements = other.GetLinearMemoryRequirements(256, NativeGpuMemoryKind.GpuOnly);
        NativeGpuHeap heap = other.CreateGpuHeap(requirements.Size, requirements.Alignment,
            NativeGpuMemoryKind.GpuOnly, [requirements.Compatibility]);
        try
        {
            var region = other.CreateLinearRegion(256, heap, 0);
            try
            {
                var exception = Assert.Throws<ArgumentException>(() => backend.DestroyLinearRegion(region));

                Assert.Equal("region", exception.ParamName);
            }
            finally { other.DestroyLinearRegion(region); }
        }
        finally { other.DestroyGpuHeap(heap); }
    }

    private sealed class ForeignCompatibility : NativeGpuMemoryCompatibility;

    private sealed class ForeignHeap() : NativeGpuHeap(4096, 256, NativeGpuMemoryKind.GpuOnly);

    private sealed class ForeignRegion() : NativeGpuLinearRegion(new ForeignHeap(), 0, 256, 0, 0);
}
