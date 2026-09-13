using Lumyte.Graphics.Tests;

namespace Lumyte.Graphics.WebGPU.Tests;

[Collection("GpuBackend")]
[Trait("Category", "WebGpuPortableConformance")]
public sealed class WebGpuPortableResourceManagerTests
{
    [Fact]
    public async Task PackageExportPinKeepsItsTextureDependencyAliveForReadback()
    {
        using var fixture = await WebGpuPortableComputeFixture.CreateAsync();

        var result = await ResourceManagerGpuCases.PackageAsync(fixture.Backend);

        Assert.Equal(new byte[] { 2, 3, 5, 7, 11, 13, 17, 19 }, result.Data);
        Assert.Equal(new byte[] { 31, 63, 127, 255 }, result.Pixel);
        Assert.True(result.Released);
    }

    [Fact]
    public async Task ManagedBindingsKeepReleasedResourcesAliveThroughCompute()
    {
        using var fixture = await WebGpuPortableComputeFixture.CreateAsync();

        var result = await ResourceManagerGpuCases.ComputeAsync(fixture.Backend);

        Assert.True(result.Complete);
        Assert.Equal(new uint[] { 63, 127 }, result.Actual);
    }
}
