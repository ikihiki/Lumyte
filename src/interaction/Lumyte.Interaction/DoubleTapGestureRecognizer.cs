using System.Numerics;

using Lumyte.Core.Time;
using Lumyte.Input;

namespace Lumyte.Interaction;

internal sealed class DoubleTapGestureRecognizer(
    float maximumMovement,
    TimeSpan maximumInterval) : GestureRecognizer
{
    private readonly Duration maximumDuration = Duration.FromTimeSpan(maximumInterval);
    private TimePoint? previousTime;
    private Vector2 previousPosition;

    public override GestureRecognition? Process(in GestureInput input)
    {
        if (input.Touch.Phase != TouchPhase.Ended || input.MaximumDistance > maximumMovement)
        {
            return null;
        }

        TimePoint now = input.Timestamp;
        if (previousTime is TimePoint previous
            && now - previous <= maximumDuration
            && Vector2.Distance(previousPosition, input.Touch.Position) <= maximumMovement)
        {
            previousTime = null;
            return new(GestureKind.DoubleTap, Specificity: 2, Duration: input.Duration);
        }

        previousTime = now;
        previousPosition = input.Touch.Position;
        return null;
    }

    public override void Reset() => previousTime = null;
}
