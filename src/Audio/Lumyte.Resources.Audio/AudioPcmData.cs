namespace Lumyte.Resources.Audio;

/// <summary>Completed interleaved PCM supplied by an audio decode stage.</summary>
/// <param name="format">The decoded PCM format.</param>
/// <param name="samples">Immutable samples retained until the clip copy completes.</param>
public sealed class AudioPcmData(AudioFormat format, ReadOnlyMemory<float> samples)
{
    /// <summary>Gets the decoded format.</summary>
    public AudioFormat Format { get; } = format;

    /// <summary>Gets the borrowed immutable sample storage.</summary>
    public ReadOnlyMemory<float> Samples { get; } = samples;
}
