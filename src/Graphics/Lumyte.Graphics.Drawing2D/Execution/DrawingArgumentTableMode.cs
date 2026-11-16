namespace Lumyte.Graphics.Drawing2D;

/// <summary>Controls cross-draw argument table sharing for direct drawing preparation.</summary>
public enum DrawingArgumentTableMode
{
    /// <summary>Uses the device performance hint; unknown devices retain sharing.</summary>
    Auto,

    /// <summary>Allows compatible draws to share tables.</summary>
    Shared,

    /// <summary>Keeps independent draws in separate tables.</summary>
    DrawLocal,
}
