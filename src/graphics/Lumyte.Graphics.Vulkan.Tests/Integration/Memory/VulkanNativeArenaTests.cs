using Lumyte.Graphics.Tests;

namespace Lumyte.Graphics.Vulkan.Tests;

[Collection("GpuBackend")]
[Trait("Category", "VulkanNativeConformance")]
public sealed class VulkanNativeArenaTests
{
    [VulkanNativeFact]
    public void ReleasedSlicesCanBePlacedAndCopiedAgain()
    {
        using VulkanBackend backend = VulkanBackend.Create();
        NativeMemoryArenaConformance.ReuseLinearSlices(backend);
    }

    [VulkanNativeFact]
    public void MixedSlicesTransferTexelsThroughOneHeap()
    {
        using VulkanBackend backend = VulkanBackend.Create();
        NativeMemoryArenaConformance.TransferThroughMixedHeap(backend);
    }
}
