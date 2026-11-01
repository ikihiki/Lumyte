namespace Lumyte.UI;

/// <summary>Describes actions actually supported by a component, independently of its role.</summary>
[Flags]
public enum UiAutomationActions
{
    /// <summary>No action.</summary>
    None = 0,

    /// <summary>Pointer activation.</summary>
    Click = 1,

    /// <summary>Keyboard focus.</summary>
    Focus = 2,
}
