namespace Lumyte.Resources;

internal sealed class ResourceDependency(ResourceDependencyLifetime lifetime)
{
    internal ResourceDependencyLifetime Lifetime { get; set; } = lifetime;

    internal ResourceGeneration? Generation { get; set; }

    internal TaskCompletionSource<ResourceGeneration> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
