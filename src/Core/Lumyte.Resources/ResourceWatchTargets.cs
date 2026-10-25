using System.Collections.Frozen;

namespace Lumyte.Resources;

internal sealed class ResourceWatchTargets : IResourceWatchTargets
{
    private readonly object _gate = new();
    private readonly Dictionary<ResourceId, References> _references = [];
    private FrozenSet<ResourceId>? _resources = FrozenSet<ResourceId>.Empty;

    public IReadOnlySet<ResourceId> Resources
    {
        get
        {
            lock (_gate)
            {
                return _resources ??= _references.Keys.ToFrozenSet();
            }
        }
    }

    internal void Track(ResourceId id)
    {
        lock (_gate)
        {
            References references = _references.GetValueOrDefault(id);
            Set(id, references with { Builds = references.Builds + 1 });
        }
    }

    internal void CompleteBuild(IEnumerable<ResourceId> sources)
    {
        lock (_gate)
        {
            foreach (ResourceId id in sources)
            {
                References references = _references[id];
                Set(id, references with { Builds = references.Builds - 1 });
            }
        }
    }

    internal bool ReplaceMetadata(ResourceMetadata? previous, ResourceMetadata current)
    {
        lock (_gate)
        {
            bool added = false;
            foreach (ResourceId id in current.Sources.Keys)
            {
                References references = _references.GetValueOrDefault(id);
                added |= references.Metadata == 0;
                Set(id, references with { Metadata = references.Metadata + 1 });
            }

            if (previous is not null)
            {
                RemoveMetadataCore(previous);
            }

            return added;
        }
    }

    internal void RemoveMetadata(ResourceMetadata metadata)
    {
        lock (_gate)
        {
            RemoveMetadataCore(metadata);
        }
    }

    internal void Clear()
    {
        lock (_gate)
        {
            _references.Clear();
            _resources = FrozenSet<ResourceId>.Empty;
        }
    }

    private void RemoveMetadataCore(ResourceMetadata metadata)
    {
        foreach (ResourceId id in metadata.Sources.Keys)
        {
            References references = _references[id];
            Set(id, references with { Metadata = references.Metadata - 1 });
        }
    }

    private void Set(ResourceId id, References references)
    {
        if (references is { Builds: 0, Metadata: 0 })
        {
            _references.Remove(id);
            _resources = null;
        }
        else
        {
            if (!_references.ContainsKey(id))
            {
                _resources = null;
            }

            _references[id] = references;
        }
    }

    private readonly record struct References(int Builds, int Metadata);
}
