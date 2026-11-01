namespace Lumyte.UI.Windowing;

/// <summary>Defines UiPopupCloseReason.</summary>
public enum UiPopupCloseReason
{
    /// <summary>Requested.</summary>
    Requested,

    /// <summary>Escape.</summary>
    Escape,

    /// <summary>OutsidePress.</summary>
    OutsidePress,

    /// <summary>OwnerUnavailable.</summary>
    OwnerUnavailable,

    /// <summary>AnchorUnavailable.</summary>
    AnchorUnavailable,

    /// <summary>Replaced.</summary>
    Replaced,

    /// <summary>PresenterLost.</summary>
    PresenterLost,

    /// <summary>OpenFailed.</summary>
    OpenFailed,

    /// <summary>Shutdown.</summary>
    Shutdown,
}
