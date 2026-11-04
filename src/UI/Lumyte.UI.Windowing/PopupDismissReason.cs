namespace Lumyte.UI.Windowing;

/// <summary>The reason for a controlled model update request.</summary>
public enum PopupDismissReason
{
    /// <summary>A press outside the popup chain.</summary>
    OutsidePress,

    /// <summary>Escape or submenu back navigation.</summary>
    Escape,

    /// <summary>A menu item requested dismissal.</summary>
    ItemInvoked,

    /// <summary>The owner or anchor became unavailable.</summary>
    OwnerUnavailable,
}
