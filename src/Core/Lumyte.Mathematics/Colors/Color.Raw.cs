using System.Numerics;

namespace Lumyte.Mathematics.Colors;

/// <summary>Provides raw legacy color compatibility operations.</summary>
public static partial class Color
{
    /// <summary>Evaluates legacy UI interpolation, retaining float alpha subtraction and final float conversion.</summary>
    /// <param name="from">The raw first endpoint.</param>
    /// <param name="to">The raw last endpoint.</param>
    /// <param name="amount">The unvalidated interpolation parameter.</param>
    /// <returns>The legacy float result, including propagated IEEE values.</returns>
    public static Vector4 InterpolateSrgbLinearPremultipliedRaw(Vector4 from, Vector4 to, float amount)
    {
        if (amount == 0)
        {
            return from;
        }

        if (amount == 1)
        {
            return to;
        }

        double alpha = from.W + ((to.W - from.W) * (double)amount);
        if (alpha == 0)
        {
            return default;
        }

        float Channel(float first, float last) => Encode(((Decode(first) * from.W) + (((Decode(last) * to.W) - (Decode(first) * from.W)) * amount)) / alpha);
        return new(Channel(from.X, to.X), Channel(from.Y, to.Y), Channel(from.Z, to.Z), (float)alpha);
    }

    private static double Decode(float value) => value <= 0.04045f ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);

    private static float Encode(double value) => (float)(value <= 0.0031308 ? value * 12.92 : (1.055 * Math.Pow(value, 1 / 2.4)) - 0.055);
}
