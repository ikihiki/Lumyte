using System.Numerics;

namespace Lumyte.Input;

/// <summary>Records one touch contact, including normalized or unavailable pressure.</summary>
/// <param name="ContactId">The ContactId value.</param>
/// <param name="Phase">The Phase value.</param>
/// <param name="Position">The position in logical pixels, with right and down positive.</param>
/// <param name="Pressure">Normalized pressure from zero to one, or null if unavailable.</param>
public sealed record TouchData(TouchContactId ContactId, TouchPhase Phase, Vector2 Position, float? Pressure) : InputData;
