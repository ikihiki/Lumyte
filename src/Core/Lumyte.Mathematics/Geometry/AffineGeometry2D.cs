using System.Numerics;

namespace Lumyte.Mathematics.Geometry;

/// <summary>Affine transforms with explicit coordinate precision and nested fused multiply-add evaluation.</summary>
public static class AffineGeometry2D
{
    /// <summary>Transforms a point using BCL matrix coefficients converted to the coordinate precision.</summary>
    /// <typeparam name="T">The coordinate precision.</typeparam>
    /// <param name="point">The point to transform.</param>
    /// <param name="transform">The BCL affine matrix.</param>
    /// <returns>The transformed finite point.</returns>
    /// <remarks>Each component evaluates FMA(first coefficient, x, FMA(second coefficient, y, translation)).</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The result is not finite.</exception>
    public static Point2D<T> Transform<T>(Point2D<T> point, Matrix3x2 transform)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        if (typeof(T) == typeof(float))
        {
            var result = Vector2.FusedMultiplyAdd(
                new Vector2(transform.M11, transform.M12),
                new Vector2(float.CreateChecked(point.X)),
                Vector2.FusedMultiplyAdd(new Vector2(transform.M21, transform.M22), new Vector2(float.CreateChecked(point.Y)), new Vector2(transform.M31, transform.M32)));
            return new(T.CreateChecked(result.X), T.CreateChecked(result.Y));
        }

        return new(
            T.FusedMultiplyAdd(T.CreateChecked(transform.M11), point.X, T.FusedMultiplyAdd(T.CreateChecked(transform.M21), point.Y, T.CreateChecked(transform.M31))),
            T.FusedMultiplyAdd(T.CreateChecked(transform.M12), point.X, T.FusedMultiplyAdd(T.CreateChecked(transform.M22), point.Y, T.CreateChecked(transform.M32))));
    }
}
