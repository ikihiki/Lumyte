using System.Numerics;

namespace Lumyte.UI;

/// <summary>A wheel delta in logical pixels, positive toward the content end.</summary>
/// <param name="Position">The finite surface-local pointer position.</param>
/// <param name="Delta">The finite per-axis scroll delta.</param>
public sealed record UiWheelInput(Vector2 Position, Vector2 Delta) : UiInput;
