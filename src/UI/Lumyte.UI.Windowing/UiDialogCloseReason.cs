namespace Lumyte.UI.Windowing;

/// <summary>Identifies a terminal dialog lifetime outcome independently of business results.</summary>
public enum UiDialogCloseReason
{
    /// <summary>The model requested closed.</summary>
    ModelClosed,

    /// <summary>The declaration was removed or its mount identity changed.</summary>
    Removed,

    /// <summary>The logical owner became unavailable.</summary>
    OwnerClosed,

    /// <summary>The host stopped.</summary>
    Stopped,

    /// <summary>Capability, preparation or presentation failed.</summary>
    Failed,
}
