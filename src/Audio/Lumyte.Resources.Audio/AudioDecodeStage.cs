namespace Lumyte.Resources.Audio;

/// <summary>Reads an observed source asynchronously and decodes WAVE on the configured CPU executor.</summary>
public sealed class AudioDecodeStage : IResourceSourceStage<AudioPcmData>
{
    private const int MaxInitialCapacity = 1024 * 1024;

    /// <inheritdoc/>
    public async ValueTask<AudioPcmData> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
    {
        ResourceSourceSnapshot snapshot = await context.OpenSourceAsync(context.Address.Id, cancellationToken: cancellationToken);
        await using Stream stream = await snapshot.OpenReadAsync(cancellationToken: cancellationToken);

        // Length is a capacity hint, not a read limit. Bound speculative allocation from external metadata.
        int capacity = snapshot.Length is long length && length > 0 ? (int)Math.Min(length, MaxInitialCapacity) : 0;
        using var bytes = new MemoryStream(capacity);
        await stream.CopyToAsync(bytes, cancellationToken);
        return WaveAudioDecoder.Decode(bytes.GetBuffer().AsSpan(0, checked((int)bytes.Length)));
    }

    /// <inheritdoc/>
    public ValueTask ReleaseAsync(AudioPcmData resource) => ValueTask.CompletedTask;
}
