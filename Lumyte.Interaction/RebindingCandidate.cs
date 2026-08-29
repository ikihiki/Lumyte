namespace Lumyte.Interaction;

public sealed record RebindingCandidate(
    InputControlDescriptor Control,
    InputValueKind ValueKind)
{
    public static RebindingCandidate From<T>(InputControl<T> control) =>
        new(InputControlDescriptor.From(control), GetValueKind<T>());

    private static InputValueKind GetValueKind<T>()
    {
        if (typeof(T) == typeof(bool))
        {
            return InputValueKind.Button;
        }

        if (typeof(T) == typeof(float))
        {
            return InputValueKind.Scalar;
        }

        if (typeof(T) == typeof(System.Numerics.Vector2))
        {
            return InputValueKind.Vector2;
        }

        throw new NotSupportedException($"Input value type {typeof(T)} is not supported.");
    }
}
