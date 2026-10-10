namespace Lumyte.Mathematics;

/// <summary>Provides scalar interpolation and polynomial progress transformations.</summary>
public static class Interpolation
{
    /// <summary>Interpolates finite scalar endpoints using double-precision weights.</summary>
    /// <param name="from">The starting value.</param>
    /// <param name="to">The ending value.</param>
    /// <param name="amount">The parameter, allowing extrapolation.</param>
    /// <returns>The interpolated value.</returns>
    public static float Linear(float from, float to, float amount) => (float)(((1d - amount) * from) + ((double)amount * to));

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
