using System.Numerics;

namespace Lumyte.Mathematics.Geometry;

/// <summary>Provides two-dimensional geometry operations with explicit numeric and boundary policies.</summary>
public static partial class Geometry2D
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
    /// For IEEE built-ins orientation is FMA(endX-startX, pointY-startY, -FMA(endY-startY, pointX-startX, 0)).
    /// IEEE built-ins preserve FMA rounding; other numeric types use exact orientation without an epsilon or boundary override.
    /// </remarks>
    public static int GetWindingContributionFused<T>(T startX, T startY, T endX, T endY, T pointX, T pointY)
        where T : INumber<T>
    {
        // Preserve IEEE FMA rounding for renderer-facing callers.
        int side;
        if (typeof(T) == typeof(float))
        {
            float value = FusedSide(float.CreateChecked(startX), float.CreateChecked(startY), float.CreateChecked(endX), float.CreateChecked(endY), float.CreateChecked(pointX), float.CreateChecked(pointY));
            side = value > 0 ? 1 : value < 0 ? -1 : 0;
        }
        else if (typeof(T) == typeof(double))
        {
            double value = FusedSide(double.CreateChecked(startX), double.CreateChecked(startY), double.CreateChecked(endX), double.CreateChecked(endY), double.CreateChecked(pointX), double.CreateChecked(pointY));
            side = value > 0 ? 1 : value < 0 ? -1 : 0;
        }
        else if (typeof(T) == typeof(Half))
        {
            Half value = FusedSide(Half.CreateChecked(startX), Half.CreateChecked(startY), Half.CreateChecked(endX), Half.CreateChecked(endY), Half.CreateChecked(pointX), Half.CreateChecked(pointY));
            side = value > Half.Zero ? 1 : value < Half.Zero ? -1 : 0;
        }
        else
        {
            side = GeometryScalar<T>.OrientationSign(new(startX, startY), new(endX, endY), new(pointX, pointY));
        }

        if (startY <= pointY && endY > pointY && side > 0)
        {
            return 1;
        }

        if (endY <= pointY && startY > pointY && side < 0)
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
    public static int AccumulateWindingFused<T>(ReadOnlySpan<Segment2D<T>> edges, Point2D<T> point)
        where T : INumber<T>
    {
        int winding = 0;
        foreach (Segment2D<T> edge in edges)
        {
            winding += GetWindingContributionFused(edge.Start.X, edge.Start.Y, edge.End.X, edge.End.Y, point.X, point.Y);
        }

        return winding;
    }

    private static T FusedSide<T>(T startX, T startY, T endX, T endY, T pointX, T pointY)
        where T : IFloatingPointIeee754<T> => T.FusedMultiplyAdd(endX - startX, pointY - startY, -T.FusedMultiplyAdd(endY - startY, pointX - startX, T.Zero));
}
