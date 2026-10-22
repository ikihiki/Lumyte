using System.Collections.Frozen;

namespace Lumyte.Resources;

internal sealed class ResourceWatchTargets : IResourceWatchTargets
{
    private FrozenSet<ResourceId> _resources = FrozenSet<ResourceId>.Empty;

    public IReadOnlySet<ResourceId> Resources => Volatile.Read(ref _resources);

    internal void Track(ResourceId id)
    {
        if (!_resources.Contains(id))
        {
            Set(_resources.Append(id));
        }
    }

    internal void Set(IEnumerable<ResourceId> resources) => Volatile.Write(ref _resources, resources.ToFrozenSet());
}
