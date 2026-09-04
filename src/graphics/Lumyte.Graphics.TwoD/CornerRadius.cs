namespace Lumyte.Graphics.TwoD;

public readonly record struct CornerRadius(
    float TopLeft,
    float TopRight,
    float BottomRight,
    float BottomLeft)
{
    public CornerRadius(float radius) : this(radius, radius, radius, radius) { }

    public CornerRadius Validate()
    {
        if (!IsRadius(TopLeft) || !IsRadius(TopRight)
            || !IsRadius(BottomRight) || !IsRadius(BottomLeft))
        {
            throw new ArgumentOutOfRangeException(nameof(TopLeft));
        }
        return this;
    }

    internal CornerRadius Clamp(Rect rectangle)
    {
        float maximum = MathF.Min(rectangle.Width, rectangle.Height) * 0.5f;
        return new(
            MathF.Min(TopLeft, maximum),
            MathF.Min(TopRight, maximum),
            MathF.Min(BottomRight, maximum),
            MathF.Min(BottomLeft, maximum));
    }

    private static bool IsRadius(float value) => float.IsFinite(value) && value >= 0;
}
