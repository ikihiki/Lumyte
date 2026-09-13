using P = Lumyte.Graphics.Portable;

namespace Lumyte.Graphics.WebGPU.Tests;

[Collection("GpuBackend")]
[Trait("Category", "WebGpuPortableConformance")]
public sealed class WebGpuPortableBindingsTests
{
    [Fact]
    public async Task OneImmutableGroupBindsBufferSampledTextureStorageTextureAndSampler()
    {
        using WebGpuBackend backend = await WebGpuBackend.CreateAsync();
        P.GpuBindingLayoutHandle layout = backend.CreateBindingLayout([
            new(0, P.GpuShaderStage.Compute, new P.GpuBufferBindingLayout(P.GpuBufferBindingType.Uniform, 16)),
            new(2, P.GpuShaderStage.Compute, new P.GpuTextureBindingLayout(P.GpuTextureSampleType.Float)),
            new(4, P.GpuShaderStage.Compute, new P.GpuStorageTextureBindingLayout(P.GpuStorageTextureAccess.WriteOnly, GpuFormat.Rgba8Unorm)),
            new(7, P.GpuShaderStage.Compute, new P.GpuSamplerBindingLayout(P.GpuSamplerBindingType.Filtering)),
        ]);
        P.GpuBufferHandle buffer = backend.CreateBuffer(new(32, P.GpuBufferUsage.Uniform));
        var description = new P.GpuTextureDescription(P.GpuTextureDimension.Texture2D,
            4, 4, 1, 1, 1, 1, GpuFormat.Rgba8Unorm, P.GpuTextureUsage.Sampled);
        P.GpuTextureHandle sampled = backend.CreateTexture(description);
        P.GpuTextureHandle storage = backend.CreateTexture(description with { Usage = P.GpuTextureUsage.Storage });
        try
        {
            P.GpuBindingsHandle bindings = backend.CreateBindings(layout, [
                P.GpuBindingEntry.Buffer(0, new(buffer, 0, null)),
                P.GpuBindingEntry.Texture(2, new(sampled, default)),
                P.GpuBindingEntry.Texture(4, new(storage, default)),
                P.GpuBindingEntry.Sampler(7, new()),
            ]);

            try { Assert.Empty(await backend.GetCreationDiagnostics(bindings)); }
            finally { backend.DestroyBindings(bindings); }
        }
        finally
        {
            backend.DestroyTexture(storage);
            backend.DestroyTexture(sampled);
            backend.DestroyBuffer(buffer);
            backend.DestroyBindingLayout(layout);
        }
    }

    [Fact]
    public async Task DestroyingBindingsLeavesTheirResourcesAndLayoutAvailable()
    {
        using WebGpuBackend backend = await WebGpuBackend.CreateAsync();
        P.GpuBindingLayoutHandle layout = backend.CreateBindingLayout([
            new(0, P.GpuShaderStage.Compute, new P.GpuTextureBindingLayout(P.GpuTextureSampleType.Float)),
        ]);
        P.GpuTextureHandle texture = backend.CreateTexture(new(P.GpuTextureDimension.Texture2D,
            4, 4, 1, 1, 1, 1, GpuFormat.Rgba8Unorm, P.GpuTextureUsage.Sampled));
        try
        {
            P.GpuBindingEntry[] entries = [P.GpuBindingEntry.Texture(0, new(texture, default))];
            P.GpuBindingsHandle first = backend.CreateBindings(layout, entries);
            Assert.Empty(await backend.GetCreationDiagnostics(first));
            backend.DestroyBindings(first);

            P.GpuBindingsHandle second = backend.CreateBindings(layout, entries);

            try { Assert.Empty(await backend.GetCreationDiagnostics(second)); }
            finally { backend.DestroyBindings(second); }
        }
        finally
        {
            backend.DestroyTexture(texture);
            backend.DestroyBindingLayout(layout);
        }
    }

