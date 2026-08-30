using System.Text;

namespace Lumyte.Resources.Tests;

public sealed class ResourceLifetimeTests
{
    [Fact]
    public async Task BudgetCollectionEvictsTheLeastRecentlyUsedResource()
    {
        ResourceStoreOptions options = new()
        {
            MemoryBudgets = new Dictionary<ResourceMemoryPool, long>
            {
                [ResourceMemoryPool.Cpu] = 100
            }
        };
        ResourceStore store = new(
            [new ContentResolver()],
            [new SizedResourceLoader()],
            options);
        ResourceHandle<SizedResource> first = await store.LoadAsync(
            Asset.From<SizedResource>("memory:first"));
        ResourceHandle<SizedResource> second = await store.LoadAsync(
            Asset.From<SizedResource>("memory:second"));
        _ = second.Value;

        ResourceCollectionReport report = await store.CollectAsync();

        Assert.Equal(1, report.UnloadedResourceCount);
        Assert.False(first.TryGetValue(out _));
        Assert.True(second.TryGetValue(out _));
    }

    [Fact]
    public async Task LoaderAndDisposalUseTheirDeclaredLanes()
    {
        var dispatcher = new RecordingDispatcher();
        ResourceStore store = new(
            [new ContentResolver()],
            [new LaneResourceLoader()],
            dispatcher: dispatcher);
        await store.LoadAsync(Asset.From<LaneResource>("memory:item"));

        await store.CollectAsync(ResourceCollectionMode.AllUnused);

        Assert.Equal(
            [
                ResourceExecutionLane.Default,
                ResourceExecutionLane.Cpu,
                ResourceExecutionLane.Graphics
            ],
            dispatcher.Lanes);
    }

    [Fact]
    public async Task ConfigurationOverridesEveryExecutionLane()
    {
        ResourceExecutionLane openLane = new("configured-open");
        ResourceExecutionLane loadLane = new("configured-load");
        ResourceExecutionLane disposalLane = new("configured-disposal");
        var dispatcher = new RecordingDispatcher();
        ResourceStore store = new(
            new ResourceStoreConfiguration
            {
                Resolvers =
                [
                    new ResourceResolverRegistration(
                        new ContentResolver(),
                        openLane)
                ],
                Loaders =
                [
                    new ResourceLoaderRegistration(
                        new LaneResourceLoader(),
                        loadLane,
                        disposalLane)
                ],
                Dispatcher = dispatcher
            });
        await store.LoadAsync(Asset.From<LaneResource>("memory:item"));

        await store.CollectAsync(ResourceCollectionMode.AllUnused);

        Assert.Equal([openLane, loadLane, disposalLane], dispatcher.Lanes);
    }

    [Fact]
    public async Task RoutingDispatcherUsesTheConfiguredLaneTarget()
    {
        ResourceExecutionLane routedLane = new("routed");
        var routed = new RecordingDispatcher();
        var fallback = new RecordingDispatcher();
        RoutingResourceDispatcher dispatcher = new(
            new Dictionary<ResourceExecutionLane, IResourceDispatcher>
            {
                [routedLane] = routed
            },
            fallback);

        await dispatcher.InvokeAsync(
            routedLane,
            () => ValueTask.CompletedTask);
        await dispatcher.InvokeAsync(
            ResourceExecutionLane.Default,
            () => ValueTask.CompletedTask);

        Assert.Equal([routedLane], routed.Lanes);
        Assert.Equal([ResourceExecutionLane.Default], fallback.Lanes);
    }

    [Fact]
    public async Task ThreadPoolDispatcherRunsWorkOnTheThreadPool()
    {
        ThreadPoolResourceDispatcher dispatcher = new();

        bool isThreadPoolThread = await dispatcher.InvokeAsync(
            ResourceExecutionLane.Cpu,
            _ => ValueTask.FromResult(Thread.CurrentThread.IsThreadPoolThread));

        Assert.True(isThreadPoolThread);
    }

    [Fact]
    public async Task SynchronizationContextDispatcherPostsWorkToItsContext()
    {
        var context = new RecordingSynchronizationContext();
        SynchronizationContextResourceDispatcher dispatcher = new(context);
        bool invoked = false;

        await dispatcher.InvokeAsync(
            ResourceExecutionLane.Graphics,
            () =>
            {
                invoked = true;
                return ValueTask.CompletedTask;
            });

        Assert.True(invoked);
        Assert.Equal(1, context.PostCount);
    }

