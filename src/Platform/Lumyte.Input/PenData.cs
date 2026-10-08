using System.Numerics;

namespace Lumyte.Input;

/// <summary>Records pen hover, contact, pressure, buttons and eraser state.</summary>
/// <param name="PointerId">The PointerId value.</param>
/// <param name="Phase">The Phase value.</param>
/// <param name="Position">The position in logical pixels, with right and down positive.</param>
/// <param name="Pressure">Normalized pressure from zero to one, or null if unavailable.</param>
/// <param name="IsInContact">Whether the pen tip is touching the input surface.</param>
/// <param name="Buttons">The Buttons value.</param>
/// <param name="IsEraser">Whether the eraser end is being used.</param>
public sealed record PenData(PenPointerId PointerId, PenPhase Phase, Vector2 Position, float? Pressure, bool IsInContact, PenButtons Buttons, bool IsEraser) : InputData;
