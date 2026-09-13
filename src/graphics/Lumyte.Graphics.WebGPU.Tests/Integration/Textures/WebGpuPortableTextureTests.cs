using P = Lumyte.Graphics.Portable;

namespace Lumyte.Graphics.WebGPU.Tests;

[Collection("GpuBackend")]
[Trait("Category", "WebGpuPortableConformance")]
public sealed class WebGpuPortableTextureTests
{
    [Theory]
    [InlineData(P.GpuTextureDimension.Texture1D, 8u, 1u, 1u, 1u, false)]
    [InlineData(P.GpuTextureDimension.Texture2D, 8u, 4u, 1u, 3u, false)]
    [InlineData(P.GpuTextureDimension.Texture3D, 8u, 4u, 2u, 1u, false)]
    [InlineData(P.GpuTextureDimension.Texture2D, 8u, 4u, 1u, 1u, true)]
    public async Task TextureDimensionsAndMutableFormatReachTheRuntime(
        P.GpuTextureDimension dimension, uint width, uint height, uint depth, uint layers, bool mutableFormat)
    {
        using WebGpuBackend backend = await WebGpuBackend.CreateAsync();
        P.GpuTextureHandle texture = backend.CreateTexture(new(dimension,
            width, height, depth, 1, layers, 1, GpuFormat.Rgba8Unorm, P.GpuTextureUsage.Sampled, mutableFormat));
        try
        {
            IReadOnlyList<P.GpuDiagnostic> diagnostics = await backend.GetCreationDiagnostics(texture);

            Assert.Empty(diagnostics);
        }
        finally { backend.DestroyTexture(texture); }
    }

    [Fact]
    public async Task InvalidTextureKeepsItsRuntimeDiagnosticSeparateFromValidTexture()
    {
        using WebGpuBackend backend = await WebGpuBackend.CreateAsync();
        var description = new P.GpuTextureDescription(P.GpuTextureDimension.Texture2D,
            0, 4, 1, 1, 1, 1, GpuFormat.Rgba8Unorm, P.GpuTextureUsage.Sampled);
        P.GpuTextureHandle invalid = backend.CreateTexture(description);
        P.GpuTextureHandle valid = backend.CreateTexture(description with { Width = 4 });
        try
        {
            IReadOnlyList<P.GpuDiagnostic> invalidDiagnostics = await backend.GetCreationDiagnostics(invalid);
            IReadOnlyList<P.GpuDiagnostic> validDiagnostics = await backend.GetCreationDiagnostics(valid);

            Assert.Contains(invalidDiagnostics, item => item.Kind == P.GpuDiagnosticKind.Validation && !string.IsNullOrWhiteSpace(item.Message));
            Assert.Empty(validDiagnostics);
        }
        finally
        {
            backend.DestroyTexture(valid);
            backend.DestroyTexture(invalid);
        }
    }

    [Fact]
    public async Task ForeignDeviceCannotDestroyTheOwningDevicesTexture()
    {
        using WebGpuBackend owner = await WebGpuBackend.CreateAsync();
        using WebGpuBackend other = await WebGpuBackend.CreateAsync();
        P.GpuTextureHandle texture = owner.CreateTexture(new(P.GpuTextureDimension.Texture2D,
            4, 4, 1, 1, 1, 1, GpuFormat.Rgba8Unorm, P.GpuTextureUsage.Sampled));
        try
        {
            Assert.Throws<ArgumentException>(() => other.DestroyTexture(texture));
            Assert.Empty(await owner.GetCreationDiagnostics(texture));
        }
        finally { owner.DestroyTexture(texture); }
    }

    [Fact]
    public async Task TextureDestructionRejectsAnExternalBackendHandle()
    {
        using WebGpuBackend backend = await WebGpuBackend.CreateAsync();

        Assert.Throws<ArgumentException>(() => backend.DestroyTexture(new OtherTexture()));
    }

    private sealed class OtherTexture : P.GpuTextureHandle;
}
