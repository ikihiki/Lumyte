using Lumyte.Resources.FileSystem;
using Xunit;

namespace Lumyte.Resources.Tests;

/// <summary>Verifies native filesystem notifications, bounded invalidation and watcher ownership.</summary>
public sealed class FileResourceChangeSourceTests
{
    /// <summary>The stream source alone supplies monitoring to the manager without independent watcher registration.</summary>
    /// <returns>Completion of native source-integrated reload and generation retention.</returns>
    [Fact]
    public async Task FileSourceAutomaticallyReloadsItsOwnStreamsAsync()
    {
        using var files = new WatchDirectory();
        string path = Path.Combine(files.Root, "asset.txt");
        await File.WriteAllTextAsync(path, "before");
        var source = new FileResourceSource(new() { RootPath = files.Root });
        await using Lumyte.Memory.MemoryManager memory = new();
        await using ResourceManager manager = new("files", source, [new TextStage()], memory);
        using ResourceReference<TextValue> reference = await manager.AcquireAsync(new ResourceKey<TextValue>("asset.txt"));
        using ResourceLease<TextValue> original = reference.Acquire();
        TaskCompletionSource published = new(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.HotReload!.Reloaded += (_, args) =>
        {
            if (args.Result.Status == ResourceReloadStatus.Published)
            {
                published.TrySetResult();
            }
        };
        await File.WriteAllTextAsync(path, "after");
        await published.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using ResourceLease<TextValue> current = reference.Acquire();
        Assert.Equal("after", current.Value.Text);
        Assert.Equal("before", original.Value.Text);
        Assert.DoesNotContain(typeof(ResourceManager).GetConstructors().SelectMany(constructor => constructor.GetParameters()), parameter => parameter.Name == "changeSource");
    }

    /// <summary>Native notifications are filtered before queueing and observe updates to the loaded input set.</summary>
    /// <returns>Completion of unrelated-file filtering and dynamic input discovery.</returns>
    [Fact]
    public async Task LoadedTargetsFilterNativeEventsAndCanGrowAsync()
    {
        using var files = new WatchDirectory();
        var source = new FileResourceSource(new() { RootPath = files.Root });
        var targets = new WatchTargets { Resources = new HashSet<ResourceId> { new("tracked.bin") } };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using IAsyncEnumerator<ResourceSourceChange> changes = source.Changes.WatchAsync(targets, timeout.Token).GetAsyncEnumerator();
        Assert.True(await changes.MoveNextAsync());
        Assert.True(changes.Current.RequiresRescan);
        await File.WriteAllTextAsync(Path.Combine(files.Root, "untracked.bin"), "ignored");
        await File.WriteAllTextAsync(Path.Combine(files.Root, "tracked.bin"), "loaded");
        await ReadUntilAsync(changes, change =>
        {
            Assert.NotEqual(new ResourceId("untracked.bin"), change.Id);
            return change.RequiresRescan || change.Id == new ResourceId("tracked.bin");
        });
        targets.Resources = new HashSet<ResourceId> { new("tracked.bin"), new("later.bin") };
        await File.WriteAllTextAsync(Path.Combine(files.Root, "later.bin"), "newly loaded");
        await ReadUntilAsync(changes, change =>
        {
            Assert.NotEqual(new ResourceId("untracked.bin"), change.Id);
            return change.RequiresRescan || change.Id == new ResourceId("later.bin");
        });
    }

    /// <summary>Subscription startup rescans and nested updates remain visible beneath the configured root.</summary>
    /// <returns>Completion of recursive notification checks.</returns>
    [Fact]
    public async Task InitialRescanAndNestedWriteAreObservedAsync()
    {
        using var files = new WatchDirectory();
        string directory = Path.Combine(files.Root, "nested");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "asset.bin");
        await File.WriteAllTextAsync(path, "old");
        var source = new FileResourceSource(new() { RootPath = files.Root });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using IAsyncEnumerator<ResourceSourceChange> changes = source.Changes.WatchAsync(timeout.Token).GetAsyncEnumerator();
        Assert.True(await changes.MoveNextAsync());
        Assert.True(changes.Current.RequiresRescan);

        await File.WriteAllTextAsync(path, "new");
        await ReadUntilAsync(changes, change => change.RequiresRescan || change.Id == new ResourceId("nested/asset.bin"));
    }

