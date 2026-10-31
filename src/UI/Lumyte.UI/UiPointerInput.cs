using System.Numerics;

namespace Lumyte.UI;

/// <summary>A primary mouse, touch or pen transition in surface-local logical coordinates.</summary>
/// <param name="PointerId">The nonnegative host-assigned pointer identity.</param>
/// <param name="Phase">The pointer transition.</param>
/// <param name="Position">The finite surface-local position; cancellation may use zero.</param>
public sealed record UiPointerInput(long PointerId, UiPointerPhase Phase, Vector2 Position) : UiInput;
