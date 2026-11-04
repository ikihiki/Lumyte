namespace Lumyte.Platform.Windows;

[Flags]
internal enum WindowsTextInputDisposition
{
    /// <summary>Pass.</summary>
    Pass = 0,

    /// <summary>Consumed.</summary>
    Consumed = 1,

    /// <summary>Translate.</summary>
    Translate = 2,

    /// <summary>Yield.</summary>
    Yield = 4,
}
