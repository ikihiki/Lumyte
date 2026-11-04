namespace Lumyte.Platform.TextInput.Abstractions;

/// <summary>Defines text input features.</summary>
[Flags]
public enum TextInputFeatures
{
    /// <summary>None.</summary>
    None = 0,

    /// <summary>Committed Text.</summary>
    CommittedText = 1,

    /// <summary>Composition.</summary>
    Composition = 2,

    /// <summary>Geometry.</summary>
    Geometry = 4,
}
