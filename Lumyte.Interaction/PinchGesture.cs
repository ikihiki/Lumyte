namespace Lumyte.Interaction;

public sealed record PinchGesture : GestureBinding
{
    public PinchGesture(InteractionIntent intent, float minimumScaleChange = 0.01f)
        : base(intent, GestureKind.Pinch)
    {
        MinimumScaleChange = minimumScaleChange;
    }

    public float MinimumScaleChange { get; }

    public override GestureRecognizer CreateRecognizer() => new PinchGestureRecognizer(MinimumScaleChange);
}
