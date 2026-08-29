namespace Lumyte.Interaction;

public abstract record GestureBinding
{
    protected GestureBinding(InteractionIntent intent, GestureKind kind)
    {
        Intent = intent ?? throw new ArgumentNullException(nameof(intent));
        Kind = kind;
    }

    public InteractionIntent Intent { get; }

    public GestureKind Kind { get; }

    public abstract GestureRecognizer CreateRecognizer();
}
