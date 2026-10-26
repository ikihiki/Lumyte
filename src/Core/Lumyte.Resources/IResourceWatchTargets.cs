namespace Lumyte.Resources;

/// <summary>Exposes a changing set of input sources used by the manager's loaded resource graph.</summary>
public interface IResourceWatchTargets
{
    /// <summary>Gets an immutable snapshot of the current input identifiers.</summary>
    IReadOnlySet<ResourceId> Resources { get; }
}
