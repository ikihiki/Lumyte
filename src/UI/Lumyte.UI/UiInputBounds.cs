using System.Numerics;

namespace Lumyte.UI;

internal readonly record struct UiInputBounds(double Left, double Top, double Right, double Bottom)
{
    internal static UiInputBounds From(Vector2 origin, Vector2 size) => new(origin.X, origin.Y, (double)origin.X + size.X, (double)origin.Y + size.Y);

    internal UiInputBounds Intersect(UiInputBounds other) => new(Math.Max(Left, other.Left), Math.Max(Top, other.Top), Math.Min(Right, other.Right), Math.Min(Bottom, other.Bottom));

    internal UiInputBounds Union(UiInputBounds other)
    {
        if (Left >= Right || Top >= Bottom)
        {
            return other;
        }

        if (other.Left >= other.Right || other.Top >= other.Bottom)
        {
            return this;
        }

        return new(Math.Min(Left, other.Left), Math.Min(Top, other.Top), Math.Max(Right, other.Right), Math.Max(Bottom, other.Bottom));
    }

    internal bool Contains(Vector2 position) => position.X >= Left && position.Y >= Top && position.X < Right && position.Y < Bottom;
}
