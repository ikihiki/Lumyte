using System.Numerics;

namespace Lumyte.Input.Abstractions;

/// <summary>Records a mouse position in target-relative logical pixels.</summary>
/// <param name="Position">The position in logical pixels, with right and down positive.</param>
public sealed record MouseMoveData(Vector2 Position) : InputData;
