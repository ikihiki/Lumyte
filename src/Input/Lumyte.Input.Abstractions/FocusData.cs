namespace Lumyte.Input.Abstractions;

/// <summary>Represents FocusData.</summary>
/// <param name="IsFocused">The IsFocused value.</param>
public sealed record FocusData(bool IsFocused) : InputData;
