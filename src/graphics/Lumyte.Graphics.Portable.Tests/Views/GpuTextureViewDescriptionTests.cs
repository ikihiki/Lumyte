namespace Lumyte.Graphics.Portable.Tests.Views;

public sealed class GpuTextureViewDescriptionTests
{
    [Theory]
    [InlineData(GpuTextureDimension.Texture1D, 1u, GpuTextureViewDimension.Texture1D)]
    [InlineData(GpuTextureDimension.Texture2D, 1u, GpuTextureViewDimension.Texture2D)]
    [InlineData(GpuTextureDimension.Texture2D, 12u, GpuTextureViewDimension.Texture2DArray)]
    [InlineData(GpuTextureDimension.Texture3D, 1u, GpuTextureViewDimension.Texture3D)]
    public void NormalizeDerivesDefaultInterpretationFromTheResource(
        GpuTextureDimension dimension, uint layers, GpuTextureViewDimension expectedDimension)
    {
        var texture = new GpuTextureDescription(dimension, 64, 32, dimension == GpuTextureDimension.Texture3D ? 8u : 1u,
            4, layers, 1, GpuFormat.Rgba8Unorm, GpuTextureUsage.Sampled);

        GpuTextureViewDescription normalized = new GpuTextureViewDescription().Normalize(texture);

        Assert.Equal(new GpuTextureViewDescription(GpuFormat.Rgba8Unorm, expectedDimension,
            MipCount: 4, LayerCount: layers), normalized);
    }

    [Fact]
    public void NormalizeResolvesRangesFromTheirNonzeroOrigins()
    {
        GpuTextureDescription texture = ArrayTexture();
        var view = new GpuTextureViewDescription(BaseMip: 1, BaseLayer: 3);

        GpuTextureViewDescription normalized = view.Normalize(texture);

        Assert.Equal(new GpuTextureViewDescription(GpuFormat.Rgba8Unorm, GpuTextureViewDimension.Texture2DArray,
            BaseMip: 1, MipCount: 3, BaseLayer: 3, LayerCount: 9), normalized);
    }

    [Theory]
    [InlineData(GpuTextureViewDimension.Texture2D, 1u)]
    [InlineData(GpuTextureViewDimension.Cube, 6u)]
    [InlineData(GpuTextureViewDimension.Texture2DArray, 9u)]
    [InlineData(GpuTextureViewDimension.CubeArray, 9u)]
    public void NormalizeUsesTheViewDimensionToResolveOmittedLayers(GpuTextureViewDimension dimension, uint layers)
    {
        var view = new GpuTextureViewDescription(Dimension: dimension, BaseLayer: 3);

        GpuTextureViewDescription normalized = view.Normalize(ArrayTexture());

        Assert.Equal(layers, normalized.LayerCount);
    }

    [Fact]
    public void NormalizePreservesExplicitEmptyRanges()
    {
        var view = new GpuTextureViewDescription(GpuFormat.Rgba8Unorm, GpuTextureViewDimension.Texture2DArray,
            BaseMip: 4, MipCount: 0, BaseLayer: 12, LayerCount: 0);

        GpuTextureViewDescription normalized = view.Normalize(ArrayTexture());

        Assert.Equal(view, normalized);
    }

    [Fact]
    public void NormalizeLeavesGpuCompatibilityDecisionsToTheRuntime()
    {
        var view = new GpuTextureViewDescription(GpuFormat.D32Float, GpuTextureViewDimension.Texture3D,
            GpuTextureAspect.StencilOnly, MipCount: 1, LayerCount: 1);

        GpuTextureViewDescription normalized = view.Normalize(ArrayTexture());

        Assert.Equal(view, normalized);
    }

    [Theory]
    [InlineData(GpuTextureAspect.DepthOnly)]
    [InlineData(GpuTextureAspect.StencilOnly)]
    public void NormalizeRetainsTheLogicalPackedFormatForSingleAspectViews(GpuTextureAspect aspect)
    {
        var texture = new GpuTextureDescription(GpuTextureDimension.Texture2D, 16, 16, 1, 1, 1, 1,
            GpuFormat.Depth24PlusStencil8, GpuTextureUsage.Sampled);

        GpuTextureViewDescription normalized = new GpuTextureViewDescription(Aspect: aspect).Normalize(texture);

        Assert.Equal(new GpuTextureViewDescription(GpuFormat.Depth24PlusStencil8, GpuTextureViewDimension.Texture2D,
            aspect, MipCount: 1, LayerCount: 1), normalized);
    }

    [Theory]
    [InlineData(5u, null, 0u, null, "BaseMip")]
    [InlineData(1u, uint.MaxValue, 0u, null, "MipCount")]
    [InlineData(0u, null, 13u, null, "BaseLayer")]
    [InlineData(0u, null, 1u, uint.MaxValue, "LayerCount")]
    public void NormalizeRejectsHostRangesOutsideTheDescription(
        uint baseMip, uint? mipCount, uint baseLayer, uint? layerCount, string field)
    {
        var view = new GpuTextureViewDescription(BaseMip: baseMip, MipCount: mipCount, BaseLayer: baseLayer, LayerCount: layerCount);

        ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(() => view.Normalize(ArrayTexture()));

        Assert.Equal("textureDescription", error.ParamName);
        Assert.Contains(field, error.Message);
    }

    private static GpuTextureDescription ArrayTexture()
        => new(GpuTextureDimension.Texture2D, 64, 32, 1, 4, 12, 1, GpuFormat.Rgba8Unorm, GpuTextureUsage.Sampled);
}
