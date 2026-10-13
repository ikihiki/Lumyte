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

This adapter targets desktop filesystems. It performs no watching or automatic
reload scheduling; pass detected resource changes to the manager's reload API.
