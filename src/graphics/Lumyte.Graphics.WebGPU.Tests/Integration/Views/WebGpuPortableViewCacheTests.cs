using P = Lumyte.Graphics.Portable;

namespace Lumyte.Graphics.WebGPU.Tests;

[Collection("GpuBackend")]
[Trait("Category", "WebGpuPortableConformance")]
public sealed class WebGpuPortableViewCacheTests
{
    [Fact]
    public async Task EquivalentViewAndSamplerValuesAreSharedUntilTheirFinalBindingIsDestroyed()
    {
        using WebGpuBackend backend = await WebGpuBackend.CreateAsync();
        P.GpuBindingLayoutHandle layout = CreateTextureSamplerLayout(backend);
        P.GpuTextureDescription description = TextureDescription();
        P.GpuTextureHandle texture = backend.CreateTexture(description);
        try
        {
            P.GpuBindingsHandle first = backend.CreateBindings(layout, Entries(texture, default, new()));
            P.GpuBindingsHandle second = backend.CreateBindings(layout,
                Entries(texture, new P.GpuTextureViewDescription().Normalize(description), new()));
            bool firstDestroyed = false;
            bool secondDestroyed = false;
            try
            {
                Assert.Empty(await backend.GetCreationDiagnostics(first));
                Assert.Empty(await backend.GetCreationDiagnostics(second));
                Assert.Equal((1, 1, 1L, 1L), backend.CacheStatistics);

                backend.DestroyBindings(first);
                firstDestroyed = true;
                Assert.Equal((1, 1, 1L, 1L), backend.CacheStatistics);
                Assert.Empty(await backend.GetCreationDiagnostics(second));

                backend.DestroyBindings(second);
                secondDestroyed = true;
                Assert.Equal((0, 0, 1L, 1L), backend.CacheStatistics);
            }
            finally
            {
                if (!secondDestroyed) { backend.DestroyBindings(second); }
                if (!firstDestroyed) { backend.DestroyBindings(first); }
            }
        }
        finally
        {
            backend.DestroyTexture(texture);
            backend.DestroyBindingLayout(layout);
        }
    }

    [Fact]
    public async Task CallerArrayMutationDoesNotReplaceBindingsOrTheirOwnedCacheLeases()
    {
        using WebGpuBackend backend = await WebGpuBackend.CreateAsync();
        P.GpuBindingLayoutHandle layout = CreateTextureSamplerLayout(backend);
        P.GpuTextureHandle firstTexture = backend.CreateTexture(TextureDescription());
        P.GpuTextureHandle replacementTexture = backend.CreateTexture(TextureDescription());
        try
        {
            P.GpuBindingEntry[] entries = Entries(firstTexture, default, new());
            P.GpuBindingsHandle bindings = backend.CreateBindings(layout, entries);
            try
            {
                entries[0] = P.GpuBindingEntry.Texture(0, new(replacementTexture, default));
                entries[1] = P.GpuBindingEntry.Sampler(1, default);

                Assert.Empty(await backend.GetCreationDiagnostics(bindings));
                Assert.Equal((1, 1, 1L, 1L), backend.CacheStatistics);
            }
            finally { backend.DestroyBindings(bindings); }

            Assert.Equal((0, 0, 1L, 1L), backend.CacheStatistics);
        }
        finally
        {
            backend.DestroyTexture(replacementTexture);
            backend.DestroyTexture(firstTexture);
            backend.DestroyBindingLayout(layout);
        }
    }

    [Fact]
    public async Task DifferentMipRangesAndSamplerAddressModesUseSeparateCacheEntries()
    {
        using WebGpuBackend backend = await WebGpuBackend.CreateAsync();
        P.GpuBindingLayoutHandle layout = CreateTextureSamplerLayout(backend);
        P.GpuTextureHandle texture = backend.CreateTexture(TextureDescription() with { MipCount = 2 });
        try
        {
            P.GpuBindingsHandle first = backend.CreateBindings(layout, Entries(texture, default, new()));
            P.GpuBindingsHandle second = backend.CreateBindings(layout,
                Entries(texture, new(BaseMip: 1, MipCount: 1), new(AddressW: P.GpuSamplerAddressMode.Repeat)));
            try
            {
                Assert.Empty(await backend.GetCreationDiagnostics(first));
                Assert.Empty(await backend.GetCreationDiagnostics(second));

                Assert.Equal((2, 2, 2L, 2L), backend.CacheStatistics);
            }
            finally
            {
                backend.DestroyBindings(second);
                backend.DestroyBindings(first);
            }

            Assert.Equal((0, 0, 2L, 2L), backend.CacheStatistics);
        }
        finally
        {
            backend.DestroyTexture(texture);
            backend.DestroyBindingLayout(layout);
        }
    }

