namespace Lumyte.UI.Windowing;

/// <summary>Identifies a controlled close request without changing the model.</summary>
public enum UiDialogCloseRequestReason
{
    /// <summary>The ordinary content close button.</summary>
    CloseButton,

    /// <summary>A nonrepeated Escape press.</summary>
    Escape,

    /// <summary>A pointer press outside the modal content.</summary>
    OutsidePress,
}
