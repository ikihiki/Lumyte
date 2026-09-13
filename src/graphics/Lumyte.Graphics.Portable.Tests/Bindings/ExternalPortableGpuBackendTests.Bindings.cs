namespace Lumyte.Graphics.Portable.Tests.Device;

public sealed partial class ExternalPortableGpuBackendTests
{
    [Theory]
    [InlineData(GpuBindingLayoutKind.Buffer)]
    [InlineData(GpuBindingLayoutKind.Texture)]
    [InlineData(GpuBindingLayoutKind.StorageTexture)]
    [InlineData(GpuBindingLayoutKind.Sampler)]
    public void ConsumerPassesEachLayoutKindToAnExternalBackend(GpuBindingLayoutKind kind)
    {
        const uint binding = 7;
        const GpuShaderStage stages = GpuShaderStage.Pixel | GpuShaderStage.Compute;
        object expected = kind switch
        {
            GpuBindingLayoutKind.Buffer => new GpuBufferBindingLayout(GpuBufferBindingType.ReadOnlyStorage, (1ul << 33) + 16, true),
            GpuBindingLayoutKind.Texture => new GpuTextureBindingLayout(GpuTextureSampleType.UnfilterableFloat, GpuTextureViewDimension.Texture2DArray),
            GpuBindingLayoutKind.StorageTexture => new GpuStorageTextureBindingLayout(GpuStorageTextureAccess.ReadWrite, GpuFormat.R32Float, GpuTextureViewDimension.Texture3D),
            GpuBindingLayoutKind.Sampler => new GpuSamplerBindingLayout(GpuSamplerBindingType.Comparison),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        GpuBindingLayoutEntry entry = expected switch
        {
            GpuBufferBindingLayout value => new(binding, stages, value),
            GpuTextureBindingLayout value => new(binding, stages, value),
            GpuStorageTextureBindingLayout value => new(binding, stages, value),
            GpuSamplerBindingLayout value => new(binding, stages, value),
            _ => throw new InvalidOperationException(),
        };
        List<object> observed = [];
        using IPortableGpuBackend backend = new ExternalBackend(observed.Add);

        GpuBindingLayoutHandle layout = backend.CreateBindingLayout([entry]);
        backend.DestroyBindingLayout(layout);

        Assert.Collection(observed,
            value =>
            {
                var declaration = Assert.Single(Assert.IsType<LayoutCreation>(value).Entries);
                Assert.Equal((binding, stages, kind, expected),
                    (declaration.Binding, declaration.Visibility, declaration.Kind, ActiveLayout(declaration)));
            },
            value => Assert.Same(layout, value));
    }

    [Fact]
    public void ConsumerBuildsAGroupFromNonOwningResourceValues()
    {
        List<object> observed = [];
        using IPortableGpuBackend backend = new ExternalBackend(observed.Add);
        var buffer = backend.CreateBuffer(new((1ul << 34) + 256, GpuBufferUsage.Storage));
        var texture = backend.CreateTexture(new(GpuTextureDimension.Texture2D, 64, 32, 1, 3, 6, 1,
            GpuFormat.Rgba8Unorm, GpuTextureUsage.Sampled));
        var layout = backend.CreateBindingLayout([
            new(0, GpuShaderStage.Compute, new GpuBufferBindingLayout(GpuBufferBindingType.ReadOnlyStorage)),
            new(1, GpuShaderStage.Compute, new GpuTextureBindingLayout(GpuTextureSampleType.Float, GpuTextureViewDimension.Texture2DArray)),
            new(2, GpuShaderStage.Compute, new GpuSamplerBindingLayout(GpuSamplerBindingType.Filtering)),
        ]);
        var range = new GpuBufferRange(buffer, (1ul << 33) + 64, 128);
        var view = new GpuTextureView(texture, new(Dimension: GpuTextureViewDimension.Texture2DArray, BaseLayer: 2, LayerCount: 3));
        var sampler = new GpuSamplerDescription(MinFilter: GpuSamplerFilter.Linear, MaxLod: 7);
        observed.Clear();

        var bindings = backend.CreateBindings(layout, [
            GpuBindingEntry.Buffer(0, range), GpuBindingEntry.Texture(1, view), GpuBindingEntry.Sampler(2, sampler),
        ]);
        backend.DestroyBindings(bindings);
        backend.DestroyBindingLayout(layout);
        backend.DestroyTexture(texture);
        backend.DestroyBuffer(buffer);

        Assert.Collection(observed,
            value =>
            {
                var creation = Assert.IsType<BindingsCreation>(value);
                Assert.Same(layout, creation.Layout);
                Assert.Collection(creation.Entries,
                    entry => Assert.Equal((0u, GpuBindingResourceKind.Buffer, range), (entry.Binding, entry.Kind, entry.BufferRange)),
                    entry => Assert.Equal((1u, GpuBindingResourceKind.Texture, view), (entry.Binding, entry.Kind, entry.TextureView)),
                    entry => Assert.Equal((2u, GpuBindingResourceKind.Sampler, sampler), (entry.Binding, entry.Kind, entry.SamplerDescription)));
            },
            value => Assert.Same(bindings, value),
            value => Assert.Same(layout, value),
            value => Assert.Same(texture, value),
            value => Assert.Same(buffer, value));
    }

    private static object ActiveLayout(GpuBindingLayoutEntry entry) => entry.Kind switch
    {
        GpuBindingLayoutKind.Buffer => entry.BufferLayout,
        GpuBindingLayoutKind.Texture => entry.TextureLayout,
        GpuBindingLayoutKind.StorageTexture => entry.StorageTextureLayout,
        GpuBindingLayoutKind.Sampler => entry.SamplerLayout,
        _ => throw new InvalidOperationException("The external backend received no selected layout."),
    };

    private sealed record LayoutCreation(GpuBindingLayoutEntry[] Entries);
    private sealed record BindingsCreation(GpuBindingLayoutHandle Layout, GpuBindingEntry[] Entries);

    private sealed partial class ExternalBackend
    {
        public GpuBindingLayoutHandle CreateBindingLayout(ReadOnlySpan<GpuBindingLayoutEntry> entries)
        { observe(new LayoutCreation(entries.ToArray())); return new Layout(); }
        public void DestroyBindingLayout(GpuBindingLayoutHandle layout) => observe(layout);
        public GpuBindingsHandle CreateBindings(GpuBindingLayoutHandle layout, ReadOnlySpan<GpuBindingEntry> entries)
        { observe(new BindingsCreation(layout, entries.ToArray())); return new Bindings(); }
        public void DestroyBindings(GpuBindingsHandle bindings) => observe(bindings);

        private sealed class Layout : GpuBindingLayoutHandle;
        private sealed class Bindings : GpuBindingsHandle;
    }
}
