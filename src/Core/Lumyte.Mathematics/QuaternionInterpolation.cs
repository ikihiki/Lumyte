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
    public static Quaternion Slerp(Quaternion from, Quaternion to, float amount) => Quaternion.Normalize(Quaternion.Slerp(from, to, amount));

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
