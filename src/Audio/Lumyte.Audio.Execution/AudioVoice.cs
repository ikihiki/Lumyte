using Lumyte.Audio.Abstractions;
using Lumyte.Execution;

namespace Lumyte.Audio.Execution;

/// <summary>Reference to an actual backend instance; controls are sent through generated senders.</summary>
public sealed partial class AudioVoice
{
    internal AudioVoice(AudioDevice owner, IAudioVoice backend)
    {
        Owner = owner;
        Backend = backend;
    }

    internal AudioDevice Owner { get; }

    internal IAudioVoice Backend { get; }

    [ExecutionCommand(Sender = "Commands", UseReceiver = true)]
    private void Play()
    {
        Owner.VerifyVoice(this);
        Backend.Play();
    }

    [ExecutionCommand(Sender = "Commands", UseReceiver = true)]
    private void Pause()
    {
        Owner.VerifyVoice(this);
        Backend.Pause();
    }

    [ExecutionCommand(Sender = "Commands", UseReceiver = true)]
    private void Stop()
    {
        Owner.VerifyVoice(this);
        Backend.Stop();
    }

    [ExecutionCommand(Sender = "Commands", UseReceiver = true)]
    private void SetGain(float gain)
    {
        Owner.VerifyVoice(this);
        Backend.Gain = gain;
    }

    [ExecutionCommand(Sender = "Commands", UseReceiver = true)]
    private void SetLoop(bool loop)
    {
        Owner.VerifyVoice(this);
        Backend.Loop = loop;
    }

    [ExecutionCommand(Sender = "Commands", UseReceiver = true)]
    private AudioPlaybackState ReadState()
    {
        Owner.VerifyVoice(this);
        return Backend.State;
    }

    [ExecutionCommand(Sender = "Commands", UseReceiver = true)]
    private void Destroy()
    {
        Owner.VerifyVoice(this);
        Backend.Dispose();
        Owner.RemoveVoice(this);
    }
}
