using System.Numerics;

using Lumyte.Core.Time;
using Lumyte.Input;

namespace Lumyte.Interaction;

internal sealed class SwipeGestureRecognizer(SwipeGesture gesture) : GestureRecognizer
{
    private readonly Duration maximumDuration = Duration.FromTimeSpan(gesture.MaximumDuration);

    public override GestureRecognition? Process(in GestureInput input)
    {
        if (input.Touch.Phase != TouchPhase.Ended
            || gesture.FingerCount != 1
            || input.MaximumDistance < gesture.MinimumDistance
            || input.Duration > maximumDuration)
        {
            return null;
        }

        Vector2 delta = input.Touch.Position - input.StartPosition;
        float seconds = (float)input.Duration.TotalSeconds;
        Vector2 velocity = seconds > 0 ? delta / seconds : Vector2.Zero;
        SwipeDirection direction = GetDirection(delta);
        if (velocity.Length() < gesture.MinimumVelocity
            || (gesture.Direction != SwipeDirection.Any && gesture.Direction != direction))
        {
            return null;
        }

        return new(
            GestureKind.Swipe,
            Specificity: 2,
            Delta: delta,
            Velocity: velocity,
            Duration: input.Duration);
    }

    private static SwipeDirection GetDirection(Vector2 delta) =>
        Math.Abs(delta.X) >= Math.Abs(delta.Y)
            ? delta.X >= 0 ? SwipeDirection.Right : SwipeDirection.Left
            : delta.Y >= 0 ? SwipeDirection.Down : SwipeDirection.Up;
}
