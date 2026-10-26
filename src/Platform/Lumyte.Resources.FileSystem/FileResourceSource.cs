namespace Lumyte.Resources.FileSystem;

/// <summary>Opens resource files directly and detects observed changes without copying or hashing payloads.</summary>
/// <remarks>
/// Length, creation time and last-write time form a metadata revision. Changes preserving those values
/// can go undetected. The application owns the root directory tree; concurrent path replacement is
/// not an atomic security boundary. This source does not retain historical file content.
/// </remarks>
public sealed class FileResourceSource
    : IResourceSource
{
    private readonly string _root;
    private readonly FileResourceChangeSource _changes;

    /// <summary>Initializes a new instance of the <see cref="FileResourceSource"/> class.</summary>
    /// <param name="options">The source root configuration.</param>
    /// <param name="changeOptions">The optional notification and native buffer limits.</param>
    public FileResourceSource(FileResourceSourceOptions options, FileResourceChangeSourceOptions? changeOptions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.RootPath);
        _root = Path.GetFullPath(options.RootPath);
        RejectLinks(_root);
        if (!Directory.Exists(_root))
        {
            throw new DirectoryNotFoundException($"The resource root '{_root}' does not exist.");
        }

        _changes = new FileResourceChangeSource(options, changeOptions);
    }

    /// <inheritdoc />
    public IResourceChangeSource Changes => _changes;

    /// <inheritdoc />
    public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = Resolve(id);
        using FileStream input = OpenInput(path);
        var stamp = FileResourceStamp.Inspect(path);
        stamp.Verify(path, input);
        return ValueTask.FromResult<ResourceSourceSnapshot>(new FileResourceSnapshot(id, stamp, path));
    }

    /// <inheritdoc />
    public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = Resolve(id);
        RejectLinks(path);
        try
        {
            return ValueTask.FromResult<string?>(FileResourceStamp.Inspect(path).Revision);
        }
        catch (FileNotFoundException)
        {
            return ValueTask.FromResult<string?>(null);
        }
        catch (DirectoryNotFoundException)
        {
            return ValueTask.FromResult<string?>(null);
        }
    }

    internal static FileStream OpenInput(string path)
    {
        RejectLinks(path);
        var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.Asynchronous | FileOptions.RandomAccess);
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
}
