# Lumyte.Resources.FileSystem

`FileResourceSource` resolves validated `ResourceId` values beneath a configured
filesystem root. Register its immutable `FileResourceSourceOptions` and
`IMemoryManager` in DI, then register the source through
`AddResources(..., resources => resources.UseSource<FileResourceSource>()...)`.

```csharp
services.AddSingleton(new FileResourceSourceOptions
{
    RootPath = Path.GetFullPath("assets"),
    SnapshotDirectory = Path.GetFullPath("asset-snapshots"),
    BufferSizeBytes = 65536,
    MemoryPool = new MemoryPoolId("resources.source"),
});
```

Opening a snapshot copies the encoded file to a temporary disk file and hashes
its content with SHA-256. It then rereads the original handle and checks its hash,
length and path metadata to detect concurrent writes. A changed or deleted input
fails acquisition; it never silently falls back to a different revision.
`GetRevisionAsync` likewise hashes and verifies the file and returns `null` only
when its path is absent before acquisition. It does not rely on timestamps as a
content revision, so same-size changes with preserved timestamps are detected.

Each acquisition uses a bounded scratch buffer charged to the configured memory
pool. There is no full-file managed allocation or decoding, but acquisition reads
the entire input twice and retains a full disk copy per live snapshot. Temporary
disk capacity is outside `IMemoryManager`'s RAM budgets. Applications requiring
cheap remote ranges or persistent shared caches should provide another
`IResourceSource` implementation.

Readers have independent cursors, support bounded ranges and keep their bytes
when the original file is replaced or deleted. Disposing the snapshot rejects
new readers and closes its delete-on-close storage handle; existing readers stay
valid and the operating system releases the underlying storage after the final
handle closes. Close readers before disposing their snapshot in normal use.

Symbolic links and other reparse points in the root's ancestry or any resource
path component are rejected before and after opening. The application must own
the root directory tree and snapshot directory. These portable path checks are
not atomic with file opening and do not form a security boundary against a
hostile process concurrently replacing directories. Absolute paths, parent
traversal and alternate data stream names are rejected by `ResourceId`.

`FileResourceChangeSource` watches the same configured root recursively using
`FileSystemWatcher`. Register it with the resource manager's reload coordinator:

```csharp
services.AddResources("assets", resources => resources
    .UseSource<FileResourceSource>()
    .AddSourcePipeline<MyResource, MyResourceStage>()
    .WatchChanges<FileResourceChangeSource>());

// Resolve within the same async DI scope as the named resource manager.
ResourceReloadService reload = scope.ServiceProvider
    .GetRequiredKeyedService<ResourceReloadService>("assets");
reload.ReloadFailed += (_, failure) => Console.Error.WriteLine(failure.Error);
reload.Start();
```

The coordinator debounces and deduplicates changes, retries transient reload
failures and publication conflicts within configured limits, and keeps the old
published resources on failure. Its `Reloaded`, `ReloadFailed`, `LastError` and
`Completion` members expose the outcome. Stop it before manually disposing the
manager; async DI scope disposal performs that ordering for scoped registrations.
See [`Lumyte.Resources`](../../Core/Lumyte.Resources/README.md) for coordinator
options and ownership. The [resource sample](../../../samples/Lumyte.Resources.Sample/README.md)
demonstrates a real filesystem edit and automatic reload.

The watcher reports normalized, source-relative IDs for file updates.
Directory changes, deletions and all renames request
revalidation of all tracked sources so moved or deleted descendants are covered.
Windows and macOS notifications likewise request full revalidation because case
aliases can differ from ordinal resource IDs. Every new subscription emits a
rescan after enabling native notifications to cover the startup interval.
Rescanning checks known source revisions; it does not import unreferenced files.

Each enumeration owns its native watcher and releases it on cancellation or
enumerator disposal. Keep the root directory itself in place throughout the
subscription; moving, deleting or replacing it requires restarting monitoring.
Native buffer overflow or a full bounded notification queue
coalesces pending hints into `ResourceSourceChange.Rescan`; changes are never
silently discarded due to those limits. Other watcher errors fail enumeration
and fault the coordinator's `Completion`. Watcher callbacks perform no hashing
or resource builds. Configure `FileResourceChangeSourceOptions` in DI to change
the notification capacity (default 256) or native buffer size (default 8192 bytes,
allowed range 4096–65536).

Snapshot scratch writes are excluded to avoid reload feedback: a dedicated
snapshot directory nested beneath the root is excluded entirely. When snapshots
share the resource root, only the reserved `lumyte-resource-{Guid:N}.snapshot`
names in that directory are excluded. A temporary directory that contains the
resource root does not exclude resource files. The root has the same symbolic-link
restrictions as `FileResourceSource`; event paths containing links are ignored
because that source cannot address them.

This adapter targets desktop filesystems. Filesystem notifications are change
hints rather than a durable event log; revisions are verified by the resource
manager before publication. An application that watches hints directly must
handle `RequiresRescan` with `ReloadAllAsync` and ordinary IDs with `ReloadAsync`.
