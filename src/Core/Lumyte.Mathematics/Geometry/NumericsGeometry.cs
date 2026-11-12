using System.Numerics;

namespace Lumyte.Mathematics.Geometry;

/// <summary>Provides geometry operations using BCL single-precision vectors and matrices.</summary>
public static class NumericsGeometry
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
    public static void GetTransformedBounds(Vector2 minimum, Vector2 maximum, Matrix3x2 transform, out Vector2 transformedMinimum, out Vector2 transformedMaximum)
    {
        Vector2 lower = new(float.PositiveInfinity);
        Vector2 upper = new(float.NegativeInfinity);
        Include(Vector2.Transform(minimum, transform));
        Include(Vector2.Transform(new Vector2(maximum.X, minimum.Y), transform));
        Include(Vector2.Transform(new Vector2(minimum.X, maximum.Y), transform));
        Include(Vector2.Transform(maximum, transform));
        transformedMinimum = lower;
        transformedMaximum = upper;

        void Include(Vector2 point)
        {
            lower = Vector2.Min(lower, point);
            upper = Vector2.Max(upper, point);
        }
    }
}
