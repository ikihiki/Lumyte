using System.Collections.Frozen;
using Lumyte.Resources.Embedded;
using Lumyte.Resources.FileSystem;

namespace Lumyte.Resources.Gltf.Sample;

internal sealed class SampleSource(FileResourceSource files, EmbeddedResourceSource embedded) : IResourceSource
{
    public IResourceChangeSource Changes => new FileChanges(files.Changes);

    public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default)
        => Select(id).OpenSnapshotAsync(id, cancellationToken);

    public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default)
        => Select(id).GetRevisionAsync(id, cancellationToken);

    private static bool IsEmbedded(ResourceId id) => id.ToString().StartsWith("shaders/", StringComparison.Ordinal);

    private IResourceSource Select(ResourceId id) => IsEmbedded(id) ? embedded : files;

    private sealed class FileChanges(IResourceChangeSource changes) : IResourceChangeSource
    {
        public IAsyncEnumerable<ResourceSourceChange> WatchAsync(CancellationToken cancellationToken = default)
            => changes.WatchAsync(cancellationToken);

        public IAsyncEnumerable<ResourceSourceChange> WatchAsync(IResourceWatchTargets targets, CancellationToken cancellationToken = default)
            => changes.WatchAsync(new FileTargets(targets), cancellationToken);
    }

    private sealed class FileTargets(IResourceWatchTargets targets) : IResourceWatchTargets
    {
        public IReadOnlySet<ResourceId> Resources => targets.Resources.Where(static id => !IsEmbedded(id)).ToFrozenSet();
    }
}
