using Lumyte.Graphics.Tests;

namespace Lumyte.Graphics.DirectX12.Tests;

[Collection("GpuBackend")]
[Trait("Category", "DirectX12Conformance")]
public sealed class DirectX12NativeArenaTests
{
    [Fact]
    public void ReleasedSlicesCanBePlacedAndCopiedAgain()
    {
        using DirectX12Backend backend = DirectX12Backend.Create();
        NativeMemoryArenaConformance.ReuseLinearSlices(backend);
    }

    [Fact]
    public void MixedSlicesTransferTexelsThroughOneHeap()
    {
        using DirectX12Backend backend = DirectX12Backend.Create();
        NativeMemoryArenaConformance.TransferThroughMixedHeap(backend);
    }
}
