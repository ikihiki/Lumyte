namespace Lumyte.Resources.Graphics.Tests;

internal sealed class TestSource : IResourceSource
{
    public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>(null);
}
