namespace Lumyte.Resources;

internal sealed class ResourceCleanup(Func<ValueTask> callback, ResourceDependencyLifetime lifetime)
{
    internal Func<ValueTask> Callback { get; } = callback;

    internal ResourceDependencyLifetime Lifetime { get; } = lifetime;

    internal bool Completed { get; set; }
}
