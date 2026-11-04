namespace Lumyte.UI;

/// <summary>Describes the semantic role exposed to UI tools.</summary>
public enum UiRole
{
    /// <summary>No semantic role.</summary>
    None,

    /// <summary>A semantic group.</summary>
    Group,

    /// <summary>A button.</summary>
    Button,

    /// <summary>An editable text field.</summary>
    TextBox,

    /// <summary>A redacted IME candidate presenter.</summary>
    ImeCandidates,
}
