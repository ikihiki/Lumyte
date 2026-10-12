using Lumyte.Audio.Abstractions;

namespace Lumyte.Audio.Execution;

/// <summary>Reference to an actual backend instance; controls are sent through generated senders.</summary>
public sealed class AudioVoice
{
    internal AudioVoice(AudioCommands owner, IAudioVoice backend)
    {
        Owner = owner;
        Backend = backend;
    }

    internal AudioCommands Owner { get; }

    internal IAudioVoice Backend { get; }
}
