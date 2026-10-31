namespace Lumyte.UI;

/// <summary>Notifies one widget that surface-local focus was gained or lost.</summary>
/// <param name="IsFocused">Whether the widget now has focus.</param>
public sealed record UiFocusChangedInput(bool IsFocused) : UiInput;
