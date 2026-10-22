# Lumyte.Resources.FileSystem

`FileResourceSource` reads validated `ResourceId` values directly beneath a
configured filesystem root. Register `FileResourceSourceOptions` in DI and use
`AddResources(..., resources => resources.UseSource<FileResourceSource>()...)`.
The source does not require `IMemoryManager`.

```csharp
services.AddSingleton(new FileResourceSourceOptions
{
    RootPath = Path.GetFullPath("assets"),
});
```

`OpenSnapshotAsync` observes file length, creation time and last-write time and
returns a descriptor. It does not read, hash or copy the payload. `GetRevisionAsync`
queries the same metadata and returns `null` for missing paths. These operations
use constant storage independent of file size. There is no temporary snapshot,
content-addressed cache or runtime file-version store.

Each reader opens the original file with its own cursor and optional bounded
range. The descriptor compares both the opened handle and current path metadata
when opening a reader and before and after each read. Detected changes fail with
`IOException`; missing inputs likewise fail. Unexpected end-of-file also fails.
Discard bytes from a failed read and restart the build or session using a new
descriptor. The reload coordinator can retry failed builds within its configured
limits. Stream sessions must be reopened by their consumer after a fault.

This is best-effort change detection, not an atomic immutable snapshot. Changes
preserving all observed metadata can go undetected, including replacements with
identical metadata. Timestamp-only edits can trigger unnecessary reloads.
Writers are not blocked; no historical bytes are retained. An already decoded
resource or GPU lease keeps its constructed data, but an old descriptor or
streaming factory cannot guarantee continued reads from the old file.

Disposing the descriptor rejects new readers. Already opened readers own their
file handles and remain usable only while the input stays unchanged. Close
readers before releasing their descriptor in normal use. Read buffers and decoded
outputs belong to the caller or Stage memory scope, rather than the source.

Symbolic links and reparse points in the root's ancestry or any resource path
component are rejected before and after opening. The application must own the
root directory tree. These portable checks are not atomic with opening and are
not a security boundary against hostile directory replacement. `ResourceId`
rejects absolute paths, parent traversal and alternate data stream names.

`FileResourceSource.Changes` provides notifications from the same root used for
stream reads. The manager subscribes automatically; the internal native watcher
is not registered independently:

```csharp
services.AddResources("assets", resources => resources
    .UseSource<FileResourceSource>()
    .AddSourcePipeline<MyResource, MyResourceStage>());

// Resolve within the same async DI scope as the named resource manager.
ResourceManager manager = scope.ServiceProvider
    .GetRequiredKeyedService<ResourceManager>("assets");
ResourceReloadService reload = manager.HotReload!;
reload.ReloadFailed += (_, failure) => Console.Error.WriteLine(failure.Error);
```

The coordinator debounces and deduplicates changes, retries transient reload
failures and publication conflicts within configured limits, and keeps the old
published resources on failure. Its `Reloaded`, `ReloadFailed`, `LastError` and
`Completion` members expose the outcome. The manager starts watching automatically
and cancels and joins its worker before releasing resources during disposal.
See [`Lumyte.Resources`](../../Core/Lumyte.Resources/README.md) for coordinator
options and ownership. The [resource sample](../../../samples/Lumyte.Resources.Sample/README.md)
demonstrates a real filesystem edit and automatic reload.

The manager passes a live, immutable input-ID set to the watcher. Only events
matching the currently loaded graph enter the notification queue; loading or
reclaiming inputs updates that set without restarting the watcher. Native
watching covers the entire root. The watcher reports normalized, source-relative
IDs for tracked file updates.
Directory changes, deletions and all renames request
revalidation of all tracked sources so moved or deleted descendants are covered.
Windows and macOS notifications likewise request full revalidation because case
aliases can differ from ordinal resource IDs. Every new subscription emits a
rescan after enabling native notifications to cover the startup interval.
Rescanning checks known source revisions; it does not import unreferenced files.

The source supplies the notification provider. Each enumeration owns its native
watcher and releases it on cancellation or
enumerator disposal. Keep the root directory itself in place throughout the
subscription; moving, deleting or replacing it requires restarting monitoring.
Native buffer overflow or a full bounded notification queue
coalesces pending hints into `ResourceSourceChange.Rescan`; changes are never
silently discarded due to those limits. Other watcher errors fail enumeration
and fault the coordinator's `Completion`. Watcher callbacks perform no hashing
or resource builds. Configure `FileResourceChangeSourceOptions` in DI to change
the notification capacity (default 256) or native buffer size (default 8192 bytes,
allowed range 4096–65536).

The source performs no scratch writes, so no snapshot directories or filenames
are reserved or excluded. The root has the same symbolic-link restrictions as
`FileResourceSource`; event paths containing links are ignored because that
source cannot address them.

This adapter targets desktop filesystems. Filesystem notifications are change
hints rather than a durable event log; revisions are verified by the resource
manager before publication. Pass the stream source to `ResourceManager` or
register it with `UseSource<FileResourceSource>()`; it supplies both input data
and matching notifications. Configure batching, retries or `Enabled = false`
through `ResourceReloadOptions`; public manual reload commands are not required.
