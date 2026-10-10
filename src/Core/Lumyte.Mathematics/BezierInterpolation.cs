namespace Lumyte.Mathematics;

/// <summary>Provides generic cubic Bezier evaluation using De Casteljau interpolation.</summary>
public static class BezierInterpolation
{
    /// <summary>Evaluates a cubic curve with a caller-provided point interpolation rule.</summary>
    /// <typeparam name="T">The point type.</typeparam>
    /// <param name="from">The starting point.</param>
    /// <param name="control1">The first control point.</param>
    /// <param name="control2">The second control point.</param>
    /// <param name="to">The ending point.</param>
    /// <param name="amount">The parameter, allowing extrapolation.</param>
    /// <param name="interpolate">The point interpolation rule.</param>
    /// <returns>The evaluated point.</returns>
    public static T Cubic<T>(T from, T control1, T control2, T to, float amount, Func<T, T, float, T> interpolate)
    {
        ArgumentNullException.ThrowIfNull(interpolate);
        if (amount == 0)
        {
            return from;
        }

        if (amount == 1)
        {
            return to;
        }

        T a = interpolate(from, control1, amount);
        T b = interpolate(control1, control2, amount);
        T c = interpolate(control2, to, amount);
        return interpolate(interpolate(a, b, amount), interpolate(b, c, amount), amount);
    }
}
