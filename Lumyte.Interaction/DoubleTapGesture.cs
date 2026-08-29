namespace Lumyte.Interaction;

public sealed record DoubleTapGesture : GestureBinding
{
    public DoubleTapGesture(
        InteractionIntent intent,
        float maximumMovement = 10,
        TimeSpan? maximumInterval = null)
        : base(intent, GestureKind.DoubleTap)
    {
        MaximumMovement = maximumMovement;
        MaximumInterval = maximumInterval ?? TimeSpan.FromMilliseconds(300);
    }

    public float MaximumMovement { get; }

    public TimeSpan MaximumInterval { get; }

    public override GestureRecognizer CreateRecognizer() =>
        new DoubleTapGestureRecognizer(MaximumMovement, MaximumInterval);
}
