namespace Lumyte.Input;

/// <summary>Represents MouseButtonData.</summary>
/// <param name="Button">The Button value.</param>
/// <param name="IsDown">The IsDown value.</param>
public sealed record MouseButtonData(MouseButton Button, bool IsDown) : InputData;
