using System.Numerics;

namespace Lumyte.Input;

/// <summary>Records a stick position with axes from minus one to one.</summary>
/// <param name="Stick">The Stick value.</param>
/// <param name="Value">The Value value.</param>
public sealed record ControllerStickData(ControllerStick Stick, Vector2 Value) : InputData;
