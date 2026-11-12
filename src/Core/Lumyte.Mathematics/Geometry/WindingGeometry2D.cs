using System.Numerics;

namespace Lumyte.Mathematics.Geometry;

/// <summary>Low-level winding predicates without boundary-inclusive polygon policy.</summary>
public static class WindingGeometry2D
{
    /// <summary>Returns one directed edge's signed winding contribution at a query coordinate.</summary>
    /// <typeparam name="T">The coordinate precision.</typeparam>
    /// <param name="startX">The edge start x.</param>
    /// <param name="startY">The edge start y.</param>
    /// <param name="endX">The edge end x.</param>
    /// <param name="endY">The edge end y.</param>
    /// <param name="pointX">The query x.</param>
    /// <param name="pointY">The query y.</param>
    /// <returns>Minus one, zero, or one; sum contributions across all contours before applying a fill rule.</returns>
    /// <remarks>
    /// Uses lower-inclusive, upper-exclusive Y transitions and strict orientation comparisons.
    /// Orientation is FMA(endX-startX, pointY-startY, -FMA(endY-startY, pointX-startX, 0)).
    /// No finite validation, scaling, epsilon, or boundary override is applied.
    /// </remarks>
    public static int Contribution<T>(T startX, T startY, T endX, T endY, T pointX, T pointY)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        T side = T.FusedMultiplyAdd(endX - startX, pointY - startY, -T.FusedMultiplyAdd(endY - startY, pointX - startX, T.Zero));
        if (startY <= pointY && endY > pointY && side > T.Zero)
        {
            return 1;
        }

        if (endY <= pointY && startY > pointY && side < T.Zero)
        {
            return -1;
        }

        return 0;
    }

    /// <summary>Accumulates winding across directed edges, including edges from multiple contours.</summary>
    /// <typeparam name="T">The coordinate precision.</typeparam>
    /// <param name="edges">The directed edges; closure is explicit.</param>
    /// <param name="point">The query point.</param>
    /// <returns>The signed winding sum. Nonzero containment is sum != 0; even-odd containment is (sum &amp; 1) != 0.</returns>
    public static int Accumulate<T>(ReadOnlySpan<Segment2D<T>> edges, Point2D<T> point)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        int winding = 0;
        foreach (Segment2D<T> edge in edges)
        {
            winding += Contribution(edge.Start.X, edge.Start.Y, edge.End.X, edge.End.Y, point.X, point.Y);
        }

        return winding;
    }
}
