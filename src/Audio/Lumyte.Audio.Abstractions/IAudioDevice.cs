namespace Lumyte.Audio.Abstractions;

/// <summary>Backend-owned audio device. Dispose children before disposing the device.</summary>
public interface IAudioDevice : IDisposable
{
    /// <summary>Gets current output availability.</summary>
    AudioOutputState OutputState { get; }

    /// <summary>Refreshes voice state snapshots on the owning thread.</summary>
    void Update();

    /// <summary>Resumes output, from a user gesture on Browser.</summary>
    /// <param name="cancellationToken">Cancels waiting without undoing a completed resume.</param>
    /// <returns>The resume operation.</returns>
    ValueTask ResumeOutputAsync(CancellationToken cancellationToken = default);

    /// <summary>Copies finite interleaved PCM into a backend-owned clip.</summary>
    /// <param name="format">Positive rate and one or two channels.</param>
    /// <param name="samples">Nonempty, channel-aligned samples.</param>
    /// <returns>The clip, owned by the caller.</returns>
    IAudioClip CreateClip(AudioFormat format, ReadOnlySpan<float> samples);

    /// <summary>Creates a stopped voice borrowing a clip from this device.</summary>
    /// <param name="clip">Live clip created by this device.</param>
    /// <param name="bus">Music or Effects; Master is not a playback destination.</param>
    /// <returns>The voice, owned by the caller.</returns>
    IAudioVoice CreateVoice(IAudioClip clip, AudioBus bus = AudioBus.Effects);

    /// <summary>Sets a finite bus gain in [0, 1], initially one.</summary>
    /// <param name="bus">Bus to update.</param>
    /// <param name="gain">Gain multiplied with voice and other bus gains.</param>
    void SetBusGain(AudioBus bus, float gain);
}
