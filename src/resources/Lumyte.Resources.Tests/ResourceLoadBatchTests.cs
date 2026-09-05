using System.Text;

namespace Lumyte.Resources.Tests;

public sealed class ResourceLoadBatchTests
{
    [Fact]
    public async Task LoadsMixedResourceTypesAndReportsProgress()
    {
        ResourceStore store = CreateStore();
        ResourceLoadBatch batch = store.CreateLoadBatch(
            new ResourceLoadBatchOptions { Priority = ResourceLoadPriority.High });
        ResourceLoadBatchItem<TextResource> text = batch.Add(
            Asset.From<TextResource>("memory:hello"));
        ResourceLoadBatchItem<LengthResource> length = batch.Add(
            Asset.From<LengthResource>("memory:four"));
        var progress = new List<ResourceLoadProgress>();
        object progressGate = new();
        batch.ProgressChanged += value =>
        {
            lock (progressGate)
            {
                progress.Add(value);
            }
        };

        ResourceLoadBatchResult result = await batch.LoadAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal("hello", result.Get(text).Value.Text);
        Assert.Equal(4, result.Get(length).Value.Length);
        lock (progressGate)
        {
            Assert.Equal(new ResourceLoadProgress(0, 2, 0, 0), progress[0]);
            Assert.Equal(new ResourceLoadProgress(2, 2, 2, 0), progress[^1]);
        }
    }

    [Fact]
    public async Task ReportsFailedItemsWithoutDiscardingSuccesses()
    {
        ResourceStore store = CreateStore();
        ResourceLoadBatch batch = store.CreateLoadBatch();
        ResourceLoadBatchItem<TextResource> success = batch.Add(
            Asset.From<TextResource>("memory:success"));
        ResourceLoadBatchItem<FailingResource> failure = batch.Add(
            Asset.From<FailingResource>("memory:failure"));

        ResourceLoadBatchResult result = await batch.LoadAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(1, result.SucceededCount);
        Assert.Collection(
            result.Failures,
            item =>
            {
                Assert.Equal(typeof(FailingResource), item.ResourceType);
                Assert.IsType<ResourceLoadException>(item.Exception);
            });
        Assert.Equal("success", result.Get(success).Value.Text);
        Assert.False(result.TryGet(failure, out _));
    }

    [Fact]
    public async Task ScopeOwnsEverySuccessfulBatchItem()
    {
        ResourceStore store = CreateStore();
        await using ResourceScope scope = store.CreateScope(
            new ResourceScopeOptions { UnloadUnusedOnDispose = true });
        ResourceLoadBatch batch = store.CreateLoadBatch();
        ResourceLoadBatchItem<TextResource> text = batch.Add(
            Asset.From<TextResource>("memory:owned"));

        ResourceLoadBatchResult result = await scope.LoadBatchAsync(batch);
        ResourceHandle<TextResource> handle = result.Get(text);
        await scope.DisposeAsync();

        Assert.False(handle.TryGetValue(out _));
    }

    [Fact]
    public async Task BatchCanOnlyRunOnce()
    {
        ResourceStore store = CreateStore();
        ResourceLoadBatch batch = store.CreateLoadBatch();
        batch.Add(Asset.From<TextResource>("memory:item"));
        await batch.LoadAsync();

        Assert.Throws<InvalidOperationException>(
            () => batch.Add(Asset.From<TextResource>("memory:other")));
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await batch.LoadAsync());
    }

    private static ResourceStore CreateStore() =>
        new(
            [new TestResolver()],
            [new TextResourceLoader(), new LengthResourceLoader(), new FailingResourceLoader()]);

    private sealed record TextResource(string Text);

    private sealed record LengthResource(int Length);

    private sealed class FailingResource;

    private sealed class TextResourceLoader : IResourceLoader<TextResource>
    {
        public ValueTask<TextResource> LoadAsync(
            ResourceLoadContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new TextResource(context.Data.Location.Path));
    }

    private sealed class LengthResourceLoader : IResourceLoader<LengthResource>
    {
        public ValueTask<LengthResource> LoadAsync(
            ResourceLoadContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new LengthResource(context.Data.Location.Path.Length));
    }

    private sealed class FailingResourceLoader : IResourceLoader<FailingResource>
    {
        public ValueTask<FailingResource> LoadAsync(
            ResourceLoadContext context,
            CancellationToken cancellationToken = default) =>
            throw new InvalidDataException("Invalid batch test data.");
    }

    private sealed class TestResolver : IAssetResolver
    {
        public string Scheme => "memory";

        public ValueTask<AssetData> OpenAsync(
            AssetAddress address,
            CancellationToken cancellationToken = default)
        {
            string name = address.ToString();
            return ValueTask.FromResult(
                new AssetData(
                    new MemoryStream(Encoding.UTF8.GetBytes(name)),
                    new AssetLocation("memory", name)));
        }
    }
}
