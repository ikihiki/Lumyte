using System.Numerics;

namespace Lumyte.Mathematics;

/// <summary>Provides scalar and vector interpolation and polynomial progress transformations.</summary>
public static class Interpolation
{
    /// <summary>Interpolates finite scalar endpoints using double-precision intermediates.</summary>
    /// <param name="from">The starting value.</param>
    /// <param name="to">The ending value.</param>
    /// <param name="amount">The parameter, allowing extrapolation.</param>
    /// <returns>The interpolated value.</returns>
    public static float Linear(float from, float to, float amount) => amount is >= 0 and <= 1
        ? (float)(((1d - amount) * from) + ((double)amount * to))
        : (float)(from + (amount * ((double)to - from)));

    /// <summary>Interpolates vector components, preserving constant values during extrapolation.</summary>
    /// <param name="from">The starting value.</param>
    /// <param name="to">The ending value.</param>
    /// <param name="amount">The parameter, allowing extrapolation.</param>
    /// <returns>The interpolated vector.</returns>
    public static Vector2 Linear(Vector2 from, Vector2 to, float amount) => amount is >= 0 and <= 1
        ? Vector2.Lerp(from, to, amount)
        : new(Linear(from.X, to.X, amount), Linear(from.Y, to.Y, amount));

    /// <summary>Interpolates vector components, preserving constant values during extrapolation.</summary>
    /// <param name="from">The starting value.</param>
    /// <param name="to">The ending value.</param>
    /// <param name="amount">The parameter, allowing extrapolation.</param>
    /// <returns>The interpolated vector.</returns>
    public static Vector3 Linear(Vector3 from, Vector3 to, float amount) => amount is >= 0 and <= 1
        ? Vector3.Lerp(from, to, amount)
        : new(Linear(from.X, to.X, amount), Linear(from.Y, to.Y, amount), Linear(from.Z, to.Z, amount));

    /// <summary>Interpolates vector components, preserving constant values during extrapolation.</summary>
    /// <param name="from">The starting value.</param>
    /// <param name="to">The ending value.</param>
    /// <param name="amount">The parameter, allowing extrapolation.</param>
    /// <returns>The interpolated vector.</returns>
    public static Vector4 Linear(Vector4 from, Vector4 to, float amount) => amount is >= 0 and <= 1
        ? Vector4.Lerp(from, to, amount)
        : new(Linear(from.X, to.X, amount), Linear(from.Y, to.Y, amount), Linear(from.Z, to.Z, amount), Linear(from.W, to.W, amount));

    /// <summary>Applies quadratic ease-in.</summary>
    /// <param name="amount">The parameter.</param>
    /// <returns>The transformed parameter.</returns>
    public static double EaseIn(double amount) => amount * amount;

    /// <summary>Applies quadratic ease-out.</summary>
    /// <param name="amount">The parameter.</param>
    /// <returns>The transformed parameter.</returns>
    public static double EaseOut(double amount) => amount * (2 - amount);

    /// <summary>Applies symmetric quadratic easing.</summary>
    /// <param name="amount">The parameter.</param>
    /// <returns>The transformed parameter.</returns>
    public static double EaseInOut(double amount) => amount < 0.5 ? 2 * amount * amount : 1 - (2 * (1 - amount) * (1 - amount));
}
