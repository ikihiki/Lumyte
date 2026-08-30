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

        ResourceHandle<TextResource> handle = await store.LoadAsync(key);

        Assert.Equal(new TextResource("hello", string.Empty), handle.Value);
        Assert.Equal("documents/readme", resolver.LastAddress);
    }

    [Fact]
    public async Task PassesSelectorToLoader()
    {
        RecordingResolver resolver = new("hello");
        ResourceStore store = CreateStore(resolver, new TextResourceLoader());
        AssetKey<TextResource> key = AssetKey<TextResource>.Parse(
            "memory:documents/readme#section/introduction");

        ResourceHandle<TextResource> handle = await store.LoadAsync(key);

        Assert.Equal(new TextResource("hello", "section/introduction"), handle.Value);
    }

    [Fact]
    public async Task InternsRepeatedKeysOnce()
    {
        RecordingResolver resolver = new("hello");
        ResourceStore store = CreateStore(
            resolver,
            new TextResourceLoader());
        AssetKey<TextResource> key = Asset.From<TextResource>("memory:documents/readme");

        await store.LoadAsync(key);
        await store.LoadAsync(key);

        Assert.Equal(1, store.InternedKeyCount);
        Assert.Equal(1, resolver.OpenCount);
    }

    [Fact]
    public async Task ReloadReplacesTheCurrentGeneration()
    {
        MutableResolver resolver = new("first");
        ResourceStore store = CreateStore(resolver, new TextResourceLoader());
        AssetKey<TextResource> key = Asset.From<TextResource>("memory:item");
        ResourceHandle<TextResource> first = await store.LoadAsync(key);
        resolver.Content = "second";

        ResourceHandle<TextResource> second = await store.ReloadAsync(key);
        ResourceHandle<TextResource> current = await store.LoadAsync(key);

        Assert.Equal("first", first.Value.Text);
        Assert.Equal("second", second.Value.Text);
        Assert.Equal(second, current);
        Assert.Equal(0u, first.Generation);
        Assert.Equal(1u, second.Generation);
    }

    [Fact]
    public async Task FailedReloadKeepsTheCurrentGeneration()
    {
        MutableResolver resolver = new("first");
        ResourceStore store = CreateStore(resolver, new TextResourceLoader());
        AssetKey<TextResource> key = Asset.From<TextResource>("memory:item");
        ResourceHandle<TextResource> first = await store.LoadAsync(key);
        resolver.Failure = new IOException("Unavailable test asset.");

        await Assert.ThrowsAsync<AssetSourceException>(
            async () => await store.ReloadAsync(key));
        ResourceHandle<TextResource> current = await store.LoadAsync(key);

        Assert.Equal(first, current);
        Assert.Equal(0u, current.Generation);
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

        ResourceHandle<TextResource> actual = await store.LoadAsync(
            Asset.Id<TextResource>("character.robot"));

        Assert.Equal(new TextResource("robot", string.Empty), actual.Value);
    }

    [Fact]
    public async Task SelectsMultipleResourcesFromOneAssetAddress()
    {
        RecordingResolver resolver = new("package data");
        ResourceStore store = CreateStore(resolver, new PackageItemLoader());
        AssetKey<PackageItem> bodyKey = Asset.From<PackageItem>(
            "memory:robot.package",
            new PackageItemSelector("Body"));
        AssetKey<PackageItem> headKey = Asset.From<PackageItem>(
            "memory:robot.package",
            new PackageItemSelector("Head"));

        ResourceHandle<PackageItem> body = await store.LoadAsync(bodyKey);
        ResourceHandle<PackageItem> head = await store.LoadAsync(headKey);

        Assert.Equal(new PackageItem("Body", 10), body.Value);
        Assert.Equal(new PackageItem("Head", 20), head.Value);
    }

    [Fact]
    public async Task ReportsMissingSubresource()
    {
        ResourceStore store = CreateStore(
            new RecordingResolver("package data"),
            new PackageItemLoader());
        AssetKey<PackageItem> key = Asset.From<PackageItem>(
            "memory:robot.package",
            new PackageItemSelector("Missing"));

        await Assert.ThrowsAsync<ResourceNotFoundException>(
            async () => await store.LoadAsync(key));
    }

    [Fact]
    public async Task TracksDependenciesLoadedThroughContext()
    {
        RecordingResolver resolver = new("hello");
        ResourceStore store = CreateStore(
            [resolver],
            [new TextResourceLoader(), new ParentResourceLoader()]);
        AssetKey<ParentResource> parentKey = Asset.From<ParentResource>("memory:parent");

        ResourceHandle<ParentResource> parent = await store.LoadAsync(parentKey);

        Assert.Equal("hello", parent.Value.FirstChild.Text);
        Assert.Equal("hello", parent.Value.SecondChild.Text);
        Assert.Equal(2, store.GetDependencyCount(parentKey));
        Assert.Equal(3, resolver.OpenCount);
    }

    [Fact]
    public async Task RejectsDependencyCycles()
    {
        ResourceStore storeWithBothLoaders = CreateStore(
            [new RecordingResolver("cycle")],
            [new FirstCycleResourceLoader(), new SecondCycleResourceLoader()]);

        await Assert.ThrowsAsync<ResourceDependencyCycleException>(
            async () => await storeWithBothLoaders.LoadAsync(
                Asset.From<FirstCycleResource>("memory:first")));
    }

    [Fact]
    public async Task DisposesDependentsBeforeDependencies()
    {
        List<string> disposalOrder = [];
        ResourceStore store = CreateStore(
            [new RecordingResolver("value")],
            [
                new DisposableChildLoader(disposalOrder),
                new DisposableParentLoader(disposalOrder)
            ]);

        await store.LoadAsync(Asset.From<DisposableParent>("memory:parent"));

        await store.DisposeAsync();

        Assert.Equal(["parent", "child"], disposalOrder);
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

    private sealed record PackageItem(string Name, int Value);

    private readonly record struct PackageItemSelector(string Name)
        : IResourceSelector<PackageItem>
    {
        public void WriteTo(ResourceSelectorBuilder builder)
        {
            builder.WriteSegment("item");
            builder.WriteSegment(Name);
        }
    }

    private sealed class PackageItemLoader : IResourceLoader<PackageItem>
    {
        private static readonly IReadOnlyDictionary<string, PackageItem> s_items =
            new Dictionary<string, PackageItem>(StringComparer.Ordinal)
            {
                ["Body"] = new PackageItem("Body", 10),
                ["Head"] = new PackageItem("Head", 20)
            };

        public ValueTask<PackageItem> LoadAsync(
            ResourceLoadContext context,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResourceSelector.Enumerator segments = context.Selector.GetEnumerator();
            if (!segments.MoveNext()
                || !segments.Current.Span.SequenceEqual("item")
                || !segments.MoveNext())
            {
                throw new ResourceNotFoundException("The package item selector is invalid.");
            }

            string name = segments.Current.ToString();
            if (segments.MoveNext() || !s_items.TryGetValue(name, out PackageItem? item))
            {
                throw new ResourceNotFoundException(
                    $"The package does not contain the '{name}' item.");
            }

            return ValueTask.FromResult(item);
        }
    }

    private sealed record ParentResource(
        TextResource FirstChild,
        TextResource SecondChild);

    private sealed class ParentResourceLoader : IResourceLoader<ParentResource>
    {
        public async ValueTask<ParentResource> LoadAsync(
            ResourceLoadContext context,
            CancellationToken cancellationToken = default)
        {
            ResourceHandle<TextResource> firstChild = await context.LoadAsync(
                Asset.From<TextResource>("memory:first-child"),
                cancellationToken);
            ResourceHandle<TextResource> secondChild = await context.LoadAsync(
                Asset.From<TextResource>("memory:second-child"),
                cancellationToken);
            return new ParentResource(firstChild.Value, secondChild.Value);
        }
    }

    private sealed class FirstCycleResource;

    private sealed class SecondCycleResource;

    private sealed class FirstCycleResourceLoader : IResourceLoader<FirstCycleResource>
    {
        public async ValueTask<FirstCycleResource> LoadAsync(
            ResourceLoadContext context,
            CancellationToken cancellationToken = default)
        {
            await context.LoadAsync(
                Asset.From<SecondCycleResource>("memory:second"),
                cancellationToken);
            return new FirstCycleResource();
        }
    }

    private sealed class SecondCycleResourceLoader : IResourceLoader<SecondCycleResource>
    {
        public async ValueTask<SecondCycleResource> LoadAsync(
            ResourceLoadContext context,
            CancellationToken cancellationToken = default)
        {
            await context.LoadAsync(
                Asset.From<FirstCycleResource>("memory:first"),
                cancellationToken);
            return new SecondCycleResource();
        }
    }

    private abstract class DisposableResource(
        string name,
        List<string> disposalOrder) : IDisposable
    {
        public void Dispose() => disposalOrder.Add(name);
    }

    private sealed class DisposableChild(List<string> disposalOrder)
        : DisposableResource("child", disposalOrder);

    private sealed class DisposableParent(List<string> disposalOrder)
        : DisposableResource("parent", disposalOrder);

    private sealed class DisposableChildLoader(List<string> disposalOrder)
        : IResourceLoader<DisposableChild>
    {
        public ValueTask<DisposableChild> LoadAsync(
            ResourceLoadContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new DisposableChild(disposalOrder));
    }

    private sealed class DisposableParentLoader(List<string> disposalOrder)
        : IResourceLoader<DisposableParent>
    {
        public async ValueTask<DisposableParent> LoadAsync(
            ResourceLoadContext context,
            CancellationToken cancellationToken = default)
        {
            await context.LoadAsync(
                Asset.From<DisposableChild>("memory:child"),
                cancellationToken);
            return new DisposableParent(disposalOrder);
        }
    }

    private sealed class TextResourceLoader : IResourceLoader<TextResource>
    {
        public async ValueTask<TextResource> LoadAsync(
            ResourceLoadContext context,
            CancellationToken cancellationToken = default)
        {
            using StreamReader reader = new(
                context.Content,
                Encoding.UTF8,
                leaveOpen: true);
            string text = await reader.ReadToEndAsync(cancellationToken);
            return new TextResource(text, context.Selector.ToString());
        }
    }

    private sealed class FailingResourceLoader : IResourceLoader<TextResource>
    {
        public ValueTask<TextResource> LoadAsync(
            ResourceLoadContext context,
            CancellationToken cancellationToken = default) =>
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

    private sealed class MutableResolver(string content) : IAssetResolver
    {
        public string Scheme => "memory";

        public string Content { get; set; } = content;

        public Exception? Failure { get; set; }

        public ValueTask<AssetData> OpenAsync(
            AssetAddress address,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure is not null)
            {
                throw new AssetSourceException("The test asset could not be opened.", Failure);
            }

            return ValueTask.FromResult(
                new AssetData(
                    new MemoryStream(Encoding.UTF8.GetBytes(Content)),
                    new AssetLocation("memory", address.ToString())));
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
