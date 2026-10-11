using System.Numerics;

namespace Lumyte.Mathematics;

/// <summary>Provides rotation interpolation and double-precision normalization.</summary>
public static class QuaternionInterpolation
{
    /// <summary>Interpolates rotations along the shortest spherical arc.</summary>
    /// <param name="from">The starting unit quaternion.</param>
    /// <param name="to">The ending unit quaternion.</param>
    /// <param name="amount">The interpolation parameter, allowing extrapolation.</param>
    /// <returns>The normalized interpolated rotation.</returns>
    public static Quaternion Slerp(Quaternion from, Quaternion to, float amount)
    {
        if (amount is >= 0 and <= 1)
        {
            return Quaternion.Normalize(Quaternion.Slerp(from, to, amount));
        }

        // The usual near-equal linear approximation cancels at large amounts. Use
        // the relative rotation instead, whose angle remains meaningful for extrapolation.
        double x = (((double)from.W * to.X) - ((double)from.X * to.W)) + (((double)from.Z * to.Y) - ((double)from.Y * to.Z));
        double y = (((double)from.W * to.Y) - ((double)from.Y * to.W)) + (((double)from.X * to.Z) - ((double)from.Z * to.X));
        double z = (((double)from.W * to.Z) - ((double)from.Z * to.W)) + (((double)from.Y * to.X) - ((double)from.X * to.Y));
        double w = ((double)from.X * to.X) + ((double)from.Y * to.Y) + ((double)from.Z * to.Z) + ((double)from.W * to.W);
        if (w < 0)
        {
            x = -x;
            y = -y;
            z = -z;
            w = -w;
        }

        double length = Math.Sqrt((x * x) + (y * y) + (z * z));
        if (length == 0)
        {
            return Normalize(from.X, from.Y, from.Z, from.W);
        }

        double angle = amount * Math.Atan2(length, w);
        double scale = Math.Sin(angle) / length;
        x *= scale;
        y *= scale;
        z *= scale;
        w = Math.Cos(angle);
        return Normalize(
            (from.W * x) + (from.X * w) + (from.Y * z) - (from.Z * y),
            (from.W * y) - (from.X * z) + (from.Y * w) + (from.Z * x),
            (from.W * z) + (from.X * y) - (from.Y * x) + (from.Z * w),
            (from.W * w) - (from.X * x) - (from.Y * y) - (from.Z * z));
    }

    /// <summary>Normalizes finite double components without premature float conversion.</summary>
    /// <param name="x">The x component.</param>
    /// <param name="y">The y component.</param>
    /// <param name="z">The z component.</param>
    /// <param name="w">The w component.</param>
    /// <returns>The unit quaternion.</returns>
    public static Quaternion Normalize(double x, double y, double z, double w)
    {
        double scale = Math.Max(Math.Max(Math.Abs(x), Math.Abs(y)), Math.Max(Math.Abs(z), Math.Abs(w)));
        if (!double.IsFinite(scale) || scale == 0)
        {
            throw new InvalidOperationException("A rotation must have finite nonzero components.");
        }

        x /= scale;
        y /= scale;
        z /= scale;
        w /= scale;
        double length = Math.Sqrt((x * x) + (y * y) + (z * z) + (w * w));
        return new Quaternion((float)(x / length), (float)(y / length), (float)(z / length), (float)(w / length));
    }
}
