using Lumyte.Audio.Abstractions;
using Lumyte.Execution;

namespace Lumyte.Audio.Execution;

/// <summary>Reference to an actual backend instance; controls are sent through generated senders.</summary>
public sealed partial class AudioVoice
{
    internal AudioVoice(IAudioVoice backend)
    {
        Backend = backend;
    }

    /// <summary>Gets the backend snapshot from any thread without submitting work.</summary>
    public AudioPlaybackState State => Backend.State;

    /// <summary>Gets the cached backend gain from any thread.</summary>
    public float Gain => Backend.Gain;

    /// <summary>Gets a value indicating whether the cached backend loop setting is enabled, from any thread.</summary>
    public bool Loop => Backend.Loop;

    internal IAudioVoice Backend { get; }

    [ExecutionCommand(Sender = "Commands", UseReceiver = true)]
    private void Play() => Backend.Play();

    [ExecutionCommand(Sender = "Commands", UseReceiver = true)]
    private void Pause() => Backend.Pause();

    [ExecutionCommand(Sender = "Commands", UseReceiver = true)]
    private void Stop() => Backend.Stop();

    [ExecutionCommand(Sender = "Commands", UseReceiver = true)]
    private void SetGain(float gain) => Backend.Gain = gain;

    [ExecutionCommand(Sender = "Commands", UseReceiver = true)]
    private void SetLoop(bool loop) => Backend.Loop = loop;

    [ExecutionCommand(Sender = "Commands", UseReceiver = true)]
    private void Destroy() => Backend.Dispose();
}
