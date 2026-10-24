namespace Lumyte.Resources.Audio.Tests;

internal sealed class RegistrationSource : IResourceSource
{
    public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>("1");
}
