namespace Lumyte.Graphics.WebGPU.Tests;

[Collection("WebGPU")]
public sealed class WebGpuDeviceTests
{
    [Fact]
    [Trait("Category", "WebGpuConformance")]
    public void BackendCanBeCreated()
    {
        using IGpuBackend backend = WebGpuBackend.Create();

        Assert.NotNull(backend.MainQueue);
    }

    [Fact]
    [Trait("Category", "WebGpuConformance")]
    public void BackendExposesImplementedCapabilities()
    {
        using IGpuBackend backend = WebGpuBackend.Create();

        Assert.Equal(
            GpuBackendCapabilities.DeviceOwnedResources | GpuBackendCapabilities.RasterPipeline,
            backend.Capabilities);
    }

    [Fact]
    [Trait("Category", "WebGpuConformance")]
    public void DeviceIssuesDistinctLogicalSamplerIds()
    {
        using IGpuBackend backend = WebGpuBackend.Create();

        SamplerId first = backend.CreateSampler(default);
        SamplerId second = backend.CreateSampler(new(GpuSamplerFilter.Linear, GpuSamplerFilter.Linear));

        Assert.False(first.IsNull);
        Assert.False(second.IsNull);
        Assert.NotEqual(first, second);

        backend.DestroySampler(second);
        backend.DestroySampler(first);
    }

    [Fact]
    [Trait("Category", "WebGpuConformance")]
    public void DeviceIssuesDistinctLogicalTextureIds()
    {
        using IGpuBackend backend = WebGpuBackend.Create();
        GpuTextureHandle texture = backend.CreateTexture(
            new(2, 2, GpuFormat.Rgba8Unorm, GpuTextureUsage.Sampled));

        GpuTextureView first = backend.CreateTextureView(texture, new(GpuFormat.Rgba8Unorm));
        GpuTextureView second = backend.CreateTextureView(texture, new(GpuFormat.Rgba8Unorm));

        Assert.False(first.Id.IsNull);
        Assert.False(second.Id.IsNull);
        Assert.NotEqual(first.Id, second.Id);

        backend.DestroyTextureView(second);
        backend.DestroyTextureView(first);
        backend.DestroyTexture(texture);
    }

    [Fact]
    [Trait("Category", "WebGpuConformance")]
    public void TextureRoundTripPreservesPixels()
    {
        using IGpuBackend backend = WebGpuBackend.Create();
        byte[] expected =
        [
            255, 0, 0, 255, 0, 255, 0, 255,
            0, 0, 255, 255, 255, 255, 255, 255,
        ];

        GpuTextureHandle texture = backend.CreateTexture(new(
            2,
            2,
            GpuFormat.Rgba8Unorm,
            GpuTextureUsage.CopyDestination | GpuTextureUsage.CopySource));

        backend.WriteTexture(texture, expected, new(2, 2, 4, 8));
        byte[] actual = backend.ReadTexture(texture, new(2, 2, 4, 8));

        Assert.Equal(expected, actual);

        backend.DestroyTexture(texture);
    }
}
