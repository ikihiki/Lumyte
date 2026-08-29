using Lumyte.Input;

namespace Lumyte.Interaction;

internal sealed class DragGestureRecognizer(float minimumDistance) : GestureRecognizer
{
    public override GestureRecognition? Process(in GestureInput input) =>
        input.Touch.Phase == TouchPhase.Ended && input.MaximumDistance >= minimumDistance
            ? new(
                GestureKind.Drag,
                Specificity: 1,
                Delta: input.Touch.Position - input.StartPosition,
                Duration: input.Duration)
            : null;
}
