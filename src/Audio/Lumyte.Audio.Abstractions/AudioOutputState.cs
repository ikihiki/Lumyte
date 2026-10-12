namespace Lumyte.Audio.Abstractions;

/// <summary>Describes device output availability.</summary>
public enum AudioOutputState
{
    /// <summary>Suspended state.</summary>
    Suspended,

    /// <summary>Running state.</summary>
    Running,

    /// <summary>Unavailable state.</summary>
    Unavailable,
}
