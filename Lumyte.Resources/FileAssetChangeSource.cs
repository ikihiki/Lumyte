namespace Lumyte.Resources;

/// <summary>Converts changes below a development asset directory into file asset changes.</summary>
public sealed class FileAssetChangeSource : IAssetChangeSource, IDisposable
{
    private readonly string root;
    private readonly FileSystemWatcher watcher;
    private readonly Func<string, string> addressFactory;
    private int disposed;

    public FileAssetChangeSource(string root, string filter = "*",
        bool includeSubdirectories = true, Func<string, string>? addressFactory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(filter);
        this.root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        this.addressFactory = addressFactory ?? (static relativePath => relativePath);
        watcher = new FileSystemWatcher(this.root, filter)
        {
            IncludeSubdirectories = includeSubdirectories,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime
        };
        watcher.Changed += OnChanged;
        watcher.Created += OnChanged;
        watcher.Deleted += OnChanged;
        watcher.Renamed += OnRenamed;
        watcher.EnableRaisingEvents = true;
    }

    public event Action<AssetChange>? Changed;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }
        watcher.EnableRaisingEvents = false;
        watcher.Changed -= OnChanged;
        watcher.Created -= OnChanged;
        watcher.Deleted -= OnChanged;
        watcher.Renamed -= OnRenamed;
        watcher.Dispose();
    }

    internal void PublishChange(string fullPath)
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            return;
        }
        string relativePath = Path.GetRelativePath(root, Path.GetFullPath(fullPath));
        if (relativePath == ".."
            || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || Path.IsPathRooted(relativePath))
        {
            return;
        }

        string portablePath = relativePath.Replace(Path.DirectorySeparatorChar, '/');
        string address = addressFactory(portablePath);
        if (!string.IsNullOrWhiteSpace(address))
        {
            Changed?.Invoke(new AssetChange("file", address));
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs args) => PublishChange(args.FullPath);

    private void OnRenamed(object sender, RenamedEventArgs args)
    {
        PublishChange(args.OldFullPath);
        PublishChange(args.FullPath);
    }
}
