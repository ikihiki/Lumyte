using P = Lumyte.Graphics.Portable;

namespace Lumyte.Graphics.WebGPU.Tests;

public sealed class WebGpuLimitMappingTests
{
    [Theory]
    [InlineData(8u)]
    [InlineData(16u)]
    public void ExplicitImmediateRequestIsNotReplacedWithAFixedRootAbi(uint bytes)
    {
        var requested = new P.GpuRequiredLimits { MaxImmediateSize = bytes };

        var native = WebGpuBackend.MapRequiredLimits(requested, 256);

        Assert.Equal(bytes, native.MaxImmediateSize);
    }

    [Fact]
    public void ExplicitBufferCapacityCannotBeSilentlyConvertedToAnUnspecifiedLimit()
    {
        var requested = new P.GpuRequiredLimits { MaxBufferSize = ulong.MaxValue };

        ArgumentOutOfRangeException failure = Assert.Throws<ArgumentOutOfRangeException>(() =>
            WebGpuBackend.MapRequiredLimits(requested, 256));

        Assert.Equal(nameof(P.GpuRequiredLimits.MaxBufferSize), failure.ParamName);
    }

    [Fact]
    public void ExplicitTextureExtentCannotBeSilentlyConvertedToAnUnspecifiedLimit()
    {
        var requested = new P.GpuRequiredLimits { MaxTextureDimension2D = uint.MaxValue };

        ArgumentOutOfRangeException failure = Assert.Throws<ArgumentOutOfRangeException>(() =>
            WebGpuBackend.MapRequiredLimits(requested, 256));

        Assert.Equal(nameof(P.GpuRequiredLimits.MaxTextureDimension2D), failure.ParamName);
    }

    [Fact]
    public void ExplicitImmediateCapacityCannotBeSilentlyConvertedToAnUnspecifiedLimit()
    {
        var requested = new P.GpuRequiredLimits { MaxImmediateSize = uint.MaxValue };

        ArgumentOutOfRangeException failure = Assert.Throws<ArgumentOutOfRangeException>(() =>
            WebGpuBackend.MapRequiredLimits(requested, 256));

        Assert.Equal(nameof(P.GpuRequiredLimits.MaxImmediateSize), failure.ParamName);
    }
}
