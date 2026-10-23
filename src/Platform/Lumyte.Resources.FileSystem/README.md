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
Readers allow read, write and delete sharing. Whether replacement of an open
destination succeeds or is atomic depends on the OS and filesystem.
No historical bytes are retained. An already decoded
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

[ADR-RESOURCES-0006](../../../docs/adr/resources/RESOURCES-0006-source-access.md)を参照してください。
