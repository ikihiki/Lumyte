using System.Security.Cryptography;
using System.Text;
using Lumyte.Memory;
using Lumyte.Resources.FileSystem;
using Xunit;

namespace Lumyte.Resources.Tests;

/// <summary>Verifies immutable file revisions, bounded reads and root confinement.</summary>
public sealed class FileResourceSourceTests
{
    /// <summary>A snapshot preserves the old bytes after replacement and deletion of its source.</summary>
    /// <returns>Completion of snapshot ownership checks.</returns>
    [Fact]
    public async Task SnapshotSurvivesSourceReplacementAndDeletionAsync()
    {
        using var files = new SourceDirectory();
        await using var memory = new MemoryManager();
        FileResourceSource source = files.CreateSource(memory);
        var id = new ResourceId("asset.bin");
        byte[] original = "original source"u8.ToArray();
        await File.WriteAllBytesAsync(files.AssetPath, original);
        await using ResourceSourceSnapshot snapshot = await source.OpenSnapshotAsync(id);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(original)), snapshot.Revision);
        Assert.Equal(snapshot.Revision, await source.GetRevisionAsync(id));

        string replacement = Path.Combine(files.Root, "replacement.bin");
        await File.WriteAllTextAsync(replacement, "new source");
        File.Move(replacement, files.AssetPath, true);
        Assert.NotEqual(snapshot.Revision, await source.GetRevisionAsync(id));
        File.Delete(files.AssetPath);
        Assert.Null(await source.GetRevisionAsync(id));

        await using Stream reader = await snapshot.OpenReadAsync();
        byte[] actual = new byte[original.Length];
        await reader.ReadExactlyAsync(actual);
        Assert.Equal(original, actual);
        Assert.Equal(0, await reader.ReadAsync(actual));
        Assert.Equal(0UL, memory.GetSnapshot(new("resources.source")).CommittedBytes);
    }

    /// <summary>Readers have independent cursors and cannot leave their requested range.</summary>
    /// <returns>Completion of range and cursor checks.</returns>
    [Fact]
    public async Task RangesHaveIndependentCursorsAndRespectBoundsAsync()
    {
        using var files = new SourceDirectory();
        await using var memory = new MemoryManager();
        FileResourceSource source = files.CreateSource(memory);
        await File.WriteAllTextAsync(files.AssetPath, "0123456789");
        await using ResourceSourceSnapshot snapshot = await source.OpenSnapshotAsync(new("asset.bin"));
        Assert.True(snapshot.CanReadRange);
        Assert.Equal(10L, snapshot.Length);
        await using Stream first = await snapshot.OpenReadAsync(2, 3);
        await using Stream second = await snapshot.OpenReadAsync(6, 2);
        byte[] bytes = new byte[8];

        Assert.Equal(3, await first.ReadAsync(bytes));
        Assert.Equal("234", Encoding.UTF8.GetString(bytes.AsSpan(0, 3)));
        Assert.Equal(0, await first.ReadAsync(bytes));
        Assert.Equal(0, second.Position);
        Assert.Equal(2, await second.ReadAsync(bytes));
        Assert.Equal("67", Encoding.UTF8.GetString(bytes.AsSpan(0, 2)));
        Assert.Equal(1, first.Seek(-2, SeekOrigin.End));
        Assert.Equal('3', first.ReadByte());
        Assert.Throws<IOException>(() => first.Seek(-1, SeekOrigin.Begin));
        Assert.Throws<IOException>(() => first.Seek(4, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => first.WriteByte(0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await snapshot.OpenReadAsync(-1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await snapshot.OpenReadAsync(11));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await snapshot.OpenReadAsync(9, 2));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await snapshot.OpenReadAsync(0, 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await snapshot.OpenReadAsync(0, -1));
        await using Stream end = await snapshot.OpenReadAsync(10);
        Assert.Equal(0, await end.ReadAsync(bytes));
    }

    /// <summary>Disposing the snapshot prevents new readers while already opened handles remain valid.</summary>
    /// <returns>Completion of temporary storage cleanup.</returns>
    [Fact]
    public async Task LastReaderClosesTemporaryStorageAsync()
    {
        using var files = new SourceDirectory();
        await using var memory = new MemoryManager();
        FileResourceSource source = files.CreateSource(memory);
        await File.WriteAllTextAsync(files.AssetPath, "still readable");
        ResourceSourceSnapshot snapshot = await source.OpenSnapshotAsync(new("asset.bin"));
        Assert.Single(Directory.GetFiles(files.Snapshots));
        await using Stream reader = await snapshot.OpenReadAsync();
        await snapshot.DisposeAsync();
        await snapshot.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await snapshot.OpenReadAsync());

        byte[] content = new byte[14];
        await reader.ReadExactlyAsync(content);
        Assert.Equal("still readable", Encoding.UTF8.GetString(content));
        await reader.DisposeAsync();
        Assert.Empty(Directory.GetFiles(files.Snapshots));
    }

    /// <summary>Missing, empty and cancelled inputs have distinct behavior and do not retain scratch buffers.</summary>
    /// <returns>Completion of source boundary checks.</returns>
    [Fact]
    public async Task MissingEmptyAndCancelledInputsReleaseTheirMemoryAsync()
    {
        using var files = new SourceDirectory();
        await using var memory = new MemoryManager();
        FileResourceSource source = files.CreateSource(memory);
        var id = new ResourceId("asset.bin");
        Assert.Null(await source.GetRevisionAsync(id));
        await Assert.ThrowsAsync<FileNotFoundException>(async () => await source.OpenSnapshotAsync(id));
        await Assert.ThrowsAsync<ArgumentException>(async () => await source.OpenSnapshotAsync(default));
        await File.WriteAllBytesAsync(files.AssetPath, []);
        await using ResourceSourceSnapshot empty = await source.OpenSnapshotAsync(id);
        Assert.Equal(0L, empty.Length);
        await using Stream reader = await empty.OpenReadAsync();
        Assert.Equal(-1, reader.ReadByte());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await source.OpenSnapshotAsync(id, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await source.GetRevisionAsync(id, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await empty.OpenReadAsync(cancellationToken: cancellation.Token));
        Assert.Equal(0UL, memory.GetSnapshot(new("resources.source")).CommittedBytes);
    }

    /// <summary>The content revision changes even if a writer preserves file length and modification time.</summary>
    /// <returns>Completion of revision comparison.</returns>
    [Fact]
    public async Task RevisionUsesContentInsteadOfLengthAndTimestampAsync()
    {
        using var files = new SourceDirectory();
        await using var memory = new MemoryManager();
        FileResourceSource source = files.CreateSource(memory);
        var id = new ResourceId("asset.bin");
        await File.WriteAllTextAsync(files.AssetPath, "before");
        DateTime timestamp = File.GetLastWriteTimeUtc(files.AssetPath);
        string? previous = await source.GetRevisionAsync(id);
        await File.WriteAllTextAsync(files.AssetPath, "after!");
        File.SetLastWriteTimeUtc(files.AssetPath, timestamp);
        Assert.NotEqual(previous, await source.GetRevisionAsync(id));
    }

    /// <summary>Nested normal directories work while file and directory links are rejected.</summary>
    /// <returns>Completion of path validation.</returns>
    [Fact]
    public async Task RejectsFileAndDirectorySymbolicLinksAsync()
    {
        using var files = new SourceDirectory();
        await using var memory = new MemoryManager();
        FileResourceSource source = files.CreateSource(memory);
        string outside = Path.Combine(files.Container, "outside");
        Directory.CreateDirectory(outside);
        await File.WriteAllTextAsync(Path.Combine(outside, "asset.bin"), "outside");
        Directory.CreateDirectory(Path.Combine(files.Root, "nested"));
        await File.WriteAllTextAsync(Path.Combine(files.Root, "nested", "asset.bin"), "inside");
        await using ResourceSourceSnapshot nested = await source.OpenSnapshotAsync(new("nested/asset.bin"));
        Assert.Equal(6L, nested.Length);

        try
        {
            File.CreateSymbolicLink(files.AssetPath, Path.Combine(outside, "asset.bin"));
            Directory.CreateSymbolicLink(Path.Combine(files.Root, "linked"), outside);
        }
        catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows())
        {
            // Windows installations without Developer Mode require elevation for symbolic links.
            return;
        }

        await Assert.ThrowsAsync<IOException>(async () => await source.OpenSnapshotAsync(new("asset.bin")));
        await Assert.ThrowsAsync<IOException>(async () => await source.GetRevisionAsync(new("asset.bin")));
        await Assert.ThrowsAsync<IOException>(async () => await source.OpenSnapshotAsync(new("linked/asset.bin")));
        await Assert.ThrowsAsync<IOException>(async () => await source.GetRevisionAsync(new("linked/asset.bin")));
        Assert.Throws<IOException>(() => new FileResourceSource(new() { RootPath = Path.Combine(files.Root, "linked") }, memory));
        Assert.Throws<IOException>(() => new FileResourceSource(new() { RootPath = Path.Combine(files.Root, "linked", "child") }, memory));
    }

    /// <summary>Large encoded inputs fit a scratch-buffer budget much smaller than the file.</summary>
    /// <returns>Completion of the bounded-memory copy.</returns>
    [Fact]
    public async Task LargeInputUsesBoundedScratchMemoryAsync()
    {
        using var files = new SourceDirectory();
        var pool = new MemoryPoolId("resources.source");
        await using var memory = new MemoryManager(new Dictionary<MemoryPoolId, MemoryBudget> { [pool] = new(4096, 4096) });
        FileResourceSource source = files.CreateSource(memory, 4096);
        await using (var file = new FileStream(files.AssetPath, FileMode.CreateNew, FileAccess.Write))
        {
            file.SetLength(1024 * 1024);
        }

        await using ResourceSourceSnapshot snapshot = await source.OpenSnapshotAsync(new("asset.bin"));
        Assert.Equal(1024 * 1024, snapshot.Length);
        Assert.Equal(0UL, memory.GetSnapshot(pool).CommittedBytes);
        Assert.Equal(snapshot.Revision, await source.GetRevisionAsync(new("asset.bin")));
        Assert.Equal(0UL, memory.GetSnapshot(pool).CommittedBytes);
    }

    /// <summary>Rejected scratch allocation creates no snapshot and returns the source handle and memory scope.</summary>
    /// <returns>Completion of failed acquisition cleanup.</returns>
    [Fact]
    public async Task ExhaustedScratchBudgetLeavesNoTemporaryFilesAsync()
    {
        using var files = new SourceDirectory();
        var pool = new MemoryPoolId("resources.source");
        await using var memory = new MemoryManager(new Dictionary<MemoryPoolId, MemoryBudget> { [pool] = new(32, 32) });
        FileResourceSource source = files.CreateSource(memory, 64);
        await File.WriteAllTextAsync(files.AssetPath, "asset");

        await Assert.ThrowsAsync<MemoryBudgetExceededException>(async () => await source.OpenSnapshotAsync(new("asset.bin")));
        await Assert.ThrowsAsync<MemoryBudgetExceededException>(async () => await source.GetRevisionAsync(new("asset.bin")));
        Assert.Empty(Directory.GetFiles(files.Snapshots));
        Assert.Equal(0UL, memory.GetSnapshot(pool).CommittedBytes);
        Assert.Equal(0UL, memory.GetSnapshot(pool).ReservedBytes);
        File.Delete(files.AssetPath);
    }

    private sealed class SourceDirectory : IDisposable
    {
        internal SourceDirectory()
        {
            Container = Path.Combine(Path.GetTempPath(), $"lumyte-resource-tests-{Guid.NewGuid():N}");
            Root = Path.Combine(Container, "root");
            Snapshots = Path.Combine(Container, "snapshots");
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Snapshots);
        }

        internal string Container { get; }

        internal string Root { get; }

        internal string Snapshots { get; }

        internal string AssetPath => Path.Combine(Root, "asset.bin");

        public void Dispose() => Directory.Delete(Container, true);

        internal FileResourceSource CreateSource(IMemoryManager memory, int bufferSize = 65536)
            => new(new() { RootPath = Root, SnapshotDirectory = Snapshots, BufferSizeBytes = bufferSize }, memory);
    }
}
