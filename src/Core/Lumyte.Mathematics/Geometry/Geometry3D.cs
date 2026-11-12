using System.Numerics;

namespace Lumyte.Mathematics.Geometry;

/// <summary>Provides three-dimensional geometry operations with explicit result arithmetic.</summary>
public static class Geometry3D
{
    /// <summary>Returns the Euclidean distance in the selected result type.</summary>
    /// <typeparam name="T">The input coordinate type.</typeparam>
    /// <typeparam name="TResult">The result numeric type; integral results must be exact.</typeparam>
    /// <param name="first">The first point.</param>
    /// <param name="second">The second point.</param>
    /// <returns>The distance, preserving the point value's numeric policies.</returns>
    public static TResult Distance<T, TResult>(Point3D<T> first, Point3D<T> second)
        where T : INumber<T>
        where TResult : INumber<TResult> => first.DistanceTo<TResult>(second);

    /// <summary>Returns the squared distance without requiring a square root.</summary>
    /// <typeparam name="T">The input coordinate type.</typeparam>
    /// <typeparam name="TResult">The result numeric type.</typeparam>
    /// <param name="first">The first point.</param>
    /// <param name="second">The second point.</param>
    /// <returns>The squared distance in the selected result type.</returns>
    public static TResult DistanceSquared<T, TResult>(Point3D<T> first, Point3D<T> second)
        where T : INumber<T>
        where TResult : INumber<TResult> => first.DistanceSquared<TResult>(second);

    /// <summary>Projects onto a closed segment using the segment value's numeric policies.</summary>
    /// <typeparam name="T">The input coordinate type.</typeparam>
    /// <typeparam name="TResult">The result coordinate type; integral coordinates must be exact.</typeparam>
    /// <param name="segment">The closed segment, including coincident endpoints.</param>
    /// <param name="point">The query point.</param>
    /// <returns>The closest point in the selected result type.</returns>
    public static Point3D<TResult> ClosestPointOnSegment<T, TResult>(Segment3D<T> segment, Point3D<T> point)
        where T : INumber<T>
        where TResult : INumber<TResult> => segment.ClosestPoint<TResult>(point);

    /// <summary>Encloses a nonempty sequence of points in closed axis-aligned bounds.</summary>
    /// <typeparam name="T">The coordinate type.</typeparam>
    /// <param name="points">The points to enclose.</param>
    /// <returns>The smallest enclosing bounds, including degenerate bounds.</returns>
    /// <exception cref="ArgumentException">The point sequence is empty.</exception>
    public static AxisAlignedBox3D<T> GetBounds<T>(ReadOnlySpan<Point3D<T>> points)
        where T : INumber<T> => AxisAlignedBox3D<T>.FromPoints(points);
}
