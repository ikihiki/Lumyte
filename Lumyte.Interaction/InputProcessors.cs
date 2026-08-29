using System.Numerics;

namespace Lumyte.Interaction;

public static class InputProcessors
{
    public static InputProcessor<float> DeadZone(float minimum)
    {
        ValidateDeadZone(minimum);
        return value => Math.Abs(value) <= minimum
            ? 0
            : MathF.CopySign((Math.Abs(value) - minimum) / (1 - minimum), value);
    }

    public static InputProcessor<Vector2> RadialDeadZone(float minimum)
    {
        ValidateDeadZone(minimum);
        return value =>
        {
            float length = value.Length();
            return length <= minimum
                ? Vector2.Zero
                : Vector2.Normalize(value) * Math.Min((length - minimum) / (1 - minimum), 1);
        };
    }

    public static InputProcessor<float> Scale(float scale) => value => value * scale;

    public static InputProcessor<Vector2> Scale(Vector2 scale) => value => value * scale;

    public static InputProcessor<float> Invert() => value => -value;

    public static InputProcessor<Vector2> InvertX() => value => new(-value.X, value.Y);

    public static InputProcessor<Vector2> InvertY() => value => new(value.X, -value.Y);

    private static void ValidateDeadZone(float minimum)
    {
        if (minimum is < 0 or >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(minimum));
        }
    }
}
