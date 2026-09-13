using Lumyte.Graphics.Tests;
using Lumyte.Graphics.Native.Resources;

namespace Lumyte.Graphics.DirectX12.Tests;

[Collection("GpuBackend")]
[Trait("Category", "DirectX12Conformance")]
public sealed class DirectX12NativeResourceManagerTests
{
    [Theory]
    [InlineData(GpuPackagePlacement.Pools)]
    [InlineData(GpuPackagePlacement.SingleAllocation)]
    public async Task PreparedPackageUploadsBuffersAndTextures(GpuPackagePlacement placement)
    {
        using DirectX12Backend backend = DirectX12Backend.Create();
        await NativeResourceManagerConformance.UploadMixedPackageAsync(backend, placement);
    }

    [Fact]
    public async Task SubmittedBatchKeepsReleasedScopeAliveThroughCopy()
    {
        using DirectX12Backend backend = DirectX12Backend.Create();
        await NativeResourceManagerConformance.CopyWithReleasedScopeAsync(backend);
    }

    [Fact]
    public async Task ManagedUpdatesAndReadbacksPreserveBufferRangesAndTextureRows()
    {
        using DirectX12Backend backend = DirectX12Backend.Create();
        await NativeResourceManagerConformance.UpdateAndReadResourcesAsync(backend);
    }
}
