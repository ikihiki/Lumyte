namespace Lumyte.Resources.Audio.IntegrationTests;

internal sealed class TestSource : IResourceSource
{
    public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default)
        => ValueTask.FromResult<ResourceSourceSnapshot>(new TestSnapshot(id));

    public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>("1");
}