    [Fact]
    public async Task SnapshotRetainsTheOldGenerationUntilReleased()
    {
        var disposed = new List<int>();
        var loader = new DisposableGenerationLoader(disposed);
        ResourceStore store = new([new ContentResolver()], [loader]);
        AssetKey<DisposableGeneration> key =
            Asset.From<DisposableGeneration>("memory:item");
        ResourceHandle<DisposableGeneration> handle = await store.LoadAsync(key);
        ResourceSnapshot snapshot = store.CreateSnapshot();

        await store.ReloadAsync(key);

        Assert.Empty(disposed);
        Assert.Equal(1, snapshot.Get(handle).Generation);
        Assert.Equal(2, handle.Value.Generation);

        await snapshot.DisposeAsync();

        Assert.Equal([1], disposed);
    }

    [Fact]
    public async Task ExplicitUnloadRejectsRetainedResources()
    {
        ResourceStore store = new(
            [new ContentResolver()],
            [new SizedResourceLoader()]);
        ResourcePin<SizedResource> pin = await store.PinAsync(
            Asset.From<SizedResource>("memory:item"));

        await Assert.ThrowsAsync<ResourceInUseException>(
            async () => await store.UnloadAsync(pin.Handle.Id));
        await pin.DisposeAsync();
        await store.UnloadAsync(pin.Handle.Id);

        Assert.False(pin.Handle.TryGetValue(out _));
    }

    private sealed record SizedResource(string Name);

    private sealed class SizedResourceLoader : IResourceLoader<SizedResource>
    {
        public ValueTask<SizedResource> LoadAsync(
            ResourceLoadContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new SizedResource(context.Data.Location.Path));

        public IReadOnlyList<ResourceMemoryCost> Measure(SizedResource resource) =>
            [new ResourceMemoryCost(ResourceMemoryPool.Cpu, 80)];
    }

    private sealed class LaneResource : IDisposable
    {
        public void Dispose()
        {
        }
    }

    private sealed class LaneResourceLoader : IResourceLoader<LaneResource>
    {
        public ResourceExecutionLane LoadLane => ResourceExecutionLane.Cpu;

        public ResourceExecutionLane DisposalLane => ResourceExecutionLane.Graphics;

        public ValueTask<LaneResource> LoadAsync(
            ResourceLoadContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new LaneResource());
    }

    private sealed class DisposableGeneration(
        int generation,
        List<int> disposed) : IDisposable
    {
        public int Generation { get; } = generation;

        public void Dispose() => disposed.Add(Generation);
    }

    private sealed class DisposableGenerationLoader(List<int> disposed)
        : IResourceLoader<DisposableGeneration>
    {
        private int generation;

        public ValueTask<DisposableGeneration> LoadAsync(
            ResourceLoadContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(
                new DisposableGeneration(
                    Interlocked.Increment(ref generation),
                    disposed));
    }

    private sealed class ContentResolver : IAssetResolver
    {
        public string Scheme => "memory";

        public ValueTask<AssetData> OpenAsync(
            AssetAddress address,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(
                new AssetData(
                    new MemoryStream(Encoding.UTF8.GetBytes(address.ToString())),
                    new AssetLocation("memory", address.ToString())));
    }

    private sealed class RecordingDispatcher : IResourceDispatcher
    {
        public List<ResourceExecutionLane> Lanes { get; } = [];

        public ValueTask<T> InvokeAsync<T>(
            ResourceExecutionLane lane,
            Func<CancellationToken, ValueTask<T>> operation,
            CancellationToken cancellationToken = default)
        {
            Lanes.Add(lane);
            return operation(cancellationToken);
        }

        public ValueTask InvokeAsync(
            ResourceExecutionLane lane,
            Func<ValueTask> operation,
            CancellationToken cancellationToken = default)
        {
            Lanes.Add(lane);
            return operation();
        }
    }

    private sealed class RecordingSynchronizationContext : SynchronizationContext
    {
        public int PostCount { get; private set; }

        public override void Post(SendOrPostCallback callback, object? state)
        {
            PostCount++;
            callback(state);
        }
    }
}
