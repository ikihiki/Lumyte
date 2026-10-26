namespace Lumyte.Platform.Abstractions;

/// <summary>Describes clipboard API availability independently of current permissions.</summary>
[Flags]
public enum ClipboardFeatures
{
    /// <summary>No clipboard capability.</summary>
    None = 0,

    /// <summary>Unicode text reading.</summary>
    TextRead = 1,

    /// <summary>Unicode text writing.</summary>
    TextWrite = 2,

    /// <summary>Content-free change notifications.</summary>
    ChangeNotifications = 4,
}
