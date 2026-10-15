namespace Lumyte.Resources;

/// <summary>Owns access to one immutable input revision, including later range reads.</summary>
public abstract class ResourceSourceSnapshot : IAsyncDisposable
{
    /// <summary>Gets the source-relative identifier.</summary>
    public abstract ResourceId Id { get; }

    /// <summary>Gets the opaque revision shared by all streams opened from this snapshot.</summary>
    public abstract string Revision { get; }

    /// <summary>Gets the byte length, or null when determining it would require reading the input.</summary>
    public abstract long? Length { get; }

    /// <summary>Gets a value indicating whether nonzero-offset range reads are supported.</summary>
    public abstract bool CanReadRange { get; }

    /// <summary>Opens an independently owned stream fixed to this snapshot's revision.</summary>
    /// <param name="offset">The nonnegative starting byte offset.</param>
    /// <param name="length">The positive byte count, or null to read the remaining input.</param>
    /// <param name="cancellationToken">Cancels opening the stream.</param>
    /// <returns>The stream, which the caller must close before the snapshot is released.</returns>
    public abstract ValueTask<Stream> OpenReadAsync(long offset = 0, long? length = null, CancellationToken cancellationToken = default);

    /// <summary>Releases the snapshot after all its streams close; failed release must support retry.</summary>
    /// <returns>The release completion.</returns>
    public abstract ValueTask DisposeAsync();
}
