namespace Lumyte.Resources;

/// <summary>Produces source-change hints for a reload coordinator.</summary>
public interface IResourceChangeSource
{
    /// <summary>Watches inputs used by a loaded graph; adapters can filter native notifications against the live target set.</summary>
    /// <param name="targets">The manager-owned set, which changes as resources are loaded and reclaimed.</param>
    /// <param name="cancellationToken">Stops watching.</param>
    /// <returns>Source-change hints. The default implementation delegates to the unfiltered subscription.</returns>
    IAsyncEnumerable<ResourceSourceChange> WatchAsync(IResourceWatchTargets targets, CancellationToken cancellationToken = default)
        => WatchAsync(cancellationToken);

    /// <summary>Watches changes without automatically rebuilding resources.</summary>
    /// <param name="cancellationToken">Stops watching.</param>
    /// <returns>The observed changes, including rescan hints when individual notifications may have been lost.</returns>
    IAsyncEnumerable<ResourceSourceChange> WatchAsync(CancellationToken cancellationToken = default);
}
