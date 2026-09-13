using Lumyte.Graphics.Native;

namespace Lumyte.Graphics.DirectX12.Tests;

public sealed class DirectX12NativeRasterStateTests
{
    [Fact]
    public void StencilReferencesDoNotChangeTheFixedPipelineKey()
    {
        var first = new NativeGpuDepthStencilState(StencilTest: true,
            Front: new(Reference: 13), Back: new(Reference: 47));
        var second = first with { Front = first.Front with { Reference = 91 }, Back = first.Back with { Reference = 117 } };

        Assert.Equal(DirectX12Backend.RasterDepthStencilKey.From(first), DirectX12Backend.RasterDepthStencilKey.From(second));
    }

    [Fact]
    public void DisabledTestsIgnoreTheirInactiveFixedStateValues()
    {
        var inactive = new NativeGpuDepthStencilState(DepthWrite: true, DepthCompare: GpuCompareOp.Never,
            StencilReadMask: 13, StencilWriteMask: 97, Front: new(PassOp: NativeGpuStencilOperation.Invert));

        Assert.Equal(DirectX12Backend.RasterDepthStencilKey.From(default), DirectX12Backend.RasterDepthStencilKey.From(inactive));
    }
}
