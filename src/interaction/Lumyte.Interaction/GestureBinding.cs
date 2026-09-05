namespace Lumyte.Interaction;

public abstract record GestureBinding
{
    protected GestureBinding(
        InteractionIntent intent,
        GestureKind kind,
        Type valueType)
    {
        Intent = intent ?? throw new ArgumentNullException(nameof(intent));
        Kind = kind;
        ValueType = valueType ?? throw new ArgumentNullException(nameof(valueType));
    }

    public InteractionIntent Intent { get; }

    public GestureKind Kind { get; }

    public Type ValueType { get; }

    public abstract GestureRecognizer CreateRecognizer();
}
