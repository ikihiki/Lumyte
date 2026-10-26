namespace Lumyte.Resources;

internal sealed class ResourceDependency(ResourceDependencyLifetime lifetime)
{
    internal ResourceDependencyLifetime Lifetime { get; set; } = lifetime;

    internal ResourceGeneration? Generation { get; set; }

    // Completed outside the manager gate so internal bookkeeping can continue without a ThreadPool relay.
    internal TaskCompletionSource<ResourceGeneration>? Completion { get; set; } = new();
}
