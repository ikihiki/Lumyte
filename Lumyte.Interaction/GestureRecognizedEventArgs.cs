using System.Numerics;

using Lumyte.Core.Time;

namespace Lumyte.Interaction;

public sealed class GestureRecognizedEventArgs(
    InteractionIntent intent,
    GestureKind gesture,
    Vector2 delta = default,
    float scale = 1,
    Vector2 velocity = default,
    Duration duration = default) : EventArgs
{
    public InteractionIntent Intent { get; } = intent;

    public GestureKind Gesture { get; } = gesture;

    public Vector2 Delta { get; } = delta;

    public float Scale { get; } = scale;

    public Vector2 Velocity { get; } = velocity;

    public Duration Duration { get; } = duration;
}
