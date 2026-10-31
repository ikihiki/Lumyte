namespace Lumyte.UI;

/// <summary>Controls consumption and bubbling of one logical input.</summary>
public enum UiInputResult
{
    /// <summary>Offers the input to the owning parent.</summary>
    Unhandled,

    /// <summary>Consumes the input and stops bubbling.</summary>
    Handled,

    /// <summary>Stops bubbling without consuming the input.</summary>
    StopPropagation,
}
