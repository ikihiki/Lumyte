namespace Lumyte.Resources.Tests;

public sealed class ResourceStoreTests
{
    [Fact]
    public async Task DispatchesCustomSchemeToRegisteredResolver()
    {
        RecordingResolver resolver = new();
        ResourceStore store = new(resolver);
        AssetKey<TestResource> key = Asset.From<TestResource>("memory:documents/readme");

        AssetLocation location = await store.ResolveAsync(key);

        Assert.Equal(new AssetLocation("memory", "documents/readme"), location);
        Assert.Equal("documents/readme", resolver.LastAddress);
    }

    [Fact]
    public async Task InternsRepeatedKeysOnce()
    {
        RecordingResolver resolver = new();
        ResourceStore store = new(resolver);
        AssetKey<TestResource> key = Asset.From<TestResource>("memory:documents/readme");

        await store.ResolveAsync(key);
        await store.ResolveAsync(key);

        Assert.Equal(1, store.InternedKeyCount);
    }

    [Fact]
    public async Task RejectsUnregisteredScheme()
    {
        ResourceStore store = new();
        AssetKey<TestResource> key = Asset.From<TestResource>("unknown:item");

        AssetResolutionException exception = await Assert.ThrowsAsync<AssetResolutionException>(
            async () => await store.ResolveAsync(key));

        Assert.Contains("unknown", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FileResolverKeepsLocationBelowConfiguredRoot()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "LumyteResourcesTests"));
        ResourceStore store = new(new FileAssetResolver(root));
        AssetKey<TestResource> key = Asset.File<TestResource>("models/robot.glb");

        AssetLocation location = await store.ResolveAsync(key);

        Assert.Equal(
            new AssetLocation("file", Path.Combine(root, "models", "robot.glb")),
            location);
    }

    [Fact]
    public async Task FileResolverRejectsEncodedTraversal()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "LumyteResourcesTests"));
        ResourceStore store = new(new FileAssetResolver(root));
        AssetKey<TestResource> key = AssetKey<TestResource>.Parse("file:%2E%2E/secret.bin");

        await Assert.ThrowsAsync<AssetResolutionException>(
            async () => await store.ResolveAsync(key));
    }

    [Fact]
    public async Task CatalogResolvesStableIdentifier()
    {
        AssetLocation expected = new("package", "content/42");
        CatalogAssetResolver resolver = new(
            new Dictionary<string, AssetLocation>
            {
                ["character.robot"] = expected
            });
        ResourceStore store = new(resolver);

        AssetLocation actual = await store.ResolveAsync(
            Asset.Id<TestResource>("character.robot"));

        Assert.Equal(expected, actual);
    }

    private sealed class TestResource;

    private sealed class RecordingResolver : IAssetResolver
    {
        public string Scheme => "memory";

        public string? LastAddress { get; private set; }

        public ValueTask<AssetLocation> ResolveAsync(
            AssetAddress address,
            CancellationToken cancellationToken = default)
        {
            LastAddress = address.ToString();
            return ValueTask.FromResult(
                new AssetLocation("memory", LastAddress));
        }
    }
}
