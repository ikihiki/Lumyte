using Lumyte.Graphics.Native;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;

namespace Lumyte.Graphics.DirectX12.Tests;

public sealed class DirectX12NativeViewTests
{
    [Fact]
    public void SampledArrayViewPreservesMipLayerAndFormat()
    {
        NativeGpuTextureView view = View with { Dimension = NativeGpuTextureViewDimension.TwoDArray,
            BaseMip = 2, MipCount = 3, BaseLayer = 5, LayerCount = 7, Format = GpuFormat.Rgba8UnormSrgb };

        ShaderResourceViewDesc native = DirectX12Backend.SampledTextureDescription(view, 1);

        Assert.Equal((SrvDimension.Texture2Darray, Format.FormatR8G8B8A8UnormSrgb, 2u, 3u, 5u, 7u),
            (native.ViewDimension, native.Format, native.Texture2DArray.MostDetailedMip,
                native.Texture2DArray.MipLevels, native.Texture2DArray.FirstArraySlice, native.Texture2DArray.ArraySize));
    }

    [Theory]
    [InlineData(GpuFormat.D32Float, NativeGpuTextureAspect.Depth, Format.FormatR32Float, 0u)]
    [InlineData(GpuFormat.Depth24PlusStencil8, NativeGpuTextureAspect.Depth, Format.FormatR24UnormX8Typeless, 0u)]
    [InlineData(GpuFormat.Depth24PlusStencil8, NativeGpuTextureAspect.Stencil, Format.FormatX24TypelessG8Uint, 1u)]
    public void SampledDepthStencilSelectsTheNativePlane(GpuFormat format, NativeGpuTextureAspect aspect,
        Format nativeFormat, uint plane)
    {
        ShaderResourceViewDesc native = DirectX12Backend.SampledTextureDescription(View with { Format = format, Aspect = aspect }, 1);

        Assert.Equal((nativeFormat, plane), (native.Format, native.Texture2D.PlaneSlice));
    }

    [Fact]
    public void CubeArrayPreservesFaceOffsetAndWholeCubeCount()
    {
        ShaderResourceViewDesc native = DirectX12Backend.SampledTextureDescription(View with
        {
            Dimension = NativeGpuTextureViewDimension.CubeArray, BaseLayer = 6, LayerCount = 12, BaseMip = 1, MipCount = 3,
        }, 1);

        Assert.Equal((SrvDimension.Texturecubearray, 6u, 2u, 1u, 3u), (native.ViewDimension,
            native.TextureCubeArray.First2DArrayFace, native.TextureCubeArray.NumCubes,
            native.TextureCubeArray.MostDetailedMip, native.TextureCubeArray.MipLevels));
    }

