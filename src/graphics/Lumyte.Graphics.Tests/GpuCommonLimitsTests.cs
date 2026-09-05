using Lumyte.Graphics;

namespace Lumyte.Graphics.Tests;

public sealed class GpuCommonLimitsTests
{
    [Theory]
    [InlineData(4)]
    [InlineData(64)]
    public void RootDataAcceptsCommonSizes(int size) => GpuShaderBindingConvention.ValidateRootData(new byte[size]);

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(68)]
    [InlineData(128)]
    public void RootDataRejectsSizesOutsideTheCommonRange(int size)
    {
        var exception = Assert.Throws<ArgumentException>(() => GpuShaderBindingConvention.ValidateRootData(new byte[size]));
        Assert.Equal("data", exception.ParamName);
    }

    [Theory]
    [InlineData(17, 0, 0, 0, 0, "textureSlotCount")]
    [InlineData(0, 17, 0, 0, 0, "samplerSlotCount")]
    [InlineData(0, 0, 9, 0, 0, "bufferSlotCount")]
    [InlineData(0, 0, 0, 5, 0, "storageTextureSlotCount")]
    [InlineData(0, 0, 5, 0, 4, "writableBufferSlotCount")]
    public void DescriptorCountsShareOneCommonProfile(int textures, int samplers, int buffers, int storage, int writable, string parameter)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new GpuResourceTable(textures, samplers, buffers, storage, writable));
        Assert.Equal(parameter, exception.ParamName);
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(1, 4)]
    public void RasterPipelineRejectsUnsupportedAttachmentAndSampleCounts(int colors, uint samples)
    {
        var description = new GpuRasterPipelineDescription(Enumerable.Repeat(new GpuColorTargetDescription(GpuFormat.Rgba8Unorm), colors)) { SampleCount = samples };
        Assert.Throws<NotSupportedException>(() => description.Validate());
    }

    [Fact]
    public void SrgbStorageTextureIsRejectedBeforeBackendCreation()
    {
        var description = new GpuTextureDescription(4, 4, GpuFormat.Rgba8UnormSrgb, GpuTextureUsage.Storage);
        Assert.Throws<NotSupportedException>(() => description.Validate());
    }
}
