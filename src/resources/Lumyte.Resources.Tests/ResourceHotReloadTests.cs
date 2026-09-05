using System.Text;

namespace Lumyte.Resources.Tests;

public sealed class ResourceHotReloadTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public void FileChangeSourceMapsPathsToPortableAssetAddresses()
    {
        string root = Path.Combine(Path.GetTempPath(), $"Lumyte-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        using var source = new FileAssetChangeSource(root);
        var changes = new List<AssetChange>();
        source.Changed += changes.Add;

        source.PublishChange(Path.Combine(root, "models", "character.glb"));
        source.PublishChange(Path.Combine(root, "..", "outside.glb"));

        AssetChange change = Assert.Single(changes);
        Assert.Equal("file", change.Scheme);
        Assert.Equal("models/character.glb", change.Address);
        Directory.Delete(root);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task FileChangeReloadsTheCurrentResource()
    {
        string root = Path.Combine(Path.GetTempPath(), $"Lumyte-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "message.txt");
        await File.WriteAllTextAsync(path, "old");
        try
        {
            ResourceStore store = new([new FileAssetResolver(root)], [new TextResourceLoader()]);
            ResourceHandle<TextResource> handle = await store.LoadAsync(
                Asset.From<TextResource>("file:message.txt"));
            using var source = new FileAssetChangeSource(root);
            await using ResourceHotReloadManager hotReload = new(
                store, [source], new ResourceHotReloadOptions { DebounceDelay = TimeSpan.Zero });
            var reloaded = new TaskCompletionSource<ResourceHotReloadResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            hotReload.Reloaded += result => reloaded.TrySetResult(result);
            hotReload.Start();
            await File.WriteAllTextAsync(path, "updated");

            source.PublishChange(path);
            ResourceHotReloadResult result = await reloaded.Task;

            Assert.Equal(1, result.ReloadedResourceCount);
            Assert.Equal("updated", handle.Value.Text);
            Assert.InRange(handle.Generation, 1u, uint.MaxValue);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CoalescesChangesAndReloadsAllTypesAndDependents()
    {
        var resolver = new MutableResolver("old");
        ResourceStore store = new(
            [resolver],
            [new TextResourceLoader(), new LengthResourceLoader(), new ParentResourceLoader()]);
        AssetKey<TextResource> textKey = Asset.From<TextResource>("memory:item");
        ResourceHandle<TextResource> text = await store.LoadAsync(textKey);
        ResourceHandle<LengthResource> length = await store.LoadAsync(
            Asset.From<LengthResource>("memory:item"));
        ResourceHandle<ParentResource> parent = await store.LoadAsync(
            Asset.From<ParentResource>("memory:parent"));
        var source = new ManualChangeSource();
        var timeProvider = new ManualTimerTimeProvider();
        await using ResourceHotReloadManager hotReload = new(
            store,
            [source],
            new ResourceHotReloadOptions
            {
                DebounceDelay = TimeSpan.FromSeconds(1),
                TimeProvider = timeProvider
            });
        var reloaded = new TaskCompletionSource<ResourceHotReloadResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        hotReload.Reloaded += result => reloaded.TrySetResult(result);
        hotReload.Start();
        resolver.Content = "updated";

        source.Raise(new AssetChange("memory", "item"));
        source.Raise(new AssetChange("memory", "item"));
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        ResourceHotReloadResult result = await reloaded.Task;

        Assert.Equal(3, result.ReloadedResourceCount);
        Assert.Equal("updated", text.Value.Text);
        Assert.Equal(7, length.Value.Length);
        Assert.Equal("updated", parent.Value.Child.Text);
        Assert.Equal(1u, text.Generation);
        Assert.Equal(1u, length.Generation);
        Assert.Equal(1u, parent.Generation);
    }

    [Fact]
    public async Task FailedHotReloadKeepsTheCurrentGeneration()
    {
        var resolver = new MutableResolver("valid");
        var loader = new FailableResourceLoader();
        ResourceStore store = new([resolver], [loader]);
        AssetKey<FailableResource> key = Asset.From<FailableResource>("memory:item");
        ResourceHandle<FailableResource> handle = await store.LoadAsync(key);
        var source = new ManualChangeSource();
        await using ResourceHotReloadManager hotReload = new(
            store,
            [source],
            new ResourceHotReloadOptions { DebounceDelay = TimeSpan.Zero });
        var failed = new TaskCompletionSource<ResourceHotReloadFailure>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        hotReload.ReloadFailed += failure => failed.TrySetResult(failure);
        hotReload.Start();
        loader.Fail = true;

        source.Raise(new AssetChange("memory", "item"));
        ResourceHotReloadFailure failure = await failed.Task;

        Assert.IsType<ResourceLoadException>(failure.Exception);
        Assert.Equal("valid", handle.Value.Text);
        Assert.Equal(0u, handle.Generation);
    }

    [Fact]
    public async Task DisposalDrainsSupersededWorkAndAllDisposersWaitForIt()
    {
        ResourceStore store = new([new MutableResolver("old")], [new TextResourceLoader()]);
        await store.LoadAsync(Asset.From<TextResource>("memory:item"));
        var source = new ManualChangeSource();
        var manager = new ResourceHotReloadManager(store, [source], new() { DebounceDelay = TimeSpan.Zero });
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        int notifications = 0;
        manager.Reloaded += _ =>
        {
            if (Interlocked.Increment(ref notifications) != 1) { return; }
            entered.SetResult();
            release.Wait();
        };
        manager.Start();
        Task notification = Task.Run(() => source.Raise(new("memory", "item")));
        await entered.Task;

        try
        {
            source.Raise(new("memory", "item"));
            Task first = manager.DisposeAsync().AsTask();
            Task second = manager.DisposeAsync().AsTask();

            Assert.False(first.IsCompleted, "Disposal must retain the superseded notification.");
            Assert.Same(first, second);
        }
        finally
        {
            release.Set();
            await notification;
            await manager.DisposeAsync();
        }
    }

    [Fact]
    public async Task CapturedNotificationIsIgnoredAfterDisposal()
    {
        ResourceStore store = new([], []);
        var source = new ManualChangeSource();
        var manager = new ResourceHotReloadManager(store, [source]);
        manager.Start();
        Action<AssetChange> captured = source.Capture();

        await manager.DisposeAsync();
        captured(new("memory", "item"));

        Assert.False(source.HasSubscribers);
        Assert.Throws<InvalidOperationException>(manager.Start);
    }

    [Fact]
    public async Task FailedNotificationIsReportedWhenTheManagerIsDisposed()
    {
        ResourceStore store = new([], []);
        var source = new ManualChangeSource();
        var manager = new ResourceHotReloadManager(store, [source], new() { DebounceDelay = TimeSpan.Zero });
        var error = new InvalidOperationException("Notification failed.");
        manager.Reloaded += _ => throw new InvalidOperationException("Reload observer failed.");
        manager.ReloadFailed += _ => throw error;
        manager.Start();
        source.Raise(new("memory", "item"));

        AggregateException failure = await Assert.ThrowsAsync<AggregateException>(() => manager.DisposeAsync().AsTask());

        Assert.Same(error, Assert.Single(failure.InnerExceptions));
    }

    private sealed record TextResource(string Text);

    private sealed record LengthResource(int Length);

    private sealed record ParentResource(TextResource Child);

    private sealed record FailableResource(string Text);

    private sealed class TextResourceLoader : IResourceLoader<TextResource>
    {
        public async ValueTask<TextResource> LoadAsync(
            ResourceLoadContext context,
            CancellationToken cancellationToken = default)
        {
            using StreamReader reader = new(context.Content, Encoding.UTF8, leaveOpen: true);
            return new TextResource(await reader.ReadToEndAsync(cancellationToken));
        }
    }

    private sealed class LengthResourceLoader : IResourceLoader<LengthResource>
    {
        public async ValueTask<LengthResource> LoadAsync(
            ResourceLoadContext context,
            CancellationToken cancellationToken = default)
        {
            using StreamReader reader = new(context.Content, Encoding.UTF8, leaveOpen: true);
            string text = await reader.ReadToEndAsync(cancellationToken);
            return new LengthResource(text.Length);
        }
    }

    private sealed class ParentResourceLoader : IResourceLoader<ParentResource>
    {
        public async ValueTask<ParentResource> LoadAsync(
            ResourceLoadContext context,
            CancellationToken cancellationToken = default)
        {
            ResourceDependency<TextResource> child = await context.LoadAsync(
                Asset.From<TextResource>("memory:item"),
                cancellationToken);
            return new ParentResource(child.Value);
        }
    }

    private sealed class FailableResourceLoader : IResourceLoader<FailableResource>
    {
        public bool Fail { get; set; }

        public async ValueTask<FailableResource> LoadAsync(
            ResourceLoadContext context,
            CancellationToken cancellationToken = default)
        {
            using StreamReader reader = new(context.Content, Encoding.UTF8, leaveOpen: true);
            string text = await reader.ReadToEndAsync(cancellationToken);
            return Fail
                ? throw new InvalidDataException("Invalid hot reload test data.")
                : new FailableResource(text);
        }
    }

    private sealed class MutableResolver(string content) : IAssetResolver
    {
        public string Content { get; set; } = content;

        public string Scheme => "memory";

        public ValueTask<AssetData> OpenAsync(
            AssetAddress address,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(
                new AssetData(
                    new MemoryStream(Encoding.UTF8.GetBytes(Content)),
                    new AssetLocation("memory", address.ToString())));
    }

    private sealed class ManualChangeSource : IAssetChangeSource
    {
        public event Action<AssetChange>? Changed;

        public void Raise(AssetChange change) => Changed?.Invoke(change);
        public Action<AssetChange> Capture() => Changed!;
        public bool HasSubscribers => Changed is not null;
    }

    private sealed class ManualTimerTimeProvider : TimeProvider
    {
        private readonly List<ManualTimer> timers = [];
        private readonly Lock gate = new();
        private long timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Volatile.Read(ref timestamp);

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            ManualTimer timer = new(this, callback, state, dueTime, period);
            lock (gate)
            {
                timers.Add(timer);
            }

            return timer;
        }

        public void Advance(TimeSpan duration)
        {
            long now = Interlocked.Add(ref timestamp, duration.Ticks);
            ManualTimer[] snapshot;
            lock (gate)
            {
                snapshot = [.. timers];
            }

            foreach (ManualTimer timer in snapshot)
            {
                timer.FireIfDue(now);
            }
        }

        private sealed class ManualTimer(
            ManualTimerTimeProvider owner,
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period) : ITimer
        {
            private long dueAt = owner.GetTimestamp() + dueTime.Ticks;
            private int disposed;

            public bool Change(TimeSpan dueTime, TimeSpan newPeriod)
            {
                dueAt = owner.GetTimestamp() + dueTime.Ticks;
                return disposed == 0;
            }

            public void Dispose() => Interlocked.Exchange(ref disposed, 1);

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            internal void FireIfDue(long now)
            {
                if (disposed != 0 || now < Volatile.Read(ref dueAt))
                {
                    return;
                }

                callback(state);
                if (period == Timeout.InfiniteTimeSpan)
                {
                    Dispose();
                }
                else
                {
                    dueAt = now + period.Ticks;
                }
            }
        }
    }
}
