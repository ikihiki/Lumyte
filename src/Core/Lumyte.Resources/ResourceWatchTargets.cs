using System.Collections.Frozen;

namespace Lumyte.Resources;

internal sealed class ResourceWatchTargets : IResourceWatchTargets
{
    private readonly object _gate = new();
    private HashSet<ResourceId> _tracked = [];
    private HashSet<ResourceId> _pending = [];
    private FrozenSet<ResourceId>? _resources = FrozenSet<ResourceId>.Empty;

    public IReadOnlySet<ResourceId> Resources
    {
        get
        {
            if (Volatile.Read(ref _resources) is { } resources)
            {
                return resources;
            }

            lock (_gate)
            {
                resources = _resources ?? _tracked.ToFrozenSet();
                Volatile.Write(ref _resources, resources);
                return resources;
            }
        }
    }

    internal void Track(ResourceId id)
    {
        lock (_gate)
        {
            if (_tracked.Add(id))
            {
                Volatile.Write(ref _resources, null);
            }
        }
    }

    internal void Set(IEnumerable<ResourceId> resources)
    {
        lock (_gate)
        {
            _pending.Clear();
            _pending.UnionWith(resources);
            if (!_tracked.SetEquals(_pending))
            {
                (_tracked, _pending) = (_pending, _tracked);
                Volatile.Write(ref _resources, null);
            }

            _pending.Clear();
        }
    }
}