    [Fact]
    public async Task InvalidResourceDiagnosticsFollowOnlyBindingsThatReferenceTheResource()
    {
        using WebGpuBackend backend = await WebGpuBackend.CreateAsync();
        P.GpuBindingLayoutHandle layout = backend.CreateBindingLayout([
            new(0, P.GpuShaderStage.Compute, new P.GpuBufferBindingLayout(P.GpuBufferBindingType.Storage)),
        ]);
        P.GpuBufferHandle invalid = backend.CreateBuffer(new(32, P.GpuBufferUsage.Storage | P.GpuBufferUsage.MapRead));
        P.GpuBufferHandle valid = backend.CreateBuffer(new(32, P.GpuBufferUsage.Storage));
        try
        {
            P.GpuBindingsHandle badBindings = backend.CreateBindings(layout, [P.GpuBindingEntry.Buffer(0, new(invalid, 0, 32))]);
            P.GpuBindingsHandle goodBindings = backend.CreateBindings(layout, [P.GpuBindingEntry.Buffer(0, new(valid, 0, 32))]);
            try
            {
                IReadOnlyList<P.GpuDiagnostic> resourceDiagnostics = await backend.GetCreationDiagnostics(invalid);
                IReadOnlyList<P.GpuDiagnostic> bindingDiagnostics = await backend.GetCreationDiagnostics(badBindings);

                Assert.NotEmpty(resourceDiagnostics);
                Assert.All(resourceDiagnostics, item => Assert.Contains(item, bindingDiagnostics));
                Assert.Empty(await backend.GetCreationDiagnostics(goodBindings));
            }
            finally
            {
                backend.DestroyBindings(goodBindings);
                backend.DestroyBindings(badBindings);
            }
        }
        finally
        {
            backend.DestroyBuffer(valid);
            backend.DestroyBuffer(invalid);
            backend.DestroyBindingLayout(layout);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task NativeBindingValidationReportsMissingDuplicateEmptyAndMisalignedEntries(int invalidCase)
    {
        using WebGpuBackend backend = await WebGpuBackend.CreateAsync();
        P.GpuBindingLayoutHandle layout = backend.CreateBindingLayout([
            new(0, P.GpuShaderStage.Compute, new P.GpuBufferBindingLayout(P.GpuBufferBindingType.Uniform, 16)),
        ]);
        P.GpuBufferHandle buffer = backend.CreateBuffer(new(512, P.GpuBufferUsage.Uniform));
        try
        {
            P.GpuBindingEntry valid = P.GpuBindingEntry.Buffer(0, new(buffer, 0, 16));
            P.GpuBindingEntry[] entries = invalidCase switch
            {
                0 => [],
                1 => [valid, valid],
                2 => [default],
                _ => [P.GpuBindingEntry.Buffer(0, new(buffer, 4, 16))],
            };
            P.GpuBindingsHandle bindings = backend.CreateBindings(layout, entries);

            try
            {
                IReadOnlyList<P.GpuDiagnostic> diagnostics = await backend.GetCreationDiagnostics(bindings);
                Assert.Contains(diagnostics, item => item.Kind == P.GpuDiagnosticKind.Validation);
            }
            finally { backend.DestroyBindings(bindings); }
        }
        finally
        {
            backend.DestroyBuffer(buffer);
            backend.DestroyBindingLayout(layout);
        }
    }

    [Fact]
    public async Task ForeignResourcesCannotBeInsertedIntoLocalBindings()
    {
        using WebGpuBackend owner = await WebGpuBackend.CreateAsync();
        using WebGpuBackend other = await WebGpuBackend.CreateAsync();
        P.GpuBindingLayoutHandle layout = owner.CreateBindingLayout([
            new(0, P.GpuShaderStage.Compute, new P.GpuBufferBindingLayout(P.GpuBufferBindingType.Storage)),
        ]);
        P.GpuBufferHandle foreign = other.CreateBuffer(new(32, P.GpuBufferUsage.Storage));
        try
        {
            Assert.Throws<ArgumentException>(() => owner.CreateBindings(layout,
                [P.GpuBindingEntry.Buffer(0, new(foreign, 0, 32))]));
        }
        finally
        {
            other.DestroyBuffer(foreign);
            owner.DestroyBindingLayout(layout);
        }
    }

    [Fact]
    public async Task DestroyedResourcesCannotBeInsertedIntoBindings()
    {
        using WebGpuBackend backend = await WebGpuBackend.CreateAsync();
        P.GpuBindingLayoutHandle layout = backend.CreateBindingLayout([
            new(0, P.GpuShaderStage.Compute, new P.GpuBufferBindingLayout(P.GpuBufferBindingType.Storage)),
        ]);
        P.GpuBufferHandle buffer = backend.CreateBuffer(new(32, P.GpuBufferUsage.Storage));
        backend.DestroyBuffer(buffer);
        try
        {
            Assert.Throws<ObjectDisposedException>(() => backend.CreateBindings(layout,
                [P.GpuBindingEntry.Buffer(0, new(buffer, 0, 32))]));
        }
        finally { backend.DestroyBindingLayout(layout); }
    }

    [Fact]
    public async Task ForeignDeviceCannotDestroyBindingsAndOwnerStillCan()
    {
        using WebGpuBackend owner = await WebGpuBackend.CreateAsync();
        using WebGpuBackend other = await WebGpuBackend.CreateAsync();
        P.GpuBindingLayoutHandle layout = owner.CreateBindingLayout([]);
        P.GpuBindingsHandle bindings = owner.CreateBindings(layout, []);
        try
        {
            Assert.Throws<ArgumentException>(() => other.DestroyBindings(bindings));
            Assert.Empty(await owner.GetCreationDiagnostics(bindings));
        }
        finally
        {
            owner.DestroyBindings(bindings);
            owner.DestroyBindingLayout(layout);
        }
    }

    [Fact]
    public async Task BindingsHaveOneOwningDestruction()
    {
        using WebGpuBackend backend = await WebGpuBackend.CreateAsync();
        P.GpuBindingLayoutHandle layout = backend.CreateBindingLayout([]);
        P.GpuBindingsHandle bindings = backend.CreateBindings(layout, []);
        backend.DestroyBindings(bindings);
        try
        {
            Assert.Throws<ObjectDisposedException>(() => backend.DestroyBindings(bindings));
        }
        finally { backend.DestroyBindingLayout(layout); }
    }

    [Theory]
    [InlineData("Length")]
    [InlineData("MipCount")]
    [InlineData("LayerCount")]
    [InlineData("MaxAnisotropy")]
    public async Task ExplicitValuesThatCannotSurviveNativeEncodingAreRejected(string parameter)
    {
        using WebGpuBackend backend = await WebGpuBackend.CreateAsync();
        P.GpuBindingLayoutHandle layout = backend.CreateBindingLayout([
            new(0, P.GpuShaderStage.Compute, new P.GpuBufferBindingLayout(P.GpuBufferBindingType.Uniform)),
            new(1, P.GpuShaderStage.Compute, new P.GpuTextureBindingLayout(P.GpuTextureSampleType.Float)),
            new(2, P.GpuShaderStage.Compute, new P.GpuSamplerBindingLayout(P.GpuSamplerBindingType.Filtering)),
        ]);
        P.GpuBufferHandle buffer = backend.CreateBuffer(new(32, P.GpuBufferUsage.Uniform));
        P.GpuTextureHandle texture = backend.CreateTexture(new(P.GpuTextureDimension.Texture2D,
            4, 4, 1, 1, 1, 1, GpuFormat.Rgba8Unorm, P.GpuTextureUsage.Sampled));
        try
        {
            var range = new P.GpuBufferRange(buffer, 0, parameter == "Length" ? ulong.MaxValue : 32);
            var view = new P.GpuTextureViewDescription(
                MipCount: parameter == "MipCount" ? uint.MaxValue : null,
                LayerCount: parameter == "LayerCount" ? uint.MaxValue : null);
            var sampler = new P.GpuSamplerDescription(MaxAnisotropy:
                parameter == "MaxAnisotropy" ? (uint)ushort.MaxValue + 1 : 1);

            ArgumentOutOfRangeException failure = Assert.Throws<ArgumentOutOfRangeException>(() =>
                backend.CreateBindings(layout, [P.GpuBindingEntry.Buffer(0, range),
                    P.GpuBindingEntry.Texture(1, new(texture, view)), P.GpuBindingEntry.Sampler(2, sampler)]));

            Assert.Equal(parameter, failure.ParamName);
            Assert.Equal((0, 0), (backend.CacheStatistics.ViewEntries, backend.CacheStatistics.SamplerEntries));
        }
        finally
        {
            backend.DestroyTexture(texture);
            backend.DestroyBuffer(buffer);
            backend.DestroyBindingLayout(layout);
        }
    }
}
