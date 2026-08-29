using System.Numerics;

using Lumyte.Core.Time;

namespace Lumyte.Interaction;

public readonly record struct GestureRecognition(
    GestureKind Kind,
    int Specificity,
    Vector2 Delta = default,
    float Scale = 1,
    Vector2 Velocity = default,
    Duration Duration = default);
