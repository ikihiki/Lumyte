using System.Numerics;

namespace Lumyte.Graphics.TwoD;

public readonly record struct Rect(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;

    public Rect Validate()
    {
        if (!float.IsFinite(X) || !float.IsFinite(Y)
            || !float.IsFinite(Width) || !float.IsFinite(Height)
            || Width <= 0 || Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Width));
        }
        return this;
    }

    internal Rect TransformBounds(Matrix3x2 transform)
    {
        Vector2 first = Vector2.Transform(new(X, Y), transform);
        Vector2 second = Vector2.Transform(new(Right, Y), transform);
        Vector2 third = Vector2.Transform(new(Right, Bottom), transform);
        Vector2 fourth = Vector2.Transform(new(X, Bottom), transform);
        float left = MathF.Min(MathF.Min(first.X, second.X), MathF.Min(third.X, fourth.X));
        float top = MathF.Min(MathF.Min(first.Y, second.Y), MathF.Min(third.Y, fourth.Y));
        float right = MathF.Max(MathF.Max(first.X, second.X), MathF.Max(third.X, fourth.X));
        float bottom = MathF.Max(MathF.Max(first.Y, second.Y), MathF.Max(third.Y, fourth.Y));
        return new(left, top, right - left, bottom - top);
    }

    internal static Rect? Intersect(Rect left, Rect right)
    {
        float x = MathF.Max(left.X, right.X);
        float y = MathF.Max(left.Y, right.Y);
        float maximumX = MathF.Min(left.Right, right.Right);
        float maximumY = MathF.Min(left.Bottom, right.Bottom);
        return maximumX <= x || maximumY <= y ? null : new(x, y, maximumX - x, maximumY - y);
    }
}
