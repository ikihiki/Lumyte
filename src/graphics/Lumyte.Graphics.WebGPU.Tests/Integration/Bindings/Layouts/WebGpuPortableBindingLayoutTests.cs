using P = Lumyte.Graphics.Portable;

namespace Lumyte.Graphics.WebGPU.Tests;

[Collection("GpuBackend")]
[Trait("Category", "WebGpuPortableConformance")]
public sealed class WebGpuPortableBindingLayoutTests
{
    [Fact]
    public async Task AGroupDeclaresAllFourResourceLayoutKinds()
    {
        using WebGpuBackend backend = await WebGpuBackend.CreateAsync();
        P.GpuBindingLayoutHandle layout = backend.CreateBindingLayout([
            new(0, P.GpuShaderStage.Vertex | P.GpuShaderStage.Pixel, new P.GpuBufferBindingLayout(P.GpuBufferBindingType.Uniform, 16)),
            new(3, P.GpuShaderStage.Pixel, new P.GpuTextureBindingLayout(P.GpuTextureSampleType.Float)),
            new(5, P.GpuShaderStage.Compute, new P.GpuStorageTextureBindingLayout(P.GpuStorageTextureAccess.WriteOnly, GpuFormat.Rgba8Unorm)),
            new(9, P.GpuShaderStage.Pixel, new P.GpuSamplerBindingLayout(P.GpuSamplerBindingType.Filtering)),
        ]);
        try
        {
            Assert.Empty(await backend.GetCreationDiagnostics(layout));
        }
        finally { backend.DestroyBindingLayout(layout); }
    }

    [Theory]
    [InlineData(P.GpuBufferBindingType.Uniform)]
    [InlineData(P.GpuBufferBindingType.ReadOnlyStorage)]
    [InlineData(P.GpuBufferBindingType.Storage)]
    public async Task BufferLayoutTypesAcceptTheirExplicitResourceRanges(P.GpuBufferBindingType type)
    {
        using WebGpuBackend backend = await WebGpuBackend.CreateAsync();
        P.GpuBindingLayoutHandle layout = backend.CreateBindingLayout([
            new(2, P.GpuShaderStage.Compute, new P.GpuBufferBindingLayout(type, 16, HasDynamicOffset: true)),
        ]);
        P.GpuBufferUsage usage = type == P.GpuBufferBindingType.Uniform ? P.GpuBufferUsage.Uniform : P.GpuBufferUsage.Storage;
        P.GpuBufferHandle buffer = backend.CreateBuffer(new(512, usage));
        try
        {
            P.GpuBindingsHandle bindings = backend.CreateBindings(layout, [P.GpuBindingEntry.Buffer(2, new(buffer, 256, 16))]);
            try { Assert.Empty(await backend.GetCreationDiagnostics(bindings)); }
            finally { backend.DestroyBindings(bindings); }
        }
        finally
        {
            backend.DestroyBuffer(buffer);
            backend.DestroyBindingLayout(layout);
        }
    }

    [Fact]
    public async Task ChangingTheCallerLayoutArrayDoesNotChangeItsResourceInterpretation()
    {
        using WebGpuBackend backend = await WebGpuBackend.CreateAsync();
        P.GpuBindingLayoutEntry[] entries = [
            new(3, P.GpuShaderStage.Compute, new P.GpuTextureBindingLayout(P.GpuTextureSampleType.Float)),
        ];
        P.GpuBindingLayoutHandle layout = backend.CreateBindingLayout(entries);
        P.GpuTextureHandle texture = backend.CreateTexture(new(P.GpuTextureDimension.Texture2D,
            4, 4, 1, 1, 1, 1, GpuFormat.Rgba8Unorm, P.GpuTextureUsage.Sampled));
        try
        {
            entries[0] = new(3, P.GpuShaderStage.Compute,
                new P.GpuStorageTextureBindingLayout(P.GpuStorageTextureAccess.WriteOnly, GpuFormat.Rgba8Unorm));
            P.GpuBindingsHandle bindings = backend.CreateBindings(layout, [P.GpuBindingEntry.Texture(3, new(texture, default))]);

            try { Assert.Empty(await backend.GetCreationDiagnostics(bindings)); }
            finally { backend.DestroyBindings(bindings); }
        }
        finally
        {
            backend.DestroyTexture(texture);
            backend.DestroyBindingLayout(layout);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidLayoutShapeIsReportedByTheRuntime(bool duplicateBinding)
    {
        using WebGpuBackend backend = await WebGpuBackend.CreateAsync();
        P.GpuBindingLayoutEntry buffer = new(0, P.GpuShaderStage.Compute,
            new P.GpuBufferBindingLayout(P.GpuBufferBindingType.Storage));
        P.GpuBindingLayoutHandle layout = backend.CreateBindingLayout(duplicateBinding ? [buffer, buffer] : [default]);
        try
        {
            IReadOnlyList<P.GpuDiagnostic> diagnostics = await backend.GetCreationDiagnostics(layout);

            Assert.Contains(diagnostics, item => item.Kind == P.GpuDiagnosticKind.Validation);
        }
        finally { backend.DestroyBindingLayout(layout); }
    }

    [Fact]
    public async Task InvalidLayoutDiagnosticsRemainDependenciesOfItsBindings()
    {
        using WebGpuBackend backend = await WebGpuBackend.CreateAsync();
        P.GpuBindingLayoutEntry entry = new(0, P.GpuShaderStage.Compute,
            new P.GpuBufferBindingLayout(P.GpuBufferBindingType.Storage));
        P.GpuBindingLayoutHandle layout = backend.CreateBindingLayout([entry, entry]);
        P.GpuBufferHandle buffer = backend.CreateBuffer(new(32, P.GpuBufferUsage.Storage));
        try
        {
            P.GpuBindingsHandle bindings = backend.CreateBindings(layout, [P.GpuBindingEntry.Buffer(0, new(buffer, 0, 32))]);
            try
            {
                IReadOnlyList<P.GpuDiagnostic> layoutDiagnostics = await backend.GetCreationDiagnostics(layout);
                IReadOnlyList<P.GpuDiagnostic> bindingDiagnostics = await backend.GetCreationDiagnostics(bindings);

                Assert.NotEmpty(layoutDiagnostics);
                Assert.All(layoutDiagnostics, item => Assert.Contains(item, bindingDiagnostics));
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
    public async Task AForeignDeviceCannotUseOrDestroyALayout()
    {
        using WebGpuBackend owner = await WebGpuBackend.CreateAsync();
        using WebGpuBackend other = await WebGpuBackend.CreateAsync();
        P.GpuBindingLayoutHandle layout = owner.CreateBindingLayout([]);
        try
        {
            Assert.Throws<ArgumentException>(() => other.CreateBindings(layout, []));
            Assert.Throws<ArgumentException>(() => other.DestroyBindingLayout(layout));
            Assert.Empty(await owner.GetCreationDiagnostics(layout));
        }
        finally { owner.DestroyBindingLayout(layout); }
    }

    [Fact]
    public async Task DestroyedLayoutCannotCreateAnotherBindingSet()
    {
        using WebGpuBackend backend = await WebGpuBackend.CreateAsync();
        P.GpuBindingLayoutHandle layout = backend.CreateBindingLayout([]);
        backend.DestroyBindingLayout(layout);

        Assert.Throws<ObjectDisposedException>(() => backend.CreateBindings(layout, []));
    }
}
