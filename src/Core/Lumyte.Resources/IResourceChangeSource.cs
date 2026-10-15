namespace Lumyte.Resources;

/// <summary>Produces source-change hints for a reload coordinator.</summary>
public interface IResourceChangeSource
{
    /// <summary>Watches changes without automatically rebuilding resources.</summary>
    /// <param name="cancellationToken">Stops watching.</param>
    /// <returns>The observed changes, including rescan hints when individual notifications may have been lost.</returns>
    IAsyncEnumerable<ResourceSourceChange> WatchAsync(CancellationToken cancellationToken = default);
}
