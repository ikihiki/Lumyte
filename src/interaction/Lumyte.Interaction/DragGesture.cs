namespace Lumyte.Interaction;

public sealed record DragGesture : GestureBinding
{
    public DragGesture(InteractionIntent intent, float minimumDistance = 3)
        : base(intent, GestureKind.Drag, typeof(System.Numerics.Vector2))
    {
        MinimumDistance = minimumDistance;
    }

    public float MinimumDistance { get; }

    public override GestureRecognizer CreateRecognizer() => new DragGestureRecognizer(MinimumDistance);
}
