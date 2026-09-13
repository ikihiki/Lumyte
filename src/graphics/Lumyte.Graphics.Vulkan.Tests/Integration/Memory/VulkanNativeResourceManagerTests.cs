using Lumyte.Graphics.Tests;
using Lumyte.Graphics.Native.Resources;

namespace Lumyte.Graphics.Vulkan.Tests;

[Collection("GpuBackend")]
[Trait("Category", "VulkanNativeConformance")]
public sealed class VulkanNativeResourceManagerTests
{
    [VulkanNativeFact]
    public async Task PreparedPooledPackageUploadsBuffersAndTextures()
    {
        using VulkanBackend backend = VulkanBackend.Create();
        await NativeResourceManagerConformance.UploadMixedPackageAsync(backend, GpuPackagePlacement.Pools);
    }

    [VulkanNativeFact]
    public async Task PreparedSingleAllocationPackageUploadsBuffersAndTextures()
    {
        using VulkanBackend backend = VulkanBackend.Create();
        await NativeResourceManagerConformance.UploadMixedPackageAsync(backend, GpuPackagePlacement.SingleAllocation);
    }

    [VulkanNativeFact]
    public async Task SubmittedBatchKeepsReleasedScopeAliveThroughCopy()
    {
        using VulkanBackend backend = VulkanBackend.Create();
        await NativeResourceManagerConformance.CopyWithReleasedScopeAsync(backend);
    }

    [VulkanNativeFact]
    public async Task ManagedUpdatesAndReadbacksPreserveBufferRangesAndTextureRows()
    {
        using VulkanBackend backend = VulkanBackend.Create();
        await NativeResourceManagerConformance.UpdateAndReadResourcesAsync(backend);
    }
}