    [Theory]
    [InlineData(NativeGpuTextureViewDimension.Cube, 6u, 6u)]
    [InlineData(NativeGpuTextureViewDimension.Cube, 0u, 12u)]
    [InlineData(NativeGpuTextureViewDimension.CubeArray, 0u, 7u)]
    [InlineData(NativeGpuTextureViewDimension.OneD, 1u, 1u)]
    [InlineData(NativeGpuTextureViewDimension.TwoD, 0u, 2u)]
    [InlineData(NativeGpuTextureViewDimension.ThreeD, 0u, 4u)]
    public void SampledViewsDoNotDiscardUnrepresentableLayers(NativeGpuTextureViewDimension dimension, uint first, uint count)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => DirectX12Backend.SampledTextureDescription(
            View with { Dimension = dimension, BaseLayer = first, LayerCount = count }, 1));

        Assert.Equal("view", error.ParamName);
    }

    [Theory]
    [InlineData(1u, 1u)]
    [InlineData(0u, 2u)]
    public void MultisampleViewsDoNotDiscardMipRanges(uint first, uint count)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => DirectX12Backend.SampledTextureDescription(
            View with { BaseMip = first, MipCount = count }, 4));

        Assert.Equal("view", error.ParamName);
    }

    [Fact]
    public void MultisampleArrayViewPreservesTheLayerRange()
    {
        ShaderResourceViewDesc native = DirectX12Backend.SampledTextureDescription(View with
        {
            Dimension = NativeGpuTextureViewDimension.TwoDArray, BaseLayer = 3, LayerCount = 2,
        }, 4);

        Assert.Equal((SrvDimension.Texture2Dmsarray, 3u, 2u),
            (native.ViewDimension, native.Texture2DMSArray.FirstArraySlice, native.Texture2DMSArray.ArraySize));
    }

    [Fact]
    public void StorageArrayViewPreservesTheMipAndLayers()
    {
        UnorderedAccessViewDesc native = DirectX12Backend.StorageTextureDescription(View with
        {
            Dimension = NativeGpuTextureViewDimension.TwoDArray, BaseMip = 2, BaseLayer = 3, LayerCount = 4,
        });

        Assert.Equal((UavDimension.Texture2Darray, 2u, 3u, 4u), (native.ViewDimension,
            native.Texture2DArray.MipSlice, native.Texture2DArray.FirstArraySlice, native.Texture2DArray.ArraySize));
    }

    [Fact]
    public void StorageViewDoesNotDiscardExtraMips()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => DirectX12Backend.StorageTextureDescription(View with { MipCount = 2 }));

        Assert.Equal("view", error.ParamName);
    }

    [Theory]
    [InlineData(NativeGpuTextureViewDimension.TwoD, 2u, 1u)]
    [InlineData(NativeGpuTextureViewDimension.TwoDArray, 2u, 3u)]
    public void VolumeAttachmentSliceViewsPreserveDepthSlices(NativeGpuTextureViewDimension dimension, uint first, uint count)
    {
        NativeGpuTextureDescription texture = Texture with { Dimension = NativeGpuTextureDimension.ThreeD, Depth = 16 };

        RenderTargetViewDesc native = DirectX12Backend.RenderTargetDescription(
            View with { Dimension = dimension, BaseMip = 1, BaseLayer = first, LayerCount = count }, texture, NativeGpuRenderViewFlags.None);

        Assert.Equal((RtvDimension.Texture3D, 1u, first, count),
            (native.ViewDimension, native.Texture3D.MipSlice, native.Texture3D.FirstWSlice, native.Texture3D.WSize));
    }

    [Fact]
    public void VolumeViewsCoverTheWholeMipDepth()
    {
        NativeGpuTextureView view = View with { Dimension = NativeGpuTextureViewDimension.ThreeD, BaseMip = 2 };
        NativeGpuTextureDescription texture = Texture with { Dimension = NativeGpuTextureDimension.ThreeD, Depth = 16 };

        RenderTargetViewDesc attachment = DirectX12Backend.RenderTargetDescription(view, texture, NativeGpuRenderViewFlags.None);
        UnorderedAccessViewDesc storage = DirectX12Backend.StorageTextureDescription(view);

        Assert.Equal((2u, 0u, uint.MaxValue),
            (attachment.Texture3D.MipSlice, attachment.Texture3D.FirstWSlice, attachment.Texture3D.WSize));
        Assert.Equal((2u, 0u, uint.MaxValue),
            (storage.Texture3D.MipSlice, storage.Texture3D.FirstWSlice, storage.Texture3D.WSize));
    }

    [Fact]
    public void DepthAttachmentPreservesReadOnlyFlagsAndLayerRange()
    {
        DepthStencilViewDesc native = DirectX12Backend.DepthStencilDescription(View with
        {
            Dimension = NativeGpuTextureViewDimension.TwoDArray, Format = GpuFormat.Depth24PlusStencil8,
            Aspect = NativeGpuTextureAspect.DepthStencil, BaseMip = 2, BaseLayer = 3, LayerCount = 4,
        }, 1, NativeGpuRenderViewFlags.DepthReadOnly | NativeGpuRenderViewFlags.StencilReadOnly);

        Assert.Equal((DsvDimension.Texture2Darray, DsvFlags.ReadOnlyDepth | DsvFlags.ReadOnlyStencil, 2u, 3u, 4u),
            (native.ViewDimension, native.Flags, native.Texture2DArray.MipSlice, native.Texture2DArray.FirstArraySlice, native.Texture2DArray.ArraySize));
    }

    [Theory]
    [InlineData(NativeGpuTextureAspect.Depth)]
    [InlineData(NativeGpuTextureAspect.Stencil)]
    public void CombinedDepthStencilAttachmentDoesNotBroadenSingleAspectSelection(NativeGpuTextureAspect aspect)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => DirectX12Backend.DepthStencilDescription(
            View with { Format = GpuFormat.Depth24PlusStencil8, Aspect = aspect }, 1, NativeGpuRenderViewFlags.None));

        Assert.Equal("view", error.ParamName);
    }

    [Fact]
    public void ColorAttachmentDoesNotDiscardReadOnlyFlags()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            DirectX12Backend.RenderTargetDescription(View, Texture, NativeGpuRenderViewFlags.DepthReadOnly));

        Assert.Equal("flags", error.ParamName);
    }

    private static NativeGpuTextureView View => new(null!, NativeGpuTextureViewDimension.TwoD,
        GpuFormat.Rgba8Unorm, NativeGpuTextureAspect.Color, 0, 1, 0, 1);

    private static NativeGpuTextureDescription Texture => new(NativeGpuTextureDimension.TwoD, 32, 32,
        1, 1, 1, 1, GpuFormat.Rgba8Unorm, NativeGpuTextureUsage.ColorAttachment);
}
