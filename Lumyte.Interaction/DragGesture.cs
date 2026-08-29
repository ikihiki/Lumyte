namespace Lumyte.Interaction;

public sealed record DragGesture : GestureBinding
{
    public DragGesture(InteractionIntent intent, float minimumDistance = 3)
        : base(intent, GestureKind.Drag)
    {
        MinimumDistance = minimumDistance;
    }

    public float MinimumDistance { get; }

    public override GestureRecognizer CreateRecognizer() => new DragGestureRecognizer(MinimumDistance);
}
