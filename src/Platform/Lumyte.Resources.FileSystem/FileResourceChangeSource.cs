using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Lumyte.Resources.FileSystem;

/// <summary>Watches a file source recursively and reports bounded source-change hints.</summary>
/// <remarks>
/// Each enumeration owns its watcher. Overflow and ambiguous directory operations request a full
/// rescan. Notifications do not read resource content or run pipeline stages. The application must
/// own the source directory tree, as with <see cref="FileResourceSource"/>, and keep the root directory
/// itself in place throughout a subscription. Restart monitoring if the root is moved or replaced.
/// </remarks>
public sealed class FileResourceChangeSource : IResourceChangeSource
{
    private static readonly StringComparison _pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private readonly string _root;
    private readonly string _snapshotDirectory;
    private readonly string? _excludedDirectory;
    private readonly FileResourceChangeSourceOptions _options;

    /// <summary>Initializes a new instance of the <see cref="FileResourceChangeSource"/> class.</summary>
    /// <param name="sourceOptions">The same immutable configuration used by the file source.</param>
    /// <param name="options">The optional notification and native buffer limits.</param>
    public FileResourceChangeSource(FileResourceSourceOptions sourceOptions, FileResourceChangeSourceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(sourceOptions);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceOptions.RootPath);
        _options = options ?? new();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(_options.NotificationCapacity);
        ArgumentOutOfRangeException.ThrowIfLessThan(_options.InternalBufferSizeBytes, 4096);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(_options.InternalBufferSizeBytes, 65536);
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceOptions.RootPath));
        _snapshotDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceOptions.SnapshotDirectory ?? Path.GetTempPath()));
        string relativeSnapshots = Path.GetRelativePath(_root, _snapshotDirectory);
        if (relativeSnapshots != "." && IsWithinRoot(relativeSnapshots))
        {
            _excludedDirectory = _snapshotDirectory;
        }

        ValidateRoot();
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ResourceSourceChange> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateRoot();
        using var subscription = new Subscription(this);
        subscription.Start();
        while (await subscription.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (subscription.TryRead(out ResourceSourceChange change))
            {
                yield return change;
            }
        }
    }

    private static bool IsWithinRoot(string relative)
        => !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static bool ContainsLink(string path)
    {
        string? current = path;
        while (current is not null)
        {
            var information = new FileInfo(current);
            if (information.LinkTarget is not null || (information.Exists && (information.Attributes & FileAttributes.ReparsePoint) != 0))
            {
                return true;
            }

            current = Path.GetDirectoryName(current);
        }

        return false;
    }

    private void ValidateRoot()
    {
        if (ContainsLink(_root))
        {
            throw new IOException($"Symbolic links and reparse points are not supported in the resource root: '{_root}'.");
        }

        if (!Directory.Exists(_root))
        {
            throw new DirectoryNotFoundException($"The resource root '{_root}' does not exist.");
        }
    }

    private bool TryIdentify(string fullPath, out ResourceId id)
    {
        id = default;
        string path = Path.GetFullPath(fullPath);
        string relative = Path.GetRelativePath(_root, path);
        if (relative == "." || !IsWithinRoot(relative) || IsSnapshot(path))
        {
            return false;
        }

        if (ContainsLink(path))
        {
            return false;
        }

        try
        {
            id = new(relative.Replace(Path.DirectorySeparatorChar, '/'));
            return true;
        }
        catch (ArgumentException)
        {
            // Such paths cannot be addressed by a ResourceId, including editor temporary files.
            return false;
        }
    }

    private bool IsSnapshot(string path)
    {
        if (_excludedDirectory is not null && (string.Equals(path, _excludedDirectory, _pathComparison)
            || path.StartsWith(_excludedDirectory + Path.DirectorySeparatorChar, _pathComparison)))
        {
            return true;
        }

        if (!string.Equals(Path.GetDirectoryName(path), _snapshotDirectory, _pathComparison))
        {
            return false;
        }

        string name = Path.GetFileName(path);
        const string Prefix = "lumyte-resource-";
        const string Suffix = ".snapshot";
        return name.Length == Prefix.Length + 32 + Suffix.Length
            && name.StartsWith(Prefix, StringComparison.Ordinal)
            && name.EndsWith(Suffix, StringComparison.Ordinal)
            && Guid.TryParseExact(name.AsSpan(Prefix.Length, 32), "N", out _);
    }

    private sealed class Subscription : IDisposable
    {
        private readonly object _gate = new();
        private readonly FileResourceChangeSource _source;
        private readonly FileSystemWatcher _watcher;
        private readonly Channel<ResourceSourceChange> _changes;
        private bool _rescanPending;

        internal Subscription(FileResourceChangeSource source)
        {
            _source = source;
            _changes = Channel.CreateBounded<ResourceSourceChange>(new BoundedChannelOptions(source._options.NotificationCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false,
            });
            _watcher = new FileSystemWatcher(source._root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = source._options.InternalBufferSizeBytes,
            };
            _watcher.Created += OnChanged;
            _watcher.Changed += OnChanged;
            _watcher.Deleted += OnChanged;
            _watcher.Renamed += OnRenamed;
            _watcher.Error += OnError;
        }

        public void Dispose()
        {
            _watcher.Dispose();
            _changes.Writer.TryComplete();
        }

        internal void Start()
        {
            _watcher.EnableRaisingEvents = true;
            Publish(ResourceSourceChange.Rescan);
        }

        internal ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken)
            => _changes.Reader.WaitToReadAsync(cancellationToken);

        internal bool TryRead(out ResourceSourceChange change)
        {
            lock (_gate)
            {
                if (!_changes.Reader.TryRead(out change))
                {
                    return false;
                }

                if (change.RequiresRescan)
                {
                    _rescanPending = false;
                }

                return true;
            }
        }

        private void OnChanged(object sender, FileSystemEventArgs args)
        {
            try
            {
                if (!_source.TryIdentify(args.FullPath, out ResourceId id))
                {
                    return;
                }

                // Deleted directories cannot be identified after removal. Case-insensitive filesystems
                // can address the same file through ordinally different resource identifiers.
                if (args.ChangeType == WatcherChangeTypes.Deleted || Directory.Exists(args.FullPath)
                    || OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
                {
                    Publish(ResourceSourceChange.Rescan);
                }
                else
                {
                    Publish(new(id, null));
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // Event paths can disappear or become inaccessible while notifications are handled.
                Publish(ResourceSourceChange.Rescan);
            }
        }

        private void OnRenamed(object sender, RenamedEventArgs args)
        {
            try
            {
                bool hasOld = _source.TryIdentify(args.OldFullPath, out _);
                bool hasNew = _source.TryIdentify(args.FullPath, out _);
                if (!hasOld && !hasNew)
                {
                    return;
                }

                // The renamed directory can already have disappeared or been replaced by a file.
                // Revalidate descendants instead of trusting the current destination's type.
                Publish(ResourceSourceChange.Rescan);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            {
                Publish(ResourceSourceChange.Rescan);
            }
        }

        private void OnError(object sender, ErrorEventArgs args)
        {
            Exception error = args.GetException();
            if (error is InternalBufferOverflowException)
            {
                Publish(ResourceSourceChange.Rescan);
            }
            else
            {
                _changes.Writer.TryComplete(error);
            }
        }

        private void Publish(ResourceSourceChange change)
        {
            lock (_gate)
            {
                if (_rescanPending)
                {
                    return;
                }

                if (!change.RequiresRescan && _changes.Writer.TryWrite(change))
                {
                    return;
                }

                while (_changes.Reader.TryRead(out _))
                {
                }

                _rescanPending = _changes.Writer.TryWrite(ResourceSourceChange.Rescan);
            }
        }
    }
}
