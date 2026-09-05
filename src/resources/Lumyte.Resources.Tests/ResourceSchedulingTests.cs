using System.Collections.Concurrent;
using System.Text;

namespace Lumyte.Resources.Tests;

public sealed class ResourceSchedulingTests
{
    [Fact]
    public async Task HigherPriorityLoadStartsFirst()
    {
        LoadCoordinator coordinator = new("blocker");
        ResourceStore store = CreateStore(
            coordinator,
            new ResourceSchedulingOptions { MaxConcurrentLoads = 1 });
        Task blockerStarted = coordinator.ExpectStart("blocker");
        Task<ResourceHandle<TestResource>> blocker = store.LoadAsync(
            Asset.From<TestResource>("memory:blocker")).AsTask();
        await blockerStarted;
        Task<ResourceHandle<TestResource>> low = store.LoadAsync(
            Asset.From<TestResource>("memory:low"),
            new ResourceLoadOptions { Priority = ResourceLoadPriority.Low }).AsTask();
        Task<ResourceHandle<TestResource>> high = store.LoadAsync(
            Asset.From<TestResource>("memory:high"),
            new ResourceLoadOptions { Priority = ResourceLoadPriority.High }).AsTask();

        coordinator.ReleaseBlocker();
        await Task.WhenAll(blocker, low, high);

        Assert.Equal(["blocker", "high", "low"], coordinator.StartOrder);
    }

    [Fact]
    public async Task SharedLoadAdoptsTheHighestWaitingPriority()
    {
        LoadCoordinator coordinator = new("blocker");
        ResourceStore store = CreateStore(
            coordinator,
            new ResourceSchedulingOptions { MaxConcurrentLoads = 1 });
        Task blockerStarted = coordinator.ExpectStart("blocker");
        Task<ResourceHandle<TestResource>> blocker = store.LoadAsync(
            Asset.From<TestResource>("memory:blocker")).AsTask();
        await blockerStarted;
        AssetKey<TestResource> sharedKey = Asset.From<TestResource>("memory:shared");
        Task<ResourceHandle<TestResource>> low = store.LoadAsync(
            sharedKey,
            new ResourceLoadOptions { Priority = ResourceLoadPriority.Low }).AsTask();
        Task<ResourceHandle<TestResource>> normal = store.LoadAsync(
            Asset.From<TestResource>("memory:normal")).AsTask();
        Task<ResourceHandle<TestResource>> high = store.LoadAsync(
            sharedKey,
            new ResourceLoadOptions { Priority = ResourceLoadPriority.High }).AsTask();

        coordinator.ReleaseBlocker();
        await Task.WhenAll(blocker, low, normal, high);
        ResourceHandle<TestResource> lowHandle = await low;
        ResourceHandle<TestResource> highHandle = await high;

        Assert.Equal(["blocker", "shared", "normal"], coordinator.StartOrder);
        Assert.Equal(lowHandle.Id, highHandle.Id);
    }

    [Fact]
    public async Task FreeLaneRunsWhileAnotherLaneIsFull()
    {
        LoadCoordinator coordinator = new("cpu-blocker");
        ResourceSchedulingOptions scheduling = new()
        {
            MaxConcurrentLoads = 2,
            MaxConcurrentLoadsPerLane = new Dictionary<ResourceExecutionLane, int>
            {
                [ResourceExecutionLane.Cpu] = 1,
                [ResourceExecutionLane.Graphics] = 1
            }
        };
        ResourceStore store = new(
            [new TestResolver()],
            [new CpuResourceLoader(coordinator), new GraphicsResourceLoader(coordinator)],
            new ResourceStoreOptions { Scheduling = scheduling });
        Task blockerStarted = coordinator.ExpectStart("cpu-blocker");
        Task graphicsStarted = coordinator.ExpectStart("graphics");
        Task<ResourceHandle<CpuResource>> blocker = store.LoadAsync(
            Asset.From<CpuResource>("memory:cpu-blocker")).AsTask();
        await blockerStarted;
        Task<ResourceHandle<CpuResource>> queuedCpu = store.LoadAsync(
            Asset.From<CpuResource>("memory:cpu-queued")).AsTask();

        Task<ResourceHandle<GraphicsResource>> graphics = store.LoadAsync(
            Asset.From<GraphicsResource>("memory:graphics")).AsTask();
        await graphicsStarted;

        Assert.DoesNotContain("cpu-queued", coordinator.StartOrder);
        coordinator.ReleaseBlocker();
        await Task.WhenAll(blocker, queuedCpu, graphics);
    }

