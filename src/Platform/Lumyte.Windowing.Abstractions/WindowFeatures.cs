namespace Lumyte.Windowing.Abstractions;

/// <summary>Defines WindowFeatures.</summary>
[Flags]
public enum WindowFeatures
{
    /// <summary>None.</summary>
    None = 0,

    /// <summary>OwnedWindow.</summary>
    OwnedWindow = 1,

    /// <summary>NonActivating.</summary>
    NonActivating = 2,

    /// <summary>AnchorPlacement.</summary>
    AnchorPlacement = 4,

    /// <summary>Minimize.</summary>
    Minimize = 8,

    /// <summary>Maximize.</summary>
    Maximize = 16,

    /// <summary>Fullscreen.</summary>
    Fullscreen = 32,
}
