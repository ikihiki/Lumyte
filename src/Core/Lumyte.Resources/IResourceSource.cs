namespace Lumyte.Resources;

/// <summary>Opens observed inputs from a configured source.</summary>
public interface IResourceSource
{
    /// <summary>Opens an owned input descriptor with source-specific consistency and change detection.</summary>
    /// <param name="id">The source-relative resource identifier.</param>
    /// <param name="cancellationToken">Cancels opening the snapshot.</param>
    /// <returns>The snapshot owned by the resource manager.</returns>
    ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default);

    /// <summary>Gets the current opaque revision according to the source's change detection capabilities.</summary>
    /// <param name="id">The source-relative resource identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The current revision, or null when the resource does not exist.</returns>
    ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default);
}
