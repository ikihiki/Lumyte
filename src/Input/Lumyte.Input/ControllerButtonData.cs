namespace Lumyte.Input;

/// <summary>Represents ControllerButtonData.</summary>
/// <param name="Button">The Button value.</param>
/// <param name="IsDown">The IsDown value.</param>
public sealed record ControllerButtonData(ControllerButton Button, bool IsDown) : InputData;
