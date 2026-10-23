using Lumyte.Audio.Abstractions;
using Lumyte.Execution;

namespace Lumyte.Audio.Execution;

/// <summary>A one-reference sender wrapping an actual backend voice.</summary>
/// <param name="original">The fully created original instance.</param>
public readonly partial struct AudioVoice(IAudioVoice original)
{
    private readonly IAudioVoice _original = original ?? throw new ArgumentNullException(nameof(original));

    /// <summary>Gets the backend snapshot from any thread.</summary>
    public AudioPlaybackState State => _original.State;

    /// <summary>Gets the cached backend gain from any thread.</summary>
    public float Gain => _original.Gain;

    /// <summary>Gets a value indicating whether the cached loop setting is enabled.</summary>
    public bool Loop => _original.Loop;

    [ExecutionCommand]
    private void PlayCore() => _original.Play();

    [ExecutionCommand]
    private void PauseCore() => _original.Pause();

    [ExecutionCommand]
    private void StopCore() => _original.Stop();

    [ExecutionCommand]
    private void SetGainCore(float gain) => _original.Gain = gain;

    [ExecutionCommand]
    private void SetLoopCore(bool loop) => _original.Loop = loop;

    [ExecutionCommand]
    private void DestroyCore() => _original.Dispose();
}
