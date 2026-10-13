namespace Lumyte.Resources;

internal sealed class ResourceSourceUse(ResourceDependencyLifetime lifetime)
{
    internal ResourceDependencyLifetime Lifetime { get; set; } = lifetime;

    internal ResourceSourceSnapshot? Snapshot { get; set; }

    internal string Revision { get; set; } = string.Empty;

    internal TaskCompletionSource<ResourceSourceSnapshot> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
