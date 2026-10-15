using Lumyte.Memory;
using Lumyte.Resources.FileSystem;
using Xunit;

namespace Lumyte.Resources.Tests;

/// <summary>Verifies native filesystem notifications, bounded invalidation and watcher ownership.</summary>
public sealed class FileResourceChangeSourceTests
{
    /// <summary>Subscription startup rescans and nested updates remain visible when the default scratch directory contains the root.</summary>
    /// <returns>Completion of recursive notification checks.</returns>
    [Fact]
    public async Task InitialRescanAndNestedWriteAreObservedAsync()
    {
        using var files = new WatchDirectory();
        string directory = Path.Combine(files.Root, "nested");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "asset.bin");
        await File.WriteAllTextAsync(path, "old");
        var source = new FileResourceChangeSource(new() { RootPath = files.Root });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using IAsyncEnumerator<ResourceSourceChange> changes = source.WatchAsync(timeout.Token).GetAsyncEnumerator();
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
        var source = new FileResourceChangeSource(new() { RootPath = files.Root });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using IAsyncEnumerator<ResourceSourceChange> changes = source.WatchAsync(timeout.Token).GetAsyncEnumerator();
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
        var source = new FileResourceChangeSource(new() { RootPath = files.Root });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using IAsyncEnumerator<ResourceSourceChange> changes = source.WatchAsync(timeout.Token).GetAsyncEnumerator();
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

    /// <summary>Source snapshot creation and deletion never feed back into the reload loop.</summary>
    /// <param name="nestedSnapshots">Whether snapshots have a dedicated directory beneath the resource root.</param>
    /// <returns>Completion of scratch exclusion checks.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SnapshotWritesAreExcludedAsync(bool nestedSnapshots)
    {
        using var files = new WatchDirectory();
        string snapshots = nestedSnapshots ? Path.Combine(files.Root, "working", "snapshots") : files.Root;
        Directory.CreateDirectory(snapshots);
        await File.WriteAllTextAsync(Path.Combine(files.Root, "asset.bin"), "asset");
        var options = new FileResourceSourceOptions { RootPath = files.Root, SnapshotDirectory = snapshots };
        var changesSource = new FileResourceChangeSource(options);
        await using var memory = new MemoryManager();
        var source = new FileResourceSource(options, memory);
        using var cancellation = new CancellationTokenSource();
        await using IAsyncEnumerator<ResourceSourceChange> changes = changesSource.WatchAsync(cancellation.Token).GetAsyncEnumerator();
        Assert.True(await changes.MoveNextAsync());
        ValueTask<bool> pending = changes.MoveNextAsync();
        await using (ResourceSourceSnapshot snapshot = await source.OpenSnapshotAsync(new("asset.bin")))
        {
            Assert.NotNull(snapshot.Revision);
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
        var source = new FileResourceChangeSource(new() { RootPath = files.Root }, new() { NotificationCapacity = 1 });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using IAsyncEnumerator<ResourceSourceChange> changes = source.WatchAsync(timeout.Token).GetAsyncEnumerator();
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
        var source = new FileResourceChangeSource(new() { RootPath = files.Root });
        using var cancellation = new CancellationTokenSource();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using IAsyncEnumerator<ResourceSourceChange> first = source.WatchAsync(cancellation.Token).GetAsyncEnumerator();
        await using IAsyncEnumerator<ResourceSourceChange> second = source.WatchAsync(timeout.Token).GetAsyncEnumerator();
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
        await using IAsyncEnumerator<ResourceSourceChange> restarted = source.WatchAsync(timeout.Token).GetAsyncEnumerator();
        Assert.True(await restarted.MoveNextAsync());
        Assert.True(restarted.Current.RequiresRescan);
    }

    /// <summary>Invalid limits and symbolic roots fail before native watcher ownership starts.</summary>
    [Fact]
    public void RejectsInvalidOptionsAndLinkedRoot()
    {
        using var files = new WatchDirectory();
        var options = new FileResourceSourceOptions { RootPath = files.Root };
        Assert.Throws<ArgumentOutOfRangeException>(() => new FileResourceChangeSource(options, new() { NotificationCapacity = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FileResourceChangeSource(options, new() { InternalBufferSizeBytes = 1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FileResourceChangeSource(options, new() { InternalBufferSizeBytes = 65537 }));
        Assert.Throws<DirectoryNotFoundException>(() => new FileResourceChangeSource(new() { RootPath = Path.Combine(files.Root, "missing") }));
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

        Assert.Throws<IOException>(() => new FileResourceChangeSource(new() { RootPath = link }));
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
        var source = new FileResourceChangeSource(new() { RootPath = files.Root });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using IAsyncEnumerator<ResourceSourceChange> changes = source.WatchAsync(timeout.Token).GetAsyncEnumerator();
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
