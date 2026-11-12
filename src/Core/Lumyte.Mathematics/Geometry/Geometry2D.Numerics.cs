using System.Numerics;

namespace Lumyte.Mathematics.Geometry;

/// <summary>Provides two-dimensional geometry operations with explicit numeric and boundary policies.</summary>
public static partial class Geometry2D
{
    /// <summary>Computes axis-aligned bounds of all four transformed rectangle corners without allocating.</summary>
    /// <param name="minimum">The first corner of the input rectangle.</param>
    /// <param name="maximum">The opposite corner; components need not be ordered.</param>
    /// <param name="transform">The affine transform applied to each corner.</param>
    /// <param name="transformedMinimum">The component-wise minimum of the transformed corners.</param>
    /// <param name="transformedMaximum">The component-wise maximum of the transformed corners.</param>
    /// <remarks>
    /// Corners are visited in minimum, opposite-X, opposite-Y, maximum order using
    /// <see cref="Vector2.Transform(Vector2, Matrix3x2)"/> and BCL minimum/maximum semantics.
    /// Non-finite inputs and results are not rejected; callers requiring finite bounds must validate them.
    /// </remarks>
    public static void GetTransformedBoundsBcl(Vector2 minimum, Vector2 maximum, Matrix3x2 transform, out Vector2 transformedMinimum, out Vector2 transformedMaximum)
    {
        Vector2 lower = new(float.PositiveInfinity);
        Vector2 upper = new(float.NegativeInfinity);
        Geometry2D.IncludeInBoundsBcl(Vector2.Transform(minimum, transform), ref lower, ref upper);
        Geometry2D.IncludeInBoundsBcl(Vector2.Transform(new Vector2(maximum.X, minimum.Y), transform), ref lower, ref upper);
        Geometry2D.IncludeInBoundsBcl(Vector2.Transform(new Vector2(minimum.X, maximum.Y), transform), ref lower, ref upper);
        Geometry2D.IncludeInBoundsBcl(Vector2.Transform(maximum, transform), ref lower, ref upper);
        transformedMinimum = lower;
        transformedMaximum = upper;
    }

    /// <summary>Transforms each corner, then scales each result before reducing bounds.</summary>
    /// <remarks>The scale is not folded into the matrix, preserving single-precision rounding.</remarks>
    /// <param name="minimum">The component-wise minimum.</param>
    /// <param name="maximum">The component-wise maximum.</param>
    /// <param name="transform">The affine transform.</param>
    /// <param name="scale">The scalar applied after each transform.</param>
    /// <param name="transformedMinimum">The minimum transformed corner.</param>
    /// <param name="transformedMaximum">The maximum transformed corner.</param>
    public static void GetTransformedBoundsBcl(Vector2 minimum, Vector2 maximum, Matrix3x2 transform, float scale, out Vector2 transformedMinimum, out Vector2 transformedMaximum)
    {
        GetBoundsBcl(
            Vector2.Transform(minimum, transform) * scale,
            Vector2.Transform(new Vector2(maximum.X, minimum.Y), transform) * scale,
            Vector2.Transform(new Vector2(minimum.X, maximum.Y), transform) * scale,
            Vector2.Transform(maximum, transform) * scale,
            out transformedMinimum,
            out transformedMaximum);
    }

    /// <summary>Reduces four evaluated points using pairwise BCL minimum/maximum semantics.</summary>
    /// <param name="a">The first point.</param>
    /// <param name="b">The second point.</param>
    /// <param name="c">The third point.</param>
    /// <param name="d">The fourth point.</param>
    /// <param name="minimum">The component-wise minimum.</param>
    /// <param name="maximum">The component-wise maximum.</param>
    public static void GetBoundsBcl(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out Vector2 minimum, out Vector2 maximum)
    {
        minimum = Vector2.Min(Vector2.Min(a, b), Vector2.Min(c, d));
        maximum = Vector2.Max(Vector2.Max(a, b), Vector2.Max(c, d));
    }
}
