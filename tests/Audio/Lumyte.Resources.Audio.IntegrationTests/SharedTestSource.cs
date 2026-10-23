namespace Lumyte.Resources.Audio.IntegrationTests;

internal sealed class SharedTestSource(TestSource source) : IResourceSource
{
    public IResourceChangeSource Changes => source.Changes;

    public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default)
        => source.OpenSnapshotAsync(id, cancellationToken);

    public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default)
        => source.GetRevisionAsync(id, cancellationToken);
}
