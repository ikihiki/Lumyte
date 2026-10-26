using System.Text;
using Lumyte.Resources.FileSystem;
using Xunit;

namespace Lumyte.Resources.Tests;

/// <summary>Verifies direct file reads, metadata change detection and root confinement.</summary>
public sealed class FileResourceSourceTests
{
    /// <summary>Replacement and deletion invalidate descriptors and open readers instead of preserving old files.</summary>
    /// <returns>Completion of direct input invalidation checks.</returns>
    [Fact]
    public async Task ReplacementAndDeletionInvalidateDirectReadsAsync()
    {
        using var files = new SourceDirectory();
        FileResourceSource source = files.CreateSource();
        var id = new ResourceId("asset.bin");
        await File.WriteAllTextAsync(files.AssetPath, "original source");
        await using ResourceSourceSnapshot snapshot = await source.OpenSnapshotAsync(id);
        await using Stream reader = await snapshot.OpenReadAsync();
        Assert.Equal(snapshot.Revision, await source.GetRevisionAsync(id));
        string replacement = Path.Combine(files.Root, "replacement.bin");
        await File.WriteAllTextAsync(replacement, "new source");

        // Keep the old handle open without requiring the OS to overwrite an open destination.
        File.Move(files.AssetPath, Path.Combine(files.Root, "previous.bin"));
        File.Move(replacement, files.AssetPath);
        Assert.NotEqual(snapshot.Revision, await source.GetRevisionAsync(id));
        await Assert.ThrowsAnyAsync<IOException>(async () => await snapshot.OpenReadAsync());
        Assert.ThrowsAny<IOException>(() => reader.ReadByte());
        File.Delete(files.AssetPath);
        Assert.Null(await source.GetRevisionAsync(id));
        await Assert.ThrowsAnyAsync<IOException>(async () => await reader.ReadExactlyAsync(new byte[1]));
    }

    /// <summary>Readers have independent cursors and cannot leave their requested range.</summary>
    /// <returns>Completion of range and cursor checks.</returns>
    [Fact]
    public async Task RangesHaveIndependentCursorsAndRespectBoundsAsync()
    {
        using var files = new SourceDirectory();
        FileResourceSource source = files.CreateSource();
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
    /// <returns>Completion of independent handle ownership checks.</returns>
    [Fact]
    public async Task ExistingReaderOwnsItsHandleAsync()
    {
        using var files = new SourceDirectory();
        FileResourceSource source = files.CreateSource();
        await File.WriteAllTextAsync(files.AssetPath, "still readable");
        ResourceSourceSnapshot snapshot = await source.OpenSnapshotAsync(new("asset.bin"));
        await using Stream reader = await snapshot.OpenReadAsync();
        await snapshot.DisposeAsync();
        await snapshot.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await snapshot.OpenReadAsync());

        byte[] content = new byte[14];
        await reader.ReadExactlyAsync(content);
        Assert.Equal("still readable", Encoding.UTF8.GetString(content));
        await reader.DisposeAsync();
        Assert.Single(Directory.GetFiles(files.Root));
    }

    /// <summary>Missing, empty and cancelled inputs have distinct behavior.</summary>
    /// <returns>Completion of source boundary checks.</returns>
    [Fact]
    public async Task MissingEmptyAndCancelledInputsAsync()
    {
        using var files = new SourceDirectory();
        FileResourceSource source = files.CreateSource();
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
    }

    /// <summary>In-place writes and truncation invalidate readers and change observed revisions.</summary>
    /// <param name="truncate">Whether to truncate instead of overwriting the same number of bytes.</param>
    /// <returns>Completion of concurrent writer detection.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InPlaceChangesInvalidateReadersAsync(bool truncate)
    {
        using var files = new SourceDirectory();
        FileResourceSource source = files.CreateSource();
        var id = new ResourceId("asset.bin");
        await File.WriteAllTextAsync(files.AssetPath, "before");
        DateTime timestamp = File.GetLastWriteTimeUtc(files.AssetPath);
        await using ResourceSourceSnapshot snapshot = await source.OpenSnapshotAsync(id);
        await using Stream reader = await snapshot.OpenReadAsync();
        await File.WriteAllTextAsync(files.AssetPath, truncate ? "a" : "after!");
        File.SetLastWriteTimeUtc(files.AssetPath, timestamp.AddSeconds(2));
        Assert.NotEqual(snapshot.Revision, await source.GetRevisionAsync(id));
        Assert.Throws<IOException>(() => reader.ReadByte());
        await Assert.ThrowsAsync<IOException>(async () => await reader.ReadExactlyAsync(new byte[1]));
        await Assert.ThrowsAsync<IOException>(async () => await snapshot.OpenReadAsync());
    }

    /// <summary>Nested normal directories work while file and directory links are rejected.</summary>
    /// <returns>Completion of path validation.</returns>
    [Fact]
    public async Task RejectsFileAndDirectorySymbolicLinksAsync()
    {
        using var files = new SourceDirectory();
        FileResourceSource source = files.CreateSource();
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
        Assert.Throws<IOException>(() => new FileResourceSource(new() { RootPath = Path.Combine(files.Root, "linked") }));
        Assert.Throws<IOException>(() => new FileResourceSource(new() { RootPath = Path.Combine(files.Root, "linked", "child") }));
    }

    /// <summary>A large input is opened without creating a full copy and can be read by range.</summary>
    /// <returns>Completion of direct range access checks.</returns>
    [Fact]
    public async Task LargeInputUsesDirectRangeReadsAsync()
    {
        using var files = new SourceDirectory();
        FileResourceSource source = files.CreateSource();
        const int Length = 64 * 1024 * 1024;
        await using (var file = new FileStream(files.AssetPath, FileMode.CreateNew, FileAccess.Write))
        {
            file.SetLength(Length);
            file.Position = Length - 1;
            file.WriteByte(42);
        }

        await using ResourceSourceSnapshot snapshot = await source.OpenSnapshotAsync(new("asset.bin"));
        Assert.Equal(Length, snapshot.Length);
        Assert.Equal(snapshot.Revision, await source.GetRevisionAsync(new("asset.bin")));
        await using Stream reader = await snapshot.OpenReadAsync(Length - 1, 1);
        Assert.Equal(42, reader.ReadByte());
        Assert.Equal(-1, reader.ReadByte());
        Assert.Single(Directory.GetFiles(files.Root));
        Assert.Empty(Directory.GetDirectories(files.Root));
    }

    private sealed class SourceDirectory : IDisposable
    {
        internal SourceDirectory()
        {
            Container = Path.Combine(Path.GetTempPath(), $"lumyte-resource-tests-{Guid.NewGuid():N}");
            Root = Path.Combine(Container, "root");
            Directory.CreateDirectory(Root);
        }

        internal string Container { get; }

        internal string Root { get; }

        internal string AssetPath => Path.Combine(Root, "asset.bin");

        public void Dispose() => Directory.Delete(Container, true);

        internal FileResourceSource CreateSource()
            => new(new() { RootPath = Root });
    }
}
