using P = Lumyte.Graphics.Portable;

namespace Lumyte.Graphics.WebGPU.Tests;

[Collection("GpuBackend")]
[Trait("Category", "WebGpuPortableConformance")]
public sealed class WebGpuPortableViewTests
{
    [Theory]
    [InlineData(P.GpuTextureDimension.Texture1D, P.GpuTextureViewDimension.Texture1D, 1u, 1u, 1u)]
    [InlineData(P.GpuTextureDimension.Texture2D, P.GpuTextureViewDimension.Texture2D, 8u, 1u, 1u)]
    [InlineData(P.GpuTextureDimension.Texture2D, P.GpuTextureViewDimension.Texture2DArray, 8u, 1u, 4u)]
    [InlineData(P.GpuTextureDimension.Texture2D, P.GpuTextureViewDimension.Cube, 8u, 1u, 6u)]
    [InlineData(P.GpuTextureDimension.Texture2D, P.GpuTextureViewDimension.CubeArray, 8u, 1u, 12u)]
    [InlineData(P.GpuTextureDimension.Texture3D, P.GpuTextureViewDimension.Texture3D, 8u, 4u, 1u)]
    public async Task TextureViewDimensionsBindTheirNativeResourceInterpretations(
        P.GpuTextureDimension dimension, P.GpuTextureViewDimension viewDimension, uint height, uint depth, uint layers)
    {
        var texture = new P.GpuTextureDescription(dimension,
            8, height, depth, 1, layers, 1, GpuFormat.Rgba8Unorm, P.GpuTextureUsage.Sampled);

        IReadOnlyList<P.GpuDiagnostic> diagnostics = await CreateSampledViewDiagnosticsAsync(
            texture, new(Dimension: viewDimension), new(P.GpuTextureSampleType.Float, viewDimension));

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task SampledArrayViewDoesNotInheritTheTexturesAttachmentUsage()
    {
        var texture = new P.GpuTextureDescription(P.GpuTextureDimension.Texture2D,
            8, 8, 1, 1, 4, 1, GpuFormat.Rgba8Unorm,
            P.GpuTextureUsage.Sampled | P.GpuTextureUsage.ColorAttachment);

        IReadOnlyList<P.GpuDiagnostic> diagnostics = await CreateSampledViewDiagnosticsAsync(texture,
            new(Dimension: P.GpuTextureViewDimension.Texture2DArray),
            new(P.GpuTextureSampleType.Float, P.GpuTextureViewDimension.Texture2DArray));

        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SrgbViewRequiresTheNativeMutableFormatPermission(bool mutableFormat)
    {
        var texture = new P.GpuTextureDescription(P.GpuTextureDimension.Texture2D,
            8, 8, 1, 1, 1, 1, GpuFormat.Rgba8Unorm, P.GpuTextureUsage.Sampled, mutableFormat);

        IReadOnlyList<P.GpuDiagnostic> diagnostics = await CreateSampledViewDiagnosticsAsync(texture,
            new(Format: GpuFormat.Rgba8UnormSrgb), new(P.GpuTextureSampleType.Float));

        if (mutableFormat) { Assert.Empty(diagnostics); }
        else { Assert.Contains(diagnostics, item => item.Kind == P.GpuDiagnosticKind.Validation); }
    }

    [Theory]
    [InlineData(GpuFormat.D32Float, P.GpuTextureAspect.DepthOnly, P.GpuTextureSampleType.Depth)]
    [InlineData(GpuFormat.Depth24PlusStencil8, P.GpuTextureAspect.DepthOnly, P.GpuTextureSampleType.Depth)]
    [InlineData(GpuFormat.Depth24PlusStencil8, P.GpuTextureAspect.StencilOnly, P.GpuTextureSampleType.Uint)]
    public async Task DepthAndStencilAspectDefaultsDoNotRequireMutableFormat(
        GpuFormat format, P.GpuTextureAspect aspect, P.GpuTextureSampleType sampleType)
    {
        var texture = new P.GpuTextureDescription(P.GpuTextureDimension.Texture2D,
            8, 8, 1, 1, 1, 1, format, P.GpuTextureUsage.Sampled);
        P.GpuTextureViewDescription view = new P.GpuTextureViewDescription(Aspect: aspect).Normalize(texture);

        IReadOnlyList<P.GpuDiagnostic> diagnostics = await CreateSampledViewDiagnosticsAsync(texture,
            view, new(sampleType));

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ExplicitInvalidMipRangeReachesNativeValidation()
    {
        var texture = new P.GpuTextureDescription(P.GpuTextureDimension.Texture2D,
            8, 8, 1, 1, 1, 1, GpuFormat.Rgba8Unorm, P.GpuTextureUsage.Sampled);

        IReadOnlyList<P.GpuDiagnostic> diagnostics = await CreateSampledViewDiagnosticsAsync(texture,
            new(BaseMip: 99, MipCount: 1), new(P.GpuTextureSampleType.Float));

        Assert.Contains(diagnostics, item => item.Kind == P.GpuDiagnosticKind.Validation);
    }

    [Theory]
    [InlineData(P.GpuSamplerBindingType.Filtering)]
    [InlineData(P.GpuSamplerBindingType.NonFiltering)]
    [InlineData(P.GpuSamplerBindingType.Comparison)]
    public async Task SamplerBindingTypesAcceptTheirCorrespondingDescriptions(P.GpuSamplerBindingType type)
    {
        P.GpuSamplerDescription sampler = type switch
        {
            P.GpuSamplerBindingType.Filtering => new(P.GpuSamplerFilter.Linear, P.GpuSamplerFilter.Linear,
                P.GpuSamplerFilter.Linear, P.GpuSamplerAddressMode.Repeat, P.GpuSamplerAddressMode.MirrorRepeat,
                P.GpuSamplerAddressMode.Repeat, 1, 8, 4),
            P.GpuSamplerBindingType.Comparison => new(Compare: GpuCompareOp.LessEqual),
            _ => new(),
        };

        IReadOnlyList<P.GpuDiagnostic> diagnostics = await CreateSamplerDiagnosticsAsync(type, sampler);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ZeroInitializedSamplerIsNotSilentlyRepaired()
    {
        IReadOnlyList<P.GpuDiagnostic> diagnostics = await CreateSamplerDiagnosticsAsync(
            P.GpuSamplerBindingType.Filtering, default);

        Assert.Contains(diagnostics, item => item.Kind == P.GpuDiagnosticKind.Validation);
    }

    [Fact]
    public async Task FilteringMismatchIsDiagnosedByTheNativeBindingRules()
    {
        IReadOnlyList<P.GpuDiagnostic> diagnostics = await CreateSamplerDiagnosticsAsync(
            P.GpuSamplerBindingType.NonFiltering, new(MinFilter: P.GpuSamplerFilter.Linear));

        Assert.Contains(diagnostics, item => item.Kind == P.GpuDiagnosticKind.Validation);
    }

    private static async Task<IReadOnlyList<P.GpuDiagnostic>> CreateSampledViewDiagnosticsAsync(
        P.GpuTextureDescription textureDescription, P.GpuTextureViewDescription viewDescription,
        P.GpuTextureBindingLayout textureLayout)
    {
        using WebGpuBackend backend = await WebGpuBackend.CreateAsync();
        P.GpuTextureHandle texture = backend.CreateTexture(textureDescription);
        P.GpuBindingLayoutHandle layout = backend.CreateBindingLayout([new(0, P.GpuShaderStage.Compute, textureLayout)]);
        try
        {
            P.GpuBindingsHandle bindings = backend.CreateBindings(layout,
                [P.GpuBindingEntry.Texture(0, new(texture, viewDescription))]);
            try { return await backend.GetCreationDiagnostics(bindings); }
            finally { backend.DestroyBindings(bindings); }
        }
        finally
        {
            backend.DestroyBindingLayout(layout);
            backend.DestroyTexture(texture);
        }
    }

    private static async Task<IReadOnlyList<P.GpuDiagnostic>> CreateSamplerDiagnosticsAsync(
        P.GpuSamplerBindingType type, P.GpuSamplerDescription sampler)
    {
        using WebGpuBackend backend = await WebGpuBackend.CreateAsync();
        P.GpuBindingLayoutHandle layout = backend.CreateBindingLayout([
            new(0, P.GpuShaderStage.Compute, new P.GpuSamplerBindingLayout(type)),
        ]);
        try
        {
            P.GpuBindingsHandle bindings = backend.CreateBindings(layout, [P.GpuBindingEntry.Sampler(0, sampler)]);
            try { return await backend.GetCreationDiagnostics(bindings); }
            finally { backend.DestroyBindings(bindings); }
        }
        finally { backend.DestroyBindingLayout(layout); }
    }
}
