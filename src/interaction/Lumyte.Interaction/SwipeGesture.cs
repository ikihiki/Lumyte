namespace Lumyte.Interaction;

public sealed record SwipeGesture : GestureBinding
{
    public SwipeGesture(
        InteractionIntent intent,
        SwipeDirection direction = SwipeDirection.Any,
        float minimumDistance = 50,
        float minimumVelocity = 100,
        TimeSpan? maximumDuration = null,
        int fingerCount = 1)
        : base(intent, GestureKind.Swipe, typeof(System.Numerics.Vector2))
    {
        if (fingerCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fingerCount));
        }

        Direction = direction;
        MinimumDistance = minimumDistance;
        MinimumVelocity = minimumVelocity;
        MaximumDuration = maximumDuration ?? TimeSpan.FromMilliseconds(500);
        FingerCount = fingerCount;
    }

    public SwipeDirection Direction { get; }

    public float MinimumDistance { get; }

    public float MinimumVelocity { get; }

    public TimeSpan MaximumDuration { get; }

    public int FingerCount { get; }

    public override GestureRecognizer CreateRecognizer() => new SwipeGestureRecognizer(this);
}
