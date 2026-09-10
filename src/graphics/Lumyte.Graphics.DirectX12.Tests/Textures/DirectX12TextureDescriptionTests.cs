using Lumyte.Graphics.Native;

using Silk.NET.Direct3D12;
using Silk.NET.DXGI;

namespace Lumyte.Graphics.DirectX12.Tests;

public sealed class DirectX12TextureDescriptionTests
{
    [Theory]
    [InlineData(NativeGpuTextureDimension.OneD, 1u, 1u, 5u, ResourceDimension.Texture1D, 5u)]
    [InlineData(NativeGpuTextureDimension.TwoD, 16u, 1u, 6u, ResourceDimension.Texture2D, 6u)]
    [InlineData(NativeGpuTextureDimension.ThreeD, 8u, 4u, 1u, ResourceDimension.Texture3D, 4u)]
    public void DimensionsAndSubresourcesReachTheNativeDescription(
        NativeGpuTextureDimension dimension, uint height, uint depth, uint layers,
        ResourceDimension expectedDimension, uint expectedDepthOrLayers)
    {
        var description = new NativeGpuTextureDescription(
            dimension, 32, height, depth, 3, layers, 1, GpuFormat.Rgba8Unorm, NativeGpuTextureUsage.Sampled);

        ResourceDesc1 native = DirectX12Backend.TextureDescription(description);

        Assert.Equal(
            (expectedDimension, 32ul, height, checked((ushort)expectedDepthOrLayers), (ushort)3),
            (native.Dimension, native.Width, native.Height, native.DepthOrArraySize, native.MipLevels));
    }

    [Theory]
    [InlineData(GpuFormat.Rgba8Unorm, Format.FormatR8G8B8A8Unorm, Format.FormatR8G8B8A8Typeless)]
    [InlineData(GpuFormat.Bgra8Unorm, Format.FormatB8G8R8A8Unorm, Format.FormatB8G8R8A8Typeless)]
    [InlineData(GpuFormat.R32Float, Format.FormatR32Float, Format.FormatR32Typeless)]
    [InlineData(GpuFormat.D32Float, Format.FormatD32Float, Format.FormatR32Typeless)]
    [InlineData(GpuFormat.Rgba8UnormSrgb, Format.FormatR8G8B8A8UnormSrgb, Format.FormatR8G8B8A8Typeless)]
    [InlineData(GpuFormat.Bgra8UnormSrgb, Format.FormatB8G8R8A8UnormSrgb, Format.FormatB8G8R8A8Typeless)]
    [InlineData(GpuFormat.R8Unorm, Format.FormatR8Unorm, Format.FormatR8Typeless)]
    [InlineData(GpuFormat.Rg8Unorm, Format.FormatR8G8Unorm, Format.FormatR8G8Typeless)]
    [InlineData(GpuFormat.Depth24PlusStencil8, Format.FormatD24UnormS8Uint, Format.FormatR24G8Typeless)]
    public void MutableFormatsUseTypelessBacking(GpuFormat format, Format typed, Format typeless)
    {
        NativeGpuTextureDescription description = Description with { Format = format };

        ResourceDesc1 fixedFormat = DirectX12Backend.TextureDescription(description);
        ResourceDesc1 mutableFormat = DirectX12Backend.TextureDescription(description with { MutableFormat = true });

        Assert.Equal((typed, typeless), (fixedFormat.Format, mutableFormat.Format));
    }

    [Theory]
    [InlineData(GpuFormat.D32Float, Format.FormatR32Typeless)]
    [InlineData(GpuFormat.Depth24PlusStencil8, Format.FormatR24G8Typeless)]
    public void SampledDepthUsesTypelessBackingWithoutMutableFormats(GpuFormat format, Format expected)
    {
        NativeGpuTextureDescription description = Description with
        {
            Format = format,
            Usage = NativeGpuTextureUsage.Sampled | NativeGpuTextureUsage.DepthStencilAttachment,
        };

        ResourceDesc1 native = DirectX12Backend.TextureDescription(description);

        Assert.Equal(expected, native.Format);
    }

    [Theory]
    [InlineData(NativeGpuTextureUsage.Storage, ResourceFlags.AllowUnorderedAccess)]
    [InlineData(NativeGpuTextureUsage.ColorAttachment, ResourceFlags.AllowRenderTarget)]
    [InlineData(NativeGpuTextureUsage.DepthStencilAttachment, ResourceFlags.AllowDepthStencil)]
    [InlineData(NativeGpuTextureUsage.Sampled | NativeGpuTextureUsage.CopySource | NativeGpuTextureUsage.CopyDestination,
        ResourceFlags.None)]
    public void UsageReachesNativeResourceFlags(NativeGpuTextureUsage usage, ResourceFlags expected)
    {
        ResourceDesc1 native = DirectX12Backend.TextureDescription(Description with { Usage = usage });

        Assert.Equal(expected, native.Flags);
    }

    [Fact]
    public void MultisampleCountReachesTheNativeDescription()
    {
        ResourceDesc1 native = DirectX12Backend.TextureDescription(Description with { SampleCount = 4 });

        Assert.Equal((4u, 0u), (native.SampleDesc.Count, native.SampleDesc.Quality));
    }

    [Theory]
    [InlineData(NativeGpuTextureDimension.OneD, 2u, 1u)]
    [InlineData(NativeGpuTextureDimension.TwoD, 2u, 1u)]
    [InlineData(NativeGpuTextureDimension.ThreeD, 4u, 2u)]
    public void UnrepresentableDimensionComponentsAreRejected(NativeGpuTextureDimension dimension, uint depth, uint layers)
    {
        NativeGpuTextureDescription description = Description with { Dimension = dimension, Depth = depth, LayerCount = layers };

        ArgumentException error = Assert.Throws<ArgumentException>(() => DirectX12Backend.TextureDescription(description));

        Assert.Equal("description", error.ParamName);
    }

    [Theory]
    [InlineData(NativeGpuTextureDimension.TwoD, 1u, 65536u, 1u)]
    [InlineData(NativeGpuTextureDimension.ThreeD, 65536u, 1u, 1u)]
    [InlineData(NativeGpuTextureDimension.TwoD, 1u, 1u, 65536u)]
    public void CountsCannotBeTruncatedToNativeFields(NativeGpuTextureDimension dimension, uint depth, uint layers, uint mips)
    {
        NativeGpuTextureDescription description = Description with
        {
            Dimension = dimension, Depth = depth, LayerCount = layers, MipCount = mips,
        };

        Assert.Throws<OverflowException>(() => DirectX12Backend.TextureDescription(description));
    }

    [Fact]
    public void UnknownUsageBitsAreNotDiscarded()
    {
        ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            DirectX12Backend.TextureDescription(Description with { Usage = (NativeGpuTextureUsage)(1 << 20) }));

        Assert.Equal("description", error.ParamName);
    }

    private static NativeGpuTextureDescription Description => new(
        NativeGpuTextureDimension.TwoD, 16, 16, 1, 1, 1, 1, GpuFormat.Rgba8Unorm, NativeGpuTextureUsage.None);
}
