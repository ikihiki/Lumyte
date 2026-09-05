using System.Numerics;

using Lumyte.Core.Time;
using Lumyte.Input;

namespace Lumyte.Interaction;

public readonly record struct GestureInput(
    TouchPoint Touch,
    Vector2 StartPosition,
    float MaximumDistance,
    Duration Duration,
    TimePoint Timestamp,
    float? PinchScale);