    [Fact]
    public async Task CancelingOneWaiterKeepsTheSharedLoad()
    {
        LoadCoordinator coordinator = new("blocker");
        ResourceStore store = CreateStore(
            coordinator,
            new ResourceSchedulingOptions { MaxConcurrentLoads = 1 });
        Task blockerStarted = coordinator.ExpectStart("blocker");
        Task secondStarted = coordinator.ExpectStart("second");
        Task<ResourceHandle<TestResource>> blocker = store.LoadAsync(
            Asset.From<TestResource>("memory:blocker")).AsTask();
        await blockerStarted;
        using CancellationTokenSource cancellation = new();
        AssetKey<TestResource> secondKey = Asset.From<TestResource>("memory:second");
        Task<ResourceHandle<TestResource>> canceledWaiter = store.LoadAsync(
            secondKey,
            cancellation.Token).AsTask();
        Task<ResourceHandle<TestResource>> remainingWaiter = store.LoadAsync(secondKey).AsTask();

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await canceledWaiter);
        coordinator.ReleaseBlocker();
        await secondStarted;
        ResourceHandle<TestResource> result = await remainingWaiter;
        await blocker;

        Assert.Equal("second", result.Value.Name);
        Assert.Equal(1, coordinator.StartOrder.Count(name => name == "second"));
    }

    [Fact]
    public async Task DependencyLoadsDoNotWaitForTheirParentLane()
    {
        LoadCoordinator coordinator = new("unused");
        ResourceStore store = new(
            [new TestResolver()],
            [new TestResourceLoader(coordinator), new ParentResourceLoader()],
            new ResourceStoreOptions
            {
                Scheduling = new ResourceSchedulingOptions
                {
                    MaxConcurrentLoads = 1,
                    MaxConcurrentLoadsPerLane = new Dictionary<ResourceExecutionLane, int>
                    {
                        [ResourceExecutionLane.Cpu] = 1
                    }
                }
            });

        ResourceHandle<ParentResource> parent = await store.LoadAsync(
            Asset.From<ParentResource>("memory:parent"));

        Assert.Equal("child", parent.Value.Child.Name);
    }

    [Fact]
    public async Task WaitingLoadAgesAheadOfNewerWork()
    {
        var timeProvider = new ManualTimeProvider();
        LoadCoordinator coordinator = new("blocker");
        ResourceStore store = CreateStore(
            coordinator,
            new ResourceSchedulingOptions
            {
                MaxConcurrentLoads = 1,
                AgingInterval = TimeSpan.FromSeconds(1),
                TimeProvider = timeProvider
            });
        Task blockerStarted = coordinator.ExpectStart("blocker");
        Task<ResourceHandle<TestResource>> blocker = store.LoadAsync(
            Asset.From<TestResource>("memory:blocker")).AsTask();
        await blockerStarted;
        Task<ResourceHandle<TestResource>> aged = store.LoadAsync(
            Asset.From<TestResource>("memory:aged"),
            new ResourceLoadOptions { Priority = ResourceLoadPriority.Low }).AsTask();
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        Task<ResourceHandle<TestResource>> newer = store.LoadAsync(
            Asset.From<TestResource>("memory:newer")).AsTask();

        coordinator.ReleaseBlocker();
        await Task.WhenAll(blocker, aged, newer);

        Assert.Equal(["blocker", "aged", "newer"], coordinator.StartOrder);
    }

    [Fact]
    public async Task LastCanceledWaiterRemovesQueuedLoad()
    {
        LoadCoordinator coordinator = new("blocker");
        ResourceStore store = CreateStore(
            coordinator,
            new ResourceSchedulingOptions { MaxConcurrentLoads = 1 });
        Task blockerStarted = coordinator.ExpectStart("blocker");
        Task<ResourceHandle<TestResource>> blocker = store.LoadAsync(
            Asset.From<TestResource>("memory:blocker")).AsTask();
        await blockerStarted;
        using CancellationTokenSource cancellation = new();
        Task<ResourceHandle<TestResource>> canceled = store.LoadAsync(
            Asset.From<TestResource>("memory:canceled"),
            cancellation.Token).AsTask();

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await canceled);
        coordinator.ReleaseBlocker();
        await blocker;

        Assert.DoesNotContain("canceled", coordinator.StartOrder);
    }

    private static ResourceStore CreateStore(
        LoadCoordinator coordinator,
        ResourceSchedulingOptions scheduling) =>
        new(
            [new TestResolver()],
            [new TestResourceLoader(coordinator)],
            new ResourceStoreOptions { Scheduling = scheduling });

    private sealed record TestResource(string Name);

    private sealed record CpuResource(string Name);

    private sealed record GraphicsResource(string Name);

    private sealed record ParentResource(TestResource Child);

    private sealed class TestResourceLoader(LoadCoordinator coordinator)
        : IResourceLoader<TestResource>
    {
        public ResourceExecutionLane LoadLane => ResourceExecutionLane.Cpu;

        public async ValueTask<TestResource> LoadAsync(
            ResourceLoadContext context,
            CancellationToken cancellationToken = default)
        {
            string name = context.Data.Location.Path;
            await coordinator.StartAsync(name, cancellationToken);
            return new TestResource(name);
        }
    }

    private sealed class CpuResourceLoader(LoadCoordinator coordinator)
        : IResourceLoader<CpuResource>
    {
        public ResourceExecutionLane LoadLane => ResourceExecutionLane.Cpu;

        public async ValueTask<CpuResource> LoadAsync(
            ResourceLoadContext context,
            CancellationToken cancellationToken = default)
        {
            string name = context.Data.Location.Path;
            await coordinator.StartAsync(name, cancellationToken);
            return new CpuResource(name);
        }
    }

    private sealed class GraphicsResourceLoader(LoadCoordinator coordinator)
        : IResourceLoader<GraphicsResource>
    {
        public ResourceExecutionLane LoadLane => ResourceExecutionLane.Graphics;

        public async ValueTask<GraphicsResource> LoadAsync(
            ResourceLoadContext context,
            CancellationToken cancellationToken = default)
        {
            string name = context.Data.Location.Path;
            await coordinator.StartAsync(name, cancellationToken);
            return new GraphicsResource(name);
        }
    }

    private sealed class ParentResourceLoader : IResourceLoader<ParentResource>
    {
        public ResourceExecutionLane LoadLane => ResourceExecutionLane.Cpu;

        public async ValueTask<ParentResource> LoadAsync(
            ResourceLoadContext context,
            CancellationToken cancellationToken = default)
        {
            ResourceDependency<TestResource> child = await context.LoadAsync(
                Asset.From<TestResource>("memory:child"),
                cancellationToken);
            return new ParentResource(child.Value);
        }
    }

    private sealed class LoadCoordinator(string blocker)
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource> starts = [];
        private readonly TaskCompletionSource release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Lock orderGate = new();
        private readonly List<string> startOrder = [];

        public IReadOnlyList<string> StartOrder
        {
            get
            {
                lock (orderGate)
                {
                    return [.. startOrder];
                }
            }
        }

        public Task ExpectStart(string name) => starts.GetOrAdd(
            name,
            _ => new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously)).Task;

        public async ValueTask StartAsync(
            string name,
            CancellationToken cancellationToken)
        {
            lock (orderGate)
            {
                startOrder.Add(name);
            }

            starts.GetOrAdd(
                name,
                _ => new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();
            if (name == blocker)
            {
                await release.Task.WaitAsync(cancellationToken);
            }
        }

        public void ReleaseBlocker() => release.TrySetResult();
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

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Volatile.Read(ref timestamp);

        public void Advance(TimeSpan duration) =>
            Interlocked.Add(ref timestamp, duration.Ticks);
    }
}
