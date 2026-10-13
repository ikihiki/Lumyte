namespace Lumyte.Resources;

/// <summary>Produces source-change hints for an application-owned reload coordinator.</summary>
public interface IResourceChangeSource
{
    /// <summary>Watches changes without automatically rebuilding resources.</summary>
    /// <param name="cancellationToken">Stops watching.</param>
    /// <returns>The observed changes; callers may debounce and deduplicate them.</returns>
    IAsyncEnumerable<ResourceSourceChange> WatchAsync(CancellationToken cancellationToken = default);
}
