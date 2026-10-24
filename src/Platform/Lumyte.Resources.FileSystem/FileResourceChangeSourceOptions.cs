namespace Lumyte.Resources.FileSystem;

/// <summary>Configures bounded buffering for a filesystem change subscription.</summary>
public sealed record FileResourceChangeSourceOptions
{
    /// <summary>Gets the maximum number of queued notifications before they are replaced by a full rescan hint.</summary>
    public int NotificationCapacity { get; init; } = 256;

    /// <summary>Gets the native watcher buffer size, between 4096 and 65536 bytes.</summary>
    public int InternalBufferSizeBytes { get; init; } = 8192;
}