    [Fact]
    public async Task FailureAfterAcquiringAViewRollsBackItsCacheLease()
    {
        using WebGpuBackend backend = await WebGpuBackend.CreateAsync();
        P.GpuBindingLayoutHandle layout = CreateTextureSamplerLayout(backend);
        P.GpuTextureHandle texture = backend.CreateTexture(TextureDescription());
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => backend.CreateBindings(layout,
                Entries(texture, default, new(MinFilter: (P.GpuSamplerFilter)99))));

            Assert.Equal((0, 0, 1L, 0L), backend.CacheStatistics);
            P.GpuBindingsHandle bindings = backend.CreateBindings(layout, Entries(texture, default, new()));
            try
            {
                Assert.Empty(await backend.GetCreationDiagnostics(bindings));
                Assert.Equal((1, 1, 2L, 1L), backend.CacheStatistics);
            }
            finally { backend.DestroyBindings(bindings); }
        }
        finally
        {
            backend.DestroyTexture(texture);
            backend.DestroyBindingLayout(layout);
        }
    }

    [Fact]
    public async Task SampledAndStorageBindingsKeepDistinctNativeUsageForTheSameViewValue()
    {
        using WebGpuBackend backend = await WebGpuBackend.CreateAsync();
        P.GpuBindingLayoutHandle sampledLayout = backend.CreateBindingLayout([
            new(0, P.GpuShaderStage.Compute, new P.GpuTextureBindingLayout(P.GpuTextureSampleType.Float)),
        ]);
        P.GpuBindingLayoutHandle storageLayout = backend.CreateBindingLayout([
            new(0, P.GpuShaderStage.Compute,
                new P.GpuStorageTextureBindingLayout(P.GpuStorageTextureAccess.WriteOnly, GpuFormat.Rgba8Unorm)),
        ]);
        P.GpuTextureHandle texture = backend.CreateTexture(TextureDescription() with
        {
            Usage = P.GpuTextureUsage.Sampled | P.GpuTextureUsage.Storage,
        });
        try
        {
            P.GpuBindingEntry[] entry = [P.GpuBindingEntry.Texture(0, new(texture, default))];
            P.GpuBindingsHandle sampled = backend.CreateBindings(sampledLayout, entry);
            P.GpuBindingsHandle storage = backend.CreateBindings(storageLayout, entry);
            try
            {
                Assert.Empty(await backend.GetCreationDiagnostics(sampled));
                Assert.Empty(await backend.GetCreationDiagnostics(storage));
                Assert.Equal((2, 0, 2L, 0L), backend.CacheStatistics);
            }
            finally
            {
                backend.DestroyBindings(storage);
                backend.DestroyBindings(sampled);
            }

            Assert.Equal((0, 0, 2L, 0L), backend.CacheStatistics);
        }
        finally
        {
            backend.DestroyTexture(texture);
            backend.DestroyBindingLayout(storageLayout);
            backend.DestroyBindingLayout(sampledLayout);
        }
    }

    private static P.GpuBindingLayoutHandle CreateTextureSamplerLayout(WebGpuBackend backend)
        => backend.CreateBindingLayout([
            new(0, P.GpuShaderStage.Compute, new P.GpuTextureBindingLayout(P.GpuTextureSampleType.Float)),
            new(1, P.GpuShaderStage.Compute, new P.GpuSamplerBindingLayout(P.GpuSamplerBindingType.Filtering)),
        ]);

    private static P.GpuTextureDescription TextureDescription()
        => new(P.GpuTextureDimension.Texture2D, 8, 8, 1, 1, 1, 1, GpuFormat.Rgba8Unorm, P.GpuTextureUsage.Sampled);

    private static P.GpuBindingEntry[] Entries(P.GpuTextureHandle texture,
        P.GpuTextureViewDescription view, P.GpuSamplerDescription sampler)
        => [P.GpuBindingEntry.Texture(0, new(texture, view)), P.GpuBindingEntry.Sampler(1, sampler)];
}
