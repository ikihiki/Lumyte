using System.Numerics;

namespace Lumyte.UI;

/// <summary>A wheel event routed by position, independently of pointer capture and keyboard focus.</summary>
/// <param name="Position">Finite surface-local logical coordinates acquired with this event.</param>
/// <param name="Delta">Finite normalized wheel steps; positive Y is upward.</param>
public sealed record UiWheelInput(Vector2 Position, Vector2 Delta) : UiInput;
