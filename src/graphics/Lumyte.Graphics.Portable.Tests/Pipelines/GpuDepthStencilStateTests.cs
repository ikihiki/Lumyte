namespace Lumyte.Graphics.Portable.Tests.Pipelines;

public sealed class GpuDepthStencilStateTests
{
    [Fact]
    public void OmittedStencilFacesAlwaysPassAndKeepTheirValues()
    {
        var state = new GpuDepthStencilState(StencilTest: true);
        var expected = new GpuStencilFaceState(GpuCompareOp.Always,
            GpuStencilOp.Keep, GpuStencilOp.Keep, GpuStencilOp.Keep);

        Assert.Equal((expected, expected), (state.Front, state.Back));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefaultDepthStencilStateDisablesAllTestsAndWrites(bool zeroInitialized)
    {
        GpuDepthStencilState state = zeroInitialized ? default : new();

        Assert.Equal((false, false, false), (state.DepthTest, state.DepthWrite, state.StencilTest));
    }
}
