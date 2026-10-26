namespace Lumyte.Resources;

/// <summary>Connects source change notifications to the shared runtime rebuild pipeline.</summary>
public sealed partial class ResourceManager
{
    /// <summary>Rebuilds the graph affected by changed input revisions and publishes it atomically.</summary>
    /// <param name="changedSources">Source identifiers to recheck; notifications are only hints.</param>
    /// <param name="cancellationToken">Cancels candidate construction before publication.</param>
    /// <returns>The publication outcome.</returns>
    internal ValueTask<ResourceReloadResult> ReloadAsync(IReadOnlyCollection<ResourceId> changedSources, CancellationToken cancellationToken = default)
    {
        foreach (ResourceId id in changedSources)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id.Value);
        }

        return ReloadCoreAsync(changedSources, [], cancellationToken);
    }

    /// <summary>Rechecks all tracked input revisions and atomically rebuilds the graph affected by changes.</summary>
    /// <remarks>Includes provenance of trimmed build-only inputs. Resources that have never been loaded are not discovered or built.</remarks>
    /// <param name="cancellationToken">Cancels candidate construction before publication.</param>
    /// <returns>The publication outcome.</returns>
    internal ValueTask<ResourceReloadResult> ReloadAllAsync(CancellationToken cancellationToken = default)
        => ReloadCoreAsync([], [], cancellationToken, recheckAllSources: true);

    internal ValueTask<ResourceReloadResult> ReloadFromChangesAsync(IReadOnlyCollection<ResourceId> changes, bool rescan, CancellationToken cancellationToken)
        => ReloadCoreAsync(changes, [], cancellationToken, recheckAllSources: rescan, automatic: true);
}
