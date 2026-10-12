using Lumyte.Audio.Abstractions;

namespace Lumyte.Audio.Execution;

/// <summary>Reference to an actual backend instance; controls are sent through generated senders.</summary>
public sealed class AudioClip
{
    internal AudioClip(AudioCommands owner, IAudioClip backend)
    {
        Owner = owner;
        Backend = backend;
    }

    internal AudioCommands Owner { get; }

    internal IAudioClip Backend { get; }
}
