using System.Text;

namespace Lumyte.Resources.Tests;

public sealed class ResourceStoreTests
{
    [Fact]
    public async Task LoadsTypedResourceThroughRegisteredResolverAndLoader()
    {
        RecordingResolver resolver = new("hello");
        ResourceStore store = CreateStore(resolver, new TextResourceLoader());
        AssetKey<TextResource> key = Asset.From<TextResource>("memory:documents/readme");

        TextResource resource = await store.LoadAsync(key);

        Assert.Equal(new TextResource("hello", string.Empty), resource);
        Assert.Equal("documents/readme", resolver.LastAddress);
    }

    [Fact]
    public async Task PassesSelectorToLoader()
    {
        RecordingResolver resolver = new("hello");
        ResourceStore store = CreateStore(resolver, new TextResourceLoader());
        AssetKey<TextResource> key = AssetKey<TextResource>.Parse(
            "memory:documents/readme#section/introduction");

        TextResource resource = await store.LoadAsync(key);

        Assert.Equal(new TextResource("hello", "section/introduction"), resource);
    }

    [Fact]
    public async Task InternsRepeatedKeysOnce()
    {
        ResourceStore store = CreateStore(
            new RecordingResolver("hello"),
            new TextResourceLoader());
        AssetKey<TextResource> key = Asset.From<TextResource>("memory:documents/readme");

        await store.LoadAsync(key);
        await store.LoadAsync(key);

        Assert.Equal(1, store.InternedKeyCount);
    }

    [Fact]
    public async Task RejectsUnregisteredScheme()
    {
        ResourceStore store = CreateStore(
            Array.Empty<IAssetResolver>(),
            [new TextResourceLoader()]);
        AssetKey<TextResource> key = Asset.From<TextResource>("unknown:item");

        AssetResolutionException exception = await Assert.ThrowsAsync<AssetResolutionException>(
            async () => await store.LoadAsync(key));

        Assert.Contains("unknown", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsMissingLoader()
    {
        RecordingResolver resolver = new("hello");
        ResourceStore store = CreateStore([resolver], []);
        AssetKey<TextResource> key = Asset.From<TextResource>("memory:item");

        await Assert.ThrowsAsync<ResourceLoaderNotFoundException>(
            async () => await store.LoadAsync(key));

        Assert.Equal(0, resolver.OpenCount);
    }

    [Fact]
    public async Task DisposesOpenedDataAfterLoading()
    {
        TrackingStream stream = new(Encoding.UTF8.GetBytes("hello"));
        RecordingResolver resolver = new(stream);
        ResourceStore store = CreateStore(resolver, new TextResourceLoader());

        await store.LoadAsync(Asset.From<TextResource>("memory:item"));

        Assert.True(stream.IsDisposed);
    }

    [Fact]
    public async Task WrapsLoaderFailures()
    {
        ResourceStore store = CreateStore(
            new RecordingResolver("hello"),
            new FailingResourceLoader());

        ResourceLoadException exception = await Assert.ThrowsAsync<ResourceLoadException>(
            async () => await store.LoadAsync(
                Asset.From<TextResource>("memory:item")));

        Assert.IsType<InvalidDataException>(exception.InnerException);
    }

    [Fact]
    public async Task FileResolverRejectsEncodedTraversal()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "LumyteResourcesTests"));
        ResourceStore store = CreateStore(
            new FileAssetResolver(root),
            new TextResourceLoader());
        AssetKey<TextResource> key = AssetKey<TextResource>.Parse("file:%2E%2E/secret.bin");

        await Assert.ThrowsAsync<AssetResolutionException>(
            async () => await store.LoadAsync(key));
    }

    [Fact]
    public async Task CatalogOpensStableIdentifier()
    {
        AssetLocation expectedLocation = new("package", "content/42");
        CatalogAssetResolver resolver = new(
            new Dictionary<string, Func<CancellationToken, ValueTask<AssetData>>>
            {
                ["character.robot"] = _ => ValueTask.FromResult(
                    new AssetData(
                        new MemoryStream(Encoding.UTF8.GetBytes("robot")),
                        expectedLocation))
            });
        ResourceStore store = CreateStore(resolver, new TextResourceLoader());

        TextResource actual = await store.LoadAsync(
            Asset.Id<TextResource>("character.robot"));

        Assert.Equal(new TextResource("robot", string.Empty), actual);
    }

    private static ResourceStore CreateStore(
        IAssetResolver resolver,
        IResourceLoader loader) =>
        CreateStore([resolver], [loader]);

    private static ResourceStore CreateStore(
        IEnumerable<IAssetResolver> resolvers,
        IEnumerable<IResourceLoader> loaders) =>
        new(resolvers, loaders);

    private sealed record TextResource(string Text, string Selector);

    private sealed class TextResourceLoader : IResourceLoader<TextResource>
    {
        public async ValueTask<T> LoadAsync<T>(
            ResourceLoadContext context,
            CancellationToken cancellationToken = default)
            where T : notnull
        {
            Assert.Equal(typeof(TextResource), typeof(T));
            using StreamReader reader = new(
                context.Content,
                Encoding.UTF8,
                leaveOpen: true);
            string text = await reader.ReadToEndAsync(cancellationToken);
            TextResource resource = new(text, context.Selector.ToString());
            return (T)(object)resource;
        }
    }

    private sealed class FailingResourceLoader : IResourceLoader<TextResource>
    {
        public ValueTask<T> LoadAsync<T>(
            ResourceLoadContext context,
            CancellationToken cancellationToken = default)
            where T : notnull =>
            throw new InvalidDataException("Invalid test data.");
    }

    private sealed class RecordingResolver : IAssetResolver
    {
        private readonly Func<Stream> open;

        public RecordingResolver(string content)
            : this(() => new MemoryStream(Encoding.UTF8.GetBytes(content)))
        {
        }

        public RecordingResolver(Stream content)
            : this(() => content)
        {
        }

        private RecordingResolver(Func<Stream> open)
        {
            this.open = open;
        }

        public string Scheme => "memory";

        public string? LastAddress { get; private set; }

        public int OpenCount { get; private set; }

        public ValueTask<AssetData> OpenAsync(
            AssetAddress address,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastAddress = address.ToString();
            OpenCount++;
            return ValueTask.FromResult(
                new AssetData(
                    open(),
                    new AssetLocation("memory", LastAddress)));
        }
    }

    private sealed class TrackingStream(byte[] buffer) : MemoryStream(buffer)
    {
        public bool IsDisposed { get; private set; }

        public override ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return base.DisposeAsync();
        }
    }
}
