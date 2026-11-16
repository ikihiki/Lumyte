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

    /// <summary>Committed text editing through a capability host.</summary>
    Edit = 4,

    /// <summary>Desired checked state through ordinary input.</summary>
    Check = 8,

    /// <summary>Single option selection through ordinary input.</summary>
    SelectOption = 16,

    /// <summary>Finite scalar numeric input.</summary>
    SetValue = 32,

    /// <summary>Atomic ordered range input.</summary>
    SetRange = 64,
}
