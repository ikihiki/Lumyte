namespace Lumyte.Platform.Abstractions;

/// <summary>Defines PlatformFeatures.</summary>
[Flags]
public enum PlatformFeatures
{
    /// <summary>None.</summary>
    None = 0,

    /// <summary>NativeWindows.</summary>
    NativeWindows = 1,

    /// <summary>DeviceInput.</summary>
    DeviceInput = 2,

    /// <summary>Unicode clipboard access.</summary>
    Clipboard = 4,
}
