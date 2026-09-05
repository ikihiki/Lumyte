using System.Numerics;

namespace Lumyte.Graphics.TwoD;

public readonly record struct Brush
{
    public Brush(Color color)
    {
        Kind = BrushKind.Solid;
        Color = color.Validate();
        SecondaryColor = Color;
    }

    private Brush(
        BrushKind kind,
        Color color,
        Color secondaryColor,
        Vector2 start,
        Vector2 end)
    {
        Kind = kind;
        Color = color;
        SecondaryColor = secondaryColor;
        Start = start;
        End = end;
    }

    public BrushKind Kind { get; }
    public Color Color { get; }
    public Color SecondaryColor { get; }
    public Vector2 Start { get; }
    public Vector2 End { get; }

    public static Brush Solid(Color color) => new(color);

    public static Brush LinearGradient(
        Vector2 start,
        Vector2 end,
        Color startColor,
        Color endColor)
    {
        ValidatePoint(start, nameof(start));
        ValidatePoint(end, nameof(end));
        if (Vector2.DistanceSquared(start, end) <= float.Epsilon)
        {
            throw new ArgumentException("Linear-gradient points must differ.", nameof(end));
        }
        return new(
            BrushKind.LinearGradient,
            startColor.Validate(),
            endColor.Validate(),
            start,
            end);
    }

    public static Brush RadialGradient(
        Vector2 center,
        float radius,
        Color innerColor,
        Color outerColor)
    {
        ValidatePoint(center, nameof(center));
        if (!float.IsFinite(radius) || radius <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(radius));
        }
        return new(
            BrushKind.RadialGradient,
            innerColor.Validate(),
            outerColor.Validate(),
            center,
            new(radius, 0));
    }

    public Brush Validate()
    {
        if (!Enum.IsDefined(Kind)) { throw new ArgumentOutOfRangeException(nameof(Kind)); }
        Color.Validate();
        SecondaryColor.Validate();
        ValidatePoint(Start, nameof(Start));
        ValidatePoint(End, nameof(End));
        if (Kind == BrushKind.LinearGradient && Vector2.DistanceSquared(Start, End) <= float.Epsilon)
        {
            throw new ArgumentException("Linear-gradient points must differ.", nameof(End));
        }
        if (Kind == BrushKind.RadialGradient && End.X <= 0)
        {
            throw new ArgumentException("Radial-gradient radius must be positive.", nameof(End));
        }
        return this;
    }

    private static void ValidatePoint(Vector2 point, string parameter)
    {
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
        {
            throw new ArgumentOutOfRangeException(parameter);
        }
    }
}
