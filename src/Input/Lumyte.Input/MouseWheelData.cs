using System.Numerics;

namespace Lumyte.Input;

/// <summary>Records normalized wheel movement, retaining fractional steps.</summary>
/// <param name="Delta">The Delta value.</param>
public sealed record MouseWheelData(Vector2 Delta) : InputData;
