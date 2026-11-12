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
    /// <remarks>IEEE built-ins evaluate nested FMA. Other types use checked native arithmetic and reject
    /// matrix coefficients that cannot round-trip through the coordinate type. For fractional output
    /// from integral inputs, select Transform&lt;T, TResult&gt; with a fractional result type.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The result is not finite.</exception>
    public static Point2D<T> Transform<T>(Point2D<T> point, Matrix3x2 transform)
        where T : INumber<T>
    {
        if (typeof(T) == typeof(float))
        {
            var result = Vector2.FusedMultiplyAdd(
                new Vector2(transform.M11, transform.M12),
                new Vector2(float.CreateChecked(point.X)),
                Vector2.FusedMultiplyAdd(new Vector2(transform.M21, transform.M22), new Vector2(float.CreateChecked(point.Y)), new Vector2(transform.M31, transform.M32)));
            return new(T.CreateChecked(result.X), T.CreateChecked(result.Y));
        }

        if (typeof(T) == typeof(double))
        {
            return Convert<T, double>(TransformIeee<double>(new(double.CreateChecked(point.X), double.CreateChecked(point.Y)), transform));
        }

        if (typeof(T) == typeof(Half))
        {
            return Convert<T, Half>(TransformIeee<Half>(new(Half.CreateChecked(point.X), Half.CreateChecked(point.Y)), transform));
        }

        return Transform(point, Coefficient<T>(transform.M11), Coefficient<T>(transform.M12), Coefficient<T>(transform.M21), Coefficient<T>(transform.M22), Coefficient<T>(transform.M31), Coefficient<T>(transform.M32));
    }

    /// <summary>Transforms numeric coordinates in an explicitly selected result precision.</summary>
    /// <typeparam name="T">The input coordinate type.</typeparam>
    /// <typeparam name="TResult">The result arithmetic type.</typeparam>
    /// <param name="point">The input point.</param>
    /// <param name="transform">The BCL matrix.</param>
    /// <returns>The transformed point.</returns>
    public static Point2D<TResult> Transform<T, TResult>(Point2D<T> point, Matrix3x2 transform)
        where T : INumber<T>
        where TResult : INumber<TResult> => Transform(new Point2D<TResult>(TResult.CreateChecked(point.X), TResult.CreateChecked(point.Y)), transform);

    /// <summary>Transforms using native coefficients and checked arithmetic, without a BCL float conversion.</summary>
    /// <typeparam name="T">The coordinate and coefficient type.</typeparam>
    /// <param name="point">The input point.</param>
    /// <param name="m11">The X-to-X coefficient.</param>
    /// <param name="m12">The X-to-Y coefficient.</param>
    /// <param name="m21">The Y-to-X coefficient.</param>
    /// <param name="m22">The Y-to-Y coefficient.</param>
    /// <param name="m31">The X translation.</param>
    /// <param name="m32">The Y translation.</param>
    /// <returns>The transformed point.</returns>
    public static Point2D<T> Transform<T>(Point2D<T> point, T m11, T m12, T m21, T m22, T m31, T m32)
        where T : INumber<T> => new(checked(checked(m11 * point.X) + checked(m21 * point.Y) + m31), checked(checked(m12 * point.X) + checked(m22 * point.Y) + m32));

    private static Point2D<T> TransformIeee<T>(Point2D<T> point, Matrix3x2 transform)
        where T : IFloatingPointIeee754<T> => new(
            T.FusedMultiplyAdd(T.CreateChecked(transform.M11), point.X, T.FusedMultiplyAdd(T.CreateChecked(transform.M21), point.Y, T.CreateChecked(transform.M31))),
            T.FusedMultiplyAdd(T.CreateChecked(transform.M12), point.X, T.FusedMultiplyAdd(T.CreateChecked(transform.M22), point.Y, T.CreateChecked(transform.M32))));

    private static Point2D<T> Convert<T, TSource>(Point2D<TSource> point)
        where T : INumber<T>
        where TSource : INumber<TSource> => new(T.CreateChecked(point.X), T.CreateChecked(point.Y));

    private static T Coefficient<T>(float value)
        where T : INumber<T>
    {
        T result = T.CreateChecked(value);
        if (float.CreateChecked(result) != value)
        {
            throw new ArithmeticException("The matrix coefficient is not representable in the selected coordinate type. Use Transform<T, TResult> or native coefficients.");
        }

        return result;
    }
}
