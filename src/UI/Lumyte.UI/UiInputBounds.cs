using System.Numerics;
using Lumyte.Mathematics.Geometry;

namespace Lumyte.UI;

internal readonly record struct UiInputBounds(double Left, double Top, double Right, double Bottom)
{
    internal double Width => Coordinates.Width;

    internal double Height => Coordinates.Height;

    private Bounds2D<double> Coordinates => new(Left, Top, Right, Bottom);

    internal static UiInputBounds From(Vector2 origin, Vector2 size) => From(Bounds2D<double>.FromOriginSize(origin.X, origin.Y, size.X, size.Y));

    internal UiInputBounds Intersect(UiInputBounds other) => From(Coordinates.Intersect(other.Coordinates));

    internal UiInputBounds Union(UiInputBounds other)
    {
        // Preserve the original >= empty policy: NaN edges must reach the raw enclosing arithmetic.
        if (Left >= Right || Top >= Bottom)
        {
            return other;
        }

        if (other.Left >= other.Right || other.Top >= other.Bottom)
        {
            return this;
        }

        (double Left, double Top, double Right, double Bottom) edges = Geometry2D.EncloseBoundsEdges((Left, Top, Right, Bottom), (other.Left, other.Top, other.Right, other.Bottom));
        return new(edges.Left, edges.Top, edges.Right, edges.Bottom);
    }

    internal bool Contains(Vector2 position) => Coordinates.Contains(position.X, position.Y);

    private static UiInputBounds From(Bounds2D<double> bounds) => new(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
}
