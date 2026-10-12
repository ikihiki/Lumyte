using Lumyte.Audio.Abstractions;
using Lumyte.Execution;

namespace Lumyte.Audio.Execution;

/// <summary>Reference to an actual backend instance; controls are sent through generated senders.</summary>
public sealed partial class AudioClip
{
    internal AudioClip(AudioDevice owner, IAudioClip backend)
    {
        Owner = owner;
        Backend = backend;
    }

    internal AudioDevice Owner { get; }

    internal IAudioClip Backend { get; }

    [ExecutionCommand(Sender = "Commands", UseReceiver = true)]
    private AudioVoice CreateVoice(AudioBus bus)
    {
        Owner.VerifyClip(this);
        var voice = new AudioVoice(Owner, Owner.Backend.CreateVoice(Backend, bus));
        Owner.RegisterVoice(voice);
        return voice;
    }

    [ExecutionCommand(Sender = "Commands", UseReceiver = true)]
    private void DestroyClip()
    {
        Owner.VerifyClip(this);
        Backend.Dispose();
        Owner.RemoveClip(this);
    }
}