    /// <summary>A file rename invalidates both names, and atomic replacement invalidates its destination.</summary>
    /// <returns>Completion of rename and replace notifications.</returns>
    [Fact]
    public async Task RenameAndAtomicReplacementAreObservedAsync()
    {
        using var files = new WatchDirectory();
        string oldPath = Path.Combine(files.Root, "old.bin");
        string newPath = Path.Combine(files.Root, "new.bin");
        string replacement = Path.Combine(files.Root, "replacement.bin");
        await File.WriteAllTextAsync(oldPath, "old");
        await File.WriteAllTextAsync(replacement, "replacement");
        var source = new FileResourceSource(new() { RootPath = files.Root });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using IAsyncEnumerator<ResourceSourceChange> changes = source.Changes.WatchAsync(timeout.Token).GetAsyncEnumerator();
        Assert.True(await changes.MoveNextAsync());
        File.Move(oldPath, newPath);
        bool oldObserved = false;
        bool newObserved = false;
        await ReadUntilAsync(changes, change =>
        {
            oldObserved |= change.Id == new ResourceId("old.bin");
            newObserved |= change.Id == new ResourceId("new.bin");
            return change.RequiresRescan || (oldObserved && newObserved);
        });

        File.Move(replacement, newPath, true);
        await ReadUntilAsync(changes, change => change.RequiresRescan || change.Id == new ResourceId("new.bin"));
    }

    /// <summary>Renaming and deleting populated directories requests revalidation of descendant resources.</summary>
    /// <param name="delete">Whether to delete the directory instead of renaming it.</param>
    /// <returns>Completion of structural invalidation.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectoryChangesRequestRescanAsync(bool delete)
    {
        using var files = new WatchDirectory();
        string path = Path.Combine(files.Root, "folder");
        Directory.CreateDirectory(path);
        await File.WriteAllTextAsync(Path.Combine(path, "asset.bin"), "asset");
        var source = new FileResourceSource(new() { RootPath = files.Root });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using IAsyncEnumerator<ResourceSourceChange> changes = source.Changes.WatchAsync(timeout.Token).GetAsyncEnumerator();
        Assert.True(await changes.MoveNextAsync());
        if (delete)
        {
            Directory.Delete(path, true);
        }
        else
        {
            Directory.Move(path, Path.Combine(files.Root, "renamed"));
        }

        await ReadUntilAsync(changes, change => change.RequiresRescan);
    }

