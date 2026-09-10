using Lumyte.Graphics.Native;

namespace Lumyte.Graphics.Vulkan.Tests;

[Collection("GpuBackend")]
public sealed unsafe class VulkanNativeMemoryTests
{
    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void RegionsShareOneMappingAndOwnIndependentByteRanges()
    {
        using var backend = VulkanBackend.Create();
        var requirements = backend.GetLinearMemoryRequirements(256, NativeGpuMemoryKind.CpuVisible);
        NativeGpuHeap heap = backend.CreateGpuHeap(checked(requirements.Size * 2), requirements.Alignment,
            NativeGpuMemoryKind.CpuVisible, [requirements.Compatibility]);
        try
        {
            NativeGpuLinearRegion first = backend.CreateLinearRegion(256, heap, 0);
            try
            {
                NativeGpuLinearRegion second = backend.CreateLinearRegion(256, heap, requirements.Size);
                try
                {
                    new Span<byte>((void*)first.CpuAddress, 256).Fill(17);
                    new Span<byte>((void*)second.CpuAddress, 256).Fill(31);

                    Assert.Equal(checked(first.CpuAddress + (nint)requirements.Size), second.CpuAddress);
                    Assert.Equal(Enumerable.Repeat((byte)17, 256), new ReadOnlySpan<byte>((void*)first.CpuAddress, 256).ToArray());
                    Assert.Equal(Enumerable.Repeat((byte)31, 256), new ReadOnlySpan<byte>((void*)second.CpuAddress, 256).ToArray());
                }
                finally { backend.DestroyLinearRegion(second); }
            }
            finally { backend.DestroyLinearRegion(first); }
        }
        finally { backend.DestroyGpuHeap(heap); }
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void DestroyingARegionLeavesItsHeapAvailableForReuse()
    {
        using var backend = VulkanBackend.Create();
        var requirements = backend.GetLinearMemoryRequirements(256, NativeGpuMemoryKind.GpuOnly);
        NativeGpuHeap heap = backend.CreateGpuHeap(requirements.Size, requirements.Alignment,
            NativeGpuMemoryKind.GpuOnly, [requirements.Compatibility]);
        try
        {
            var first = backend.CreateLinearRegion(256, heap, 0);
            backend.DestroyLinearRegion(first);

            var replacement = backend.CreateLinearRegion(256, heap, 0);
            try
            {
                Assert.NotEqual(0ul, replacement.GpuAddress);
                Assert.Equal(0, replacement.CpuAddress);
            }
            finally { backend.DestroyLinearRegion(replacement); }
        }
        finally { backend.DestroyGpuHeap(heap); }
    }
}
