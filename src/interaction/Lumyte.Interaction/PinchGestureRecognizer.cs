using Lumyte.Input;

namespace Lumyte.Interaction;

internal sealed class PinchGestureRecognizer(float minimumScaleChange) : GestureRecognizer
{
    public override GestureRecognition? Process(in GestureInput input) =>
        input.Touch.Phase == TouchPhase.Moved
        && input.PinchScale is float scale
        && Math.Abs(scale - 1) >= minimumScaleChange
            ? new(GestureKind.Pinch, Specificity: 2, Scale: scale)
            : null;
}
