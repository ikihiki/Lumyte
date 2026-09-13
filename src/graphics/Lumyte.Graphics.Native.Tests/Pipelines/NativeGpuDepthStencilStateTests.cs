namespace Lumyte.Graphics.Native.Tests.Pipelines;

public sealed class NativeGpuDepthStencilStateTests
{
    [Fact]
    public void EnablingStencilUsesAlwaysCompareAndKeepOperationsForBothFaces()
    {
        var state = new NativeGpuDepthStencilState(StencilTest: true);

        var expected = new NativeGpuStencilFaceState(GpuCompareOp.Always,
            NativeGpuStencilOperation.Keep, NativeGpuStencilOperation.Keep, NativeGpuStencilOperation.Keep);
        Assert.Equal((expected, expected), (state.Front, state.Back));
    }

    [Fact]
    public void ExplicitFaceStateIsPreservedWhenTheOtherFaceIsOmitted()
    {
        var front = new NativeGpuStencilFaceState(GpuCompareOp.Equal,
            NativeGpuStencilOperation.Replace, PassOp: NativeGpuStencilOperation.IncrementWrap, Reference: 37);

        var state = new NativeGpuDepthStencilState(StencilTest: true, Front: front);

        Assert.Equal((front, new NativeGpuStencilFaceState()), (state.Front, state.Back));
    }

    [Fact]
    public void BothDefaultConstructionsDisableAllTestsAndWrites()
    {
        NativeGpuDepthStencilState zero = default;
        var constructed = new NativeGpuDepthStencilState();

        Assert.Equal((false, false, false), (zero.DepthTest, zero.DepthWrite, zero.StencilTest));
        Assert.Equal((false, false, false), (constructed.DepthTest, constructed.DepthWrite, constructed.StencilTest));
    }
}
