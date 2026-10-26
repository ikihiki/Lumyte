using Lumyte.Audio.Abstractions;
using Lumyte.Execution;

namespace Lumyte.Audio.Execution;

/// <summary>A one-reference sender wrapping an actual backend clip.</summary>
/// <remarks>Copies do not retain the resource. Keep the clip resource lease until every created voice has been destroyed.</remarks>
/// <param name="original">The fully created original instance.</param>
public readonly partial struct AudioClip(IAudioClip original)
{
    private readonly IAudioClip _original = original;

    /// <summary>Gets the immutable PCM format.</summary>
    public AudioFormat Format => _original.Format;

    /// <summary>Gets the immutable PCM frame count.</summary>
    public long FrameCount => _original.FrameCount;

    [ExecutionCommand]
    private AudioVoice CreateVoiceCore(AudioBus bus) => new(_original.Device.CreateVoice(_original, bus));

    [ExecutionCommand]
    private void DestroyClipCore() => _original.Dispose();
}
