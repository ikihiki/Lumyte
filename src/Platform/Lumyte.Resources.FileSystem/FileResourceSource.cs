using System.Security.Cryptography;
using Lumyte.Memory;

namespace Lumyte.Resources.FileSystem;

/// <summary>Copies encoded files to immutable disk snapshots using bounded, budgeted scratch memory.</summary>
/// <remarks>
/// Snapshot acquisition reads the source twice to detect edits and writes one full copy to disk.
/// Application ownership of the root directory tree is required: path checks cannot atomically exclude
/// an unrelated process replacing path components while a file is opened.
/// </remarks>
public sealed class FileResourceSource : IResourceSource
{
    private readonly string _root;
    private readonly string _snapshotDirectory;
    private readonly int _bufferSize;
    private readonly MemoryPoolId _pool;
    private readonly IMemoryManager _memory;

    /// <summary>Initializes a new instance of the <see cref="FileResourceSource"/> class.</summary>
    /// <param name="options">The immutable source configuration.</param>
    /// <param name="memory">The memory manager used for bounded scratch buffers.</param>
    public FileResourceSource(FileResourceSourceOptions options, IMemoryManager memory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.RootPath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.BufferSizeBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.MemoryPool.Value);
        _root = Path.GetFullPath(options.RootPath);
        _snapshotDirectory = Path.GetFullPath(options.SnapshotDirectory ?? Path.GetTempPath());
        _bufferSize = options.BufferSizeBytes;
        _pool = options.MemoryPool;
        _memory = memory;
        RejectLinks(_root);
        if (!Directory.Exists(_root))
        {
            throw new DirectoryNotFoundException($"The resource root '{_root}' does not exist.");
        }

        Directory.CreateDirectory(_snapshotDirectory);
    }

    /// <inheritdoc />
    public async ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = Resolve(id);
        await using FileStream input = OpenInput(path);
        FileStamp initial = Inspect(path, input);
        await using IMemoryScope scope = _memory.CreateScope($"resource source: {id}");
        using IMemoryBuffer<byte> scratch = await scope.RentAsync<byte>(_pool, _bufferSize, MemoryPurpose.Build, cancellationToken).ConfigureAwait(false);
        string snapshotPath = Path.Combine(_snapshotDirectory, $"lumyte-resource-{Guid.NewGuid():N}.snapshot");
        FileStream snapshot = CreateSnapshot(snapshotPath);
        try
        {
            string revision = await CopyAndHashAsync(input, snapshot, scratch.Memory, cancellationToken).ConfigureAwait(false);
            await snapshot.FlushAsync(cancellationToken).ConfigureAwait(false);
            await VerifyAsync(path, input, initial, revision, scratch.Memory, cancellationToken).ConfigureAwait(false);
            return new FileResourceSnapshot(id, revision, initial.Length, snapshotPath, snapshot);
        }
        catch
        {
            await snapshot.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = Resolve(id);
        FileStream input;
        try
        {
            input = OpenInput(path);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }

        await using (input.ConfigureAwait(false))
        {
            FileStamp initial = Inspect(path, input);
            await using IMemoryScope scope = _memory.CreateScope($"resource revision: {id}");
            using IMemoryBuffer<byte> scratch = await scope.RentAsync<byte>(_pool, _bufferSize, MemoryPurpose.Build, cancellationToken).ConfigureAwait(false);
            string revision = await CopyAndHashAsync(input, null, scratch.Memory, cancellationToken).ConfigureAwait(false);
            await VerifyAsync(path, input, initial, revision, scratch.Memory, cancellationToken).ConfigureAwait(false);
            return revision;
        }
    }

    private static FileStream CreateSnapshot(string path)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.ReadWrite,
            Share = FileShare.Read | FileShare.Delete,
            BufferSize = 1,
            Options = FileOptions.Asynchronous | FileOptions.DeleteOnClose,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        return new FileStream(path, options);
    }

    private static FileStream OpenInput(string path)
    {
        RejectLinks(path);
        var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.Asynchronous | FileOptions.SequentialScan);
        try
        {
            RejectLinks(path);
            return input;
        }
        catch
        {
            input.Dispose();
            throw;
        }
    }

    private static FileStamp Inspect(string path, FileStream input)
    {
        RejectLinks(path);
        var information = new FileInfo(path);
        if (!information.Exists)
        {
            throw new FileNotFoundException("The resource disappeared while it was being read.", path);
        }

        return new(input.Length, information.Length, information.LastWriteTimeUtc);
    }

    private static async ValueTask VerifyAsync(string path, FileStream input, FileStamp initial, string revision, Memory<byte> scratch, CancellationToken cancellationToken)
    {
        if (initial != Inspect(path, input))
        {
            throw new IOException($"Resource '{path}' changed while its snapshot was being acquired.");
        }

        input.Position = 0;
        string verification = await CopyAndHashAsync(input, null, scratch, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(revision, verification, StringComparison.Ordinal) || initial != Inspect(path, input))
        {
            throw new IOException($"Resource '{path}' changed while its snapshot was being acquired.");
        }
    }

    private static async ValueTask<string> CopyAndHashAsync(FileStream input, FileStream? destination, Memory<byte> scratch, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        while (true)
        {
            int read = await input.ReadAsync(scratch, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return Convert.ToHexString(hash.GetHashAndReset());
            }

            hash.AppendData(scratch.Span[..read]);
            if (destination is not null)
            {
                await destination.WriteAsync(scratch[..read], cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static void RejectLinks(string path)
    {
        string? current = path;
        while (current is not null)
        {
            var information = new FileInfo(current);
            if (information.LinkTarget is not null || (information.Exists && (information.Attributes & FileAttributes.ReparsePoint) != 0))
            {
                throw new IOException($"Symbolic links and reparse points are not supported in resource paths: '{current}'.");
            }

            current = Path.GetDirectoryName(current);
        }
    }

    private string Resolve(ResourceId id)
    {
        if (id.Value is null)
        {
            throw new ArgumentException("Supply an initialized resource identifier.", nameof(id));
        }

        string path = Path.GetFullPath(Path.Combine(_root, id.Value.Replace('/', Path.DirectorySeparatorChar)));
        string relative = Path.GetRelativePath(_root, path);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new ArgumentException("The resource path must remain within its root.", nameof(id));
        }

        return path;
    }

    private readonly record struct FileStamp(long Length, long PathLength, DateTime LastWriteTime);
}
