using Lumyte.Resources.Embedded;
using Lumyte.Resources.FileSystem;

namespace Lumyte.Resources.Gltf.Sample;

internal sealed class SampleSource(FileResourceSource files, EmbeddedResourceSource embedded) : IResourceSource
{
    public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default)
        => Select(id).OpenSnapshotAsync(id, cancellationToken);

    public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default)
        => Select(id).GetRevisionAsync(id, cancellationToken);

    private IResourceSource Select(ResourceId id) => id.ToString().StartsWith("shaders/", StringComparison.Ordinal) ? embedded : files;
}
