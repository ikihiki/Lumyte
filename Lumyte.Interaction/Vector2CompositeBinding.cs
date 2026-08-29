using System.Numerics;

namespace Lumyte.Interaction;

public sealed class Vector2CompositeBinding : ActionBinding
{
    private readonly InputProcessor<Vector2>[] processors;

    public Vector2CompositeBinding(
        InputAction<Vector2> action,
        InputControl<bool> up,
        InputControl<bool> down,
        InputControl<bool> left,
        InputControl<bool> right,
        params ReadOnlySpan<InputProcessor<Vector2>> processors)
        : base(action, new[] { up, down, left, right })
    {
        TypedAction = action;
        Up = up;
        Down = down;
        Left = left;
        Right = right;
        this.processors = processors.ToArray();
    }

    public InputAction<Vector2> TypedAction { get; }

    public InputControl<bool> Up { get; }

    public InputControl<bool> Down { get; }

    public InputControl<bool> Left { get; }

    public InputControl<bool> Right { get; }

    public IReadOnlyList<InputProcessor<Vector2>> Processors => processors;

    public Vector2 Process(Vector2 value)
    {
        foreach (InputProcessor<Vector2> processor in processors)
        {
            value = processor(value);
        }

        return value;
    }

    internal bool TryMatch(
        InputControl<bool> control,
        InputControl<bool>? alias,
        out CompositePart part,
        out int specificity)
    {
        if (TryMatchControl(control, out part))
        {
            specificity = 2;
            return true;
        }

        if (alias is not null && TryMatchControl(alias, out part))
        {
            specificity = 1;
            return true;
        }

        part = default;
        specificity = 0;
        return false;
    }

    private bool TryMatchControl(InputControl<bool> control, out CompositePart part)
    {
        if (control == Up)
        {
            part = CompositePart.Up;
            return true;
        }

        if (control == Down)
        {
            part = CompositePart.Down;
            return true;
        }

        if (control == Left)
        {
            part = CompositePart.Left;
            return true;
        }

        if (control == Right)
        {
            part = CompositePart.Right;
            return true;
        }

        part = default;
        return false;
    }
}
