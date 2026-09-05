using System.Numerics;

namespace Lumyte.Input;

public readonly record struct TouchPoint(
    long Id,
    Vector2 Position,
    Vector2 Delta,
    TouchPhase Phase,
    float? Pressure);
