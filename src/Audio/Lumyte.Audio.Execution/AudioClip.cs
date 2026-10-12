using Lumyte.Audio.Abstractions;
using Lumyte.Execution;

namespace Lumyte.Audio.Execution;

/// <summary>A one-reference sender wrapping an actual backend clip.</summary>
/// <param name="original">The fully created original instance.</param>
public readonly partial struct AudioClip(IAudioClip original)
{
    private readonly IAudioClip _original = original ?? throw new ArgumentNullException(nameof(original));

    /// <summary>Gets the immutable PCM format.</summary>
    public AudioFormat Format => _original.Format;

    /// <summary>Gets the immutable PCM frame count.</summary>
    public long FrameCount => _original.FrameCount;

    [ExecutionCommand]
    private IAudioVoice CreateVoiceCore(AudioBus bus) => _original.Device.CreateVoice(_original, bus);

    [ExecutionCommand]
    private void DestroyClipCore() => _original.Dispose();
}
