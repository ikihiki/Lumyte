namespace Lumyte.Resources.Audio.Sample;

internal sealed class TestSnapshot(ResourceId id, string revision, float sample) : ResourceSourceSnapshot
{
    public override ResourceId Id => id;

    public override string Revision => revision;

    public override long? Length => sizeof(float);

    public override bool CanReadRange => false;

    public override ValueTask<Stream> OpenReadAsync(long offset = 0, long? length = null, CancellationToken cancellationToken = default)
        => ValueTask.FromResult<Stream>(new MemoryStream(BitConverter.GetBytes(sample), writable: false));

    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
