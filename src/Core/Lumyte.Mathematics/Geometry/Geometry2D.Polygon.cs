using System.Numerics;

namespace Lumyte.Mathematics.Geometry;

/// <summary>Provides two-dimensional geometry operations with explicit numeric and boundary policies.</summary>
public static partial class Geometry2D
{
    /// <summary>Tests containment using the nonzero winding rule and half-open vertical edge transitions.</summary>
    /// <typeparam name="T">The ordered coordinate type.</typeparam>
    /// <param name="vertices">The contour; a repeated closing vertex is optional. Empty contours contain nothing.</param>
    /// <param name="point">The query point.</param>
    /// <returns>True for interior or boundary points. One- and two-vertex contours contain only their boundary.</returns>
    public static bool ContainsPolygonClosed<T>(ReadOnlySpan<Point2D<T>> vertices, Point2D<T> point)
        where T : INumber<T> => ContainsCore(vertices, point, false);

    /// <summary>Tests containment using the even-odd rule and half-open vertical edge transitions.</summary>
    /// <typeparam name="T">The ordered coordinate type.</typeparam>
    /// <param name="vertices">The contour; a repeated closing vertex is optional. Empty contours contain nothing.</param>
    /// <param name="point">The query point.</param>
    /// <returns>True for interior or boundary points. One- and two-vertex contours contain only their boundary.</returns>
    public static bool ContainsPolygonEvenOddClosed<T>(ReadOnlySpan<Point2D<T>> vertices, Point2D<T> point)
        where T : INumber<T> => ContainsCore(vertices, point, true);

    private static bool ContainsCore<T>(ReadOnlySpan<Point2D<T>> vertices, Point2D<T> point, bool evenOdd)
        where T : INumber<T>
    {
        long winding = 0;
        for (int i = 0; i < vertices.Length; i++)
        {
            Point2D<T> start = vertices[i];
            Point2D<T> end = vertices[(i + 1) % vertices.Length];
            int orientation = GeometryScalar<T>.OrientationSign(start, end, point);
            if (orientation == 0 && point.X >= T.Min(start.X, end.X) && point.X <= T.Max(start.X, end.X)
                && point.Y >= T.Min(start.Y, end.Y) && point.Y <= T.Max(start.Y, end.Y))
            {
                return true;
            }

            if (start.Y <= point.Y && end.Y > point.Y && orientation > 0)
            {
                winding++;
            }
            else if (start.Y > point.Y && end.Y <= point.Y && orientation < 0)
            {
                winding--;
            }
        }

        return evenOdd ? (winding & 1) != 0 : winding != 0;
    }
}
