namespace Lumyte.Resources;

internal sealed class ResourceSourceUse(ResourceDependencyLifetime lifetime)
{
    internal ResourceDependencyLifetime Lifetime { get; set; } = lifetime;

    internal ResourceSourceSnapshot? Snapshot { get; set; }

    internal string Revision { get; set; } = string.Empty;

    // Completed outside the manager gate so internal bookkeeping can continue without a ThreadPool relay.
    internal TaskCompletionSource<ResourceSourceSnapshot> Completion { get; } = new();
}
