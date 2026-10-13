using Lumyte.Memory;

namespace Lumyte.Resources.FileSystem;

/// <summary>Configures a file source rooted in an application-owned directory tree.</summary>
public sealed record FileResourceSourceOptions
{
    /// <summary>Gets the directory containing resource files. Symbolic links in its ancestry or contents are rejected.</summary>
    public required string RootPath { get; init; }

    /// <summary>Gets the snapshot directory, or <see langword="null"/> to use the system temporary directory.</summary>
    public string? SnapshotDirectory { get; init; }

    /// <summary>Gets the positive number of scratch bytes used to copy and hash one input at a time.</summary>
    public int BufferSizeBytes { get; init; } = 65536;

    /// <summary>Gets the memory pool charged for source scratch buffers.</summary>
    public MemoryPoolId MemoryPool { get; init; } = new("resources.source");
}
