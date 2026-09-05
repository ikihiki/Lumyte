using System.Numerics;

namespace Lumyte.Interaction;

internal readonly record struct CompositeButtonState(
    bool Up = false,
    bool Down = false,
    bool Left = false,
    bool Right = false)
{
    public Vector2 Value
    {
        get
        {
            var value = new Vector2(
                (Right ? 1 : 0) - (Left ? 1 : 0),
                (Up ? 1 : 0) - (Down ? 1 : 0));
            return value.LengthSquared() > 1 ? Vector2.Normalize(value) : value;
        }
    }

    public CompositeButtonState With(CompositePart part, bool pressed) => part switch
    {
        CompositePart.Up => this with { Up = pressed },
        CompositePart.Down => this with { Down = pressed },
        CompositePart.Left => this with { Left = pressed },
        CompositePart.Right => this with { Right = pressed },
        _ => throw new ArgumentOutOfRangeException(nameof(part)),
    };
}