    /// <summary>Direct source reads do not write files or feed back into the reload loop.</summary>
    /// <param name="rangeRead">Whether to read a bounded range.</param>
    /// <returns>Completion of read-only source checks.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectReadsDoNotProduceNotificationsAsync(bool rangeRead)
    {
        using var files = new WatchDirectory();
        await File.WriteAllTextAsync(Path.Combine(files.Root, "asset.bin"), "asset");
        var options = new FileResourceSourceOptions { RootPath = files.Root };
        var changesSource = new FileResourceSource(options);
        var source = new FileResourceSource(options);
        using var cancellation = new CancellationTokenSource();
        await using IAsyncEnumerator<ResourceSourceChange> changes = changesSource.Changes.WatchAsync(cancellation.Token).GetAsyncEnumerator();
        Assert.True(await changes.MoveNextAsync());
        ValueTask<bool> pending = changes.MoveNextAsync();
        await using (ResourceSourceSnapshot snapshot = await source.OpenSnapshotAsync(new("asset.bin")))
        {
            await using Stream reader = await snapshot.OpenReadAsync(rangeRead ? 1 : 0, rangeRead ? 2 : null);
            byte[] bytes = new byte[5];
            Assert.Equal(rangeRead ? 2 : 5, await reader.ReadAsync(bytes));
            Assert.Equal(snapshot.Revision, await source.GetRevisionAsync(new("asset.bin")));
        }

        cancellation.CancelAfter(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
    }

    /// <summary>Notification pressure replaces queued hints with a full rescan instead of silently dropping changes.</summary>
    /// <returns>Completion of bounded overflow handling.</returns>
    [Fact]
    public async Task BoundedQueueOverflowRequestsRescanAsync()
    {
        using var files = new WatchDirectory();
        var source = new FileResourceSource(new() { RootPath = files.Root }, new() { NotificationCapacity = 1 });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using IAsyncEnumerator<ResourceSourceChange> changes = source.Changes.WatchAsync(timeout.Token).GetAsyncEnumerator();
        Assert.True(await changes.MoveNextAsync());
        for (int index = 0; index < 64; index++)
        {
            await File.WriteAllTextAsync(Path.Combine(files.Root, $"asset-{index}.bin"), "asset");
        }

        // Leave the single-slot queue unread while the operating system delivers the burst.
        await Task.Delay(200, timeout.Token);
        Assert.True(await changes.MoveNextAsync());
        Assert.True(changes.Current.RequiresRescan);
        await File.WriteAllTextAsync(Path.Combine(files.Root, "after.bin"), "after");
        await ReadUntilAsync(changes, change => change.RequiresRescan || change.Id == new ResourceId("after.bin"));
    }

    /// <summary>Cancelling one enumeration releases it without stopping another subscription.</summary>
    /// <returns>Completion of independent subscription ownership.</returns>
    [Fact]
    public async Task SubscriptionsOwnCancellationAndDisposalAsync()
    {
        using var files = new WatchDirectory();
        var source = new FileResourceSource(new() { RootPath = files.Root });
        using var cancellation = new CancellationTokenSource();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using IAsyncEnumerator<ResourceSourceChange> first = source.Changes.WatchAsync(cancellation.Token).GetAsyncEnumerator();
        await using IAsyncEnumerator<ResourceSourceChange> second = source.Changes.WatchAsync(timeout.Token).GetAsyncEnumerator();
        Assert.True(await first.MoveNextAsync());
        Assert.True(await second.MoveNextAsync());
        ValueTask<bool> pending = first.MoveNextAsync();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
        await first.DisposeAsync();
        await File.WriteAllTextAsync(Path.Combine(files.Root, "asset.bin"), "asset");
        await ReadUntilAsync(second, change => change.RequiresRescan || change.Id == new ResourceId("asset.bin"));
        await second.DisposeAsync();
        Directory.Delete(files.Root, true);
        Directory.CreateDirectory(files.Root);
        await using IAsyncEnumerator<ResourceSourceChange> restarted = source.Changes.WatchAsync(timeout.Token).GetAsyncEnumerator();
        Assert.True(await restarted.MoveNextAsync());
        Assert.True(restarted.Current.RequiresRescan);
    }

    /// <summary>Invalid limits and symbolic roots fail before native watcher ownership starts.</summary>
    [Fact]
    public void RejectsInvalidOptionsAndLinkedRoot()
    {
        using var files = new WatchDirectory();
        var options = new FileResourceSourceOptions { RootPath = files.Root };
        Assert.Throws<ArgumentOutOfRangeException>(() => new FileResourceSource(options, new() { NotificationCapacity = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FileResourceSource(options, new() { InternalBufferSizeBytes = 1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FileResourceSource(options, new() { InternalBufferSizeBytes = 65537 }));
        Assert.Throws<DirectoryNotFoundException>(() => new FileResourceSource(new() { RootPath = Path.Combine(files.Root, "missing") }));
        string link = Path.Combine(files.Container, "link");
        try
        {
            Directory.CreateSymbolicLink(link, files.Root);
        }
        catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows())
        {
            // Windows installations without Developer Mode require elevation for symbolic links.
            return;
        }

        Assert.Throws<IOException>(() => new FileResourceSource(new() { RootPath = link }));
    }

    /// <summary>An unrelated symbolic link is unaddressable and does not stop ordinary asset monitoring.</summary>
    /// <returns>Completion of unrelated link filtering.</returns>
    [Fact]
    public async Task UnrelatedSymbolicLinkDoesNotStopWatchingAsync()
    {
        using var files = new WatchDirectory();
        string outside = Path.Combine(files.Container, "outside");
        Directory.CreateDirectory(outside);
        await File.WriteAllTextAsync(Path.Combine(outside, "asset.bin"), "outside");
        var source = new FileResourceSource(new() { RootPath = files.Root });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using IAsyncEnumerator<ResourceSourceChange> changes = source.Changes.WatchAsync(timeout.Token).GetAsyncEnumerator();
        Assert.True(await changes.MoveNextAsync());
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(files.Root, "linked"), outside);
        }
        catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows())
        {
            // Windows installations without Developer Mode require elevation for symbolic links.
            return;
        }

        await File.WriteAllTextAsync(Path.Combine(files.Root, "asset.bin"), "inside");
        await ReadUntilAsync(changes, change => change.RequiresRescan || change.Id == new ResourceId("asset.bin"));
    }

    private static async Task ReadUntilAsync(IAsyncEnumerator<ResourceSourceChange> changes, Func<ResourceSourceChange, bool> predicate)
    {
        while (await changes.MoveNextAsync())
        {
            if (predicate(changes.Current))
            {
                return;
            }
        }

        Assert.Fail("The file watcher completed before the expected notification.");
    }

    private sealed class TextValue(string text)
    {
        internal string Text { get; } = text;
    }

    private sealed class TextStage : IResourceSourceStage<TextValue>
    {
        public async ValueTask<TextValue> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
        {
            ResourceSourceSnapshot input = await context.OpenSourceAsync(context.Address.Id, cancellationToken: cancellationToken);
            await using Stream stream = await input.OpenReadAsync(cancellationToken: cancellationToken);
            using StreamReader reader = new(stream);
            return new(await reader.ReadToEndAsync(cancellationToken));
        }

        public ValueTask ReleaseAsync(TextValue resource) => ValueTask.CompletedTask;
    }

    private sealed class WatchTargets : IResourceWatchTargets
    {
        private IReadOnlySet<ResourceId> _resources = new HashSet<ResourceId>();

        public IReadOnlySet<ResourceId> Resources
        {
            get => Volatile.Read(ref _resources);
            set => Volatile.Write(ref _resources, value);
        }
    }

    private sealed class WatchDirectory : IDisposable
    {
        internal WatchDirectory()
        {
            Container = Path.Combine(Path.GetTempPath(), $"lumyte-watch-tests-{Guid.NewGuid():N}");
            Root = Path.Combine(Container, "root");
            Directory.CreateDirectory(Root);
        }

        internal string Container { get; }

        internal string Root { get; }

        public void Dispose() => Directory.Delete(Container, true);
    }
}
