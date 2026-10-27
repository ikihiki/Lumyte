using Lumyte.Audio.Abstractions;

namespace Lumyte.Audio.Execution;

/// <summary>Provides backend-compatible defaults for audio clip commands.</summary>
public static class AudioClipExtensions
{

    /// <summary>Creates a stopped voice on the Effects bus using the asynchronous naming convention.</summary>
    /// <param name="clip">The fully created clip which the voice will borrow.</param>
    /// <param name="cancellationToken">Cancels before voice creation executes.</param>
    /// <returns>The fully created voice.</returns>
    public static ValueTask<AudioVoice> CreateVoiceAsync(this AudioClip clip, CancellationToken cancellationToken = default)
        => clip.CreateVoiceAsync(AudioBus.Effects, cancellationToken);
}
