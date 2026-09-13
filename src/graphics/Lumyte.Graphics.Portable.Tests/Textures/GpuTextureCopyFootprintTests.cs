namespace Lumyte.Graphics.Portable.Tests.Textures;

public sealed class GpuTextureCopyFootprintTests
{
    [Theory]
    [InlineData(GpuFormat.R8Unorm, 6ul)]
    [InlineData(GpuFormat.Rg8Unorm, 12ul)]
    [InlineData(GpuFormat.Rgba8Unorm, 24ul)]
    [InlineData(GpuFormat.Rgba8UnormSrgb, 24ul)]
    [InlineData(GpuFormat.Bgra8Unorm, 24ul)]
    [InlineData(GpuFormat.Bgra8UnormSrgb, 24ul)]
    [InlineData(GpuFormat.R32Float, 24ul)]
    [InlineData(GpuFormat.D32Float, 24ul)]
    public void OmittedStridesCalculateTightlyPackedBytes(GpuFormat format, ulong expected)
    {
        var footprint = new GpuTextureCopyFootprint(0, GpuTextureAspect.All, default, new(3, 2, 1));

        Assert.Equal(expected, footprint.RequiredBytes(format));
    }

    [Fact]
    public void RequiredBytesIncludesImageGapsWithoutFinalRowPadding()
    {
        var footprint = new GpuTextureCopyFootprint(2, GpuTextureAspect.All, new(1, 2, 3), new(4, 3, 2), 256, 1024);

        Assert.Equal(1552ul, footprint.RequiredBytes(GpuFormat.Rgba8Unorm));
    }

    [Fact]
    public void StencilAspectUsesOneBytePerTexel()
    {
        var footprint = new GpuTextureCopyFootprint(0, GpuTextureAspect.StencilOnly, default, new(3, 2, 1), 256);

        Assert.Equal(259ul, footprint.RequiredBytes(GpuFormat.Depth24PlusStencil8));
    }

    [Theory]
    [InlineData(GpuTextureAspect.All)]
    [InlineData(GpuTextureAspect.DepthOnly)]
    public void ImplementationDefinedDepthCannotBeUsedForByteArithmetic(GpuTextureAspect aspect)
    {
        var footprint = new GpuTextureCopyFootprint(0, aspect, default, new(1, 1, 1));

        Assert.Throws<NotSupportedException>(() => footprint.RequiredBytes(GpuFormat.Depth24PlusStencil8));
    }

    [Theory]
    [InlineData(0u, 2u, 3u)]
    [InlineData(2u, 0u, 3u)]
    [InlineData(2u, 3u, 0u)]
    public void EmptyExtentNeedsNoBytes(uint width, uint height, uint depth)
    {
        var footprint = new GpuTextureCopyFootprint(0, GpuTextureAspect.All, default, new(width, height, depth));

        Assert.Equal(0ul, footprint.RequiredBytes(GpuFormat.Rgba8Unorm));
    }

    [Fact]
    public void ByteArithmeticDoesNotValidateGpuPitchAlignment()
    {
        var footprint = new GpuTextureCopyFootprint(0, GpuTextureAspect.All, default, new(3, 2, 2), 13, 27);

        Assert.Equal(52ul, footprint.RequiredBytes(GpuFormat.Rgba8Unorm));
    }

    [Fact]
    public void SingleRowDoesNotMultiplyUnusedImageStride()
    {
        var footprint = new GpuTextureCopyFootprint(0, GpuTextureAspect.All, default, new(1, 1, 1), ulong.MaxValue);

        Assert.Equal(4ul, footprint.RequiredBytes(GpuFormat.Rgba8Unorm));
    }

    [Fact]
    public void ByteCountOverflowIsReportedInsteadOfWrapping()
    {
        var footprint = new GpuTextureCopyFootprint(0, GpuTextureAspect.All, default, new(1, 2, 2), ulong.MaxValue);

        Assert.Throws<OverflowException>(() => footprint.RequiredBytes(GpuFormat.Rgba8Unorm));
    }

    [Fact]
    public void UnknownFormatsCannotInventAByteRepresentation()
    {
        var footprint = new GpuTextureCopyFootprint(0, GpuTextureAspect.All, default, new(1, 1, 1));

        Assert.Equal("format", Assert.Throws<ArgumentOutOfRangeException>(
            () => footprint.RequiredBytes((GpuFormat)int.MaxValue)).ParamName);
    }

    [Fact]
    public void UnknownAspectsCannotInventAByteRepresentation()
    {
        var footprint = new GpuTextureCopyFootprint(0, (GpuTextureAspect)int.MaxValue, default, new(1, 1, 1));

        Assert.Contains("unknown texture aspect", Assert.Throws<InvalidOperationException>(
            () => footprint.RequiredBytes(GpuFormat.Rgba8Unorm)).Message);
    }
}
