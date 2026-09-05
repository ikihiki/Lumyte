namespace Lumyte.Interaction;

public sealed record TapGesture : GestureBinding
{
    public TapGesture(InteractionIntent intent, float maximumMovement = 10)
        : base(intent, GestureKind.Tap, typeof(bool))
    {
        MaximumMovement = maximumMovement;
    }

    public float MaximumMovement { get; }

    public override GestureRecognizer CreateRecognizer() => new TapGestureRecognizer(MaximumMovement);
}
