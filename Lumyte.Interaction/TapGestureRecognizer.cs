using Lumyte.Input;

namespace Lumyte.Interaction;

internal sealed class TapGestureRecognizer(float maximumMovement) : GestureRecognizer
{
    public override GestureRecognition? Process(in GestureInput input) =>
        input.Touch.Phase == TouchPhase.Ended && input.MaximumDistance <= maximumMovement
            ? new(GestureKind.Tap, Specificity: 1, Duration: input.Duration)
            : null;
}
