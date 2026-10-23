namespace Lumyte.Resources;

internal sealed class ResourceRuntimeState(IResourceRuntimeDependency provider, object identity, ResourceRuntimeSnapshot snapshot)
{
    internal IResourceRuntimeDependency Provider { get; } = provider;

    internal object Identity { get; } = identity;

    internal ResourceRuntimeSnapshot Snapshot { get; set; } = snapshot;

    internal EventHandler? Handler { get; set; }
}
