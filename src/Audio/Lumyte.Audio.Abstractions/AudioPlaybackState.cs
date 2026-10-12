namespace Lumyte.Audio.Abstractions;

/// <summary>Describes the last observed playback state.</summary>
public enum AudioPlaybackState
{
    /// <summary>Stopped state.</summary>
    Stopped,

    /// <summary>Playing state.</summary>
    Playing,

    /// <summary>Paused state.</summary>
    Paused,

    /// <summary>Completed state.</summary>
    Completed,

    /// <summary>Failed state.</summary>
    Failed,
}
