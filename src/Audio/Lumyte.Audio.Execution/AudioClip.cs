using Lumyte.Audio.Abstractions;
using Lumyte.Execution;

namespace Lumyte.Audio.Execution;

/// <summary>Reference to an actual backend instance; controls are sent through generated senders.</summary>
public sealed partial class AudioClip
{
    internal AudioClip(IAudioDevice device, IAudioClip backend)
    {
        Device = device;
        Backend = backend;
    }

    /// <summary>Gets the immutable PCM format directly from the backend.</summary>
    public AudioFormat Format => Backend.Format;

    /// <summary>Gets the immutable PCM frame count directly from the backend.</summary>
    public long FrameCount => Backend.FrameCount;

    internal IAudioDevice Device { get; }

    internal IAudioClip Backend { get; }

    [ExecutionCommand(Sender = "Commands", UseReceiver = true)]
    private AudioVoice CreateVoice(AudioBus bus) => new(Device.CreateVoice(Backend, bus));

    [ExecutionCommand(Sender = "Commands", UseReceiver = true)]
    private void DestroyClip() => Backend.Dispose();
}
