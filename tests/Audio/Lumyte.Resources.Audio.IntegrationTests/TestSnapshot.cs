namespace Lumyte.Resources.Audio.IntegrationTests;

internal sealed class TestSnapshot(ResourceId id) : ResourceSourceSnapshot
{
    public override ResourceId Id => id;

    public override string Revision => "1";

    public override long? Length => 44 + (48000 * sizeof(float));

    public override bool CanReadRange => false;

    public override ValueTask<Stream> OpenReadAsync(long offset = 0, long? length = null, CancellationToken cancellationToken = default)
        => ValueTask.FromResult<Stream>(new MemoryStream(TestWave.Create(id.Value == "invalid" ? float.NaN : 0.25f), writable: false));

    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
