using System.Numerics;

namespace Lumyte.Graphics.TwoD;

/// <summary>A straight-alpha color in linear space.</summary>
public readonly record struct Color(float Red, float Green, float Blue, float Alpha = 1)
{
    public static Color Transparent => default;
    public static Color White => new(1, 1, 1, 1);

    public static Color FromSrgb(float red, float green, float blue, float alpha = 1)
    {
        if (!IsUnit(red) || !IsUnit(green) || !IsUnit(blue) || !IsUnit(alpha))
        {
            throw new ArgumentOutOfRangeException(nameof(red));
        }

        return new(ToLinear(red), ToLinear(green), ToLinear(blue), alpha);
    }

    public Color Validate()
    {
        if (!float.IsFinite(Red) || Red < 0
            || !float.IsFinite(Green) || Green < 0
            || !float.IsFinite(Blue) || Blue < 0
            || !IsUnit(Alpha))
        {
            throw new ArgumentOutOfRangeException(nameof(Red));
        }
        return this;
    }

    internal Vector4 Premultiplied()
    {
        Validate();
        return new(Red * Alpha, Green * Alpha, Blue * Alpha, Alpha);
    }

    private static bool IsUnit(float value) => float.IsFinite(value) && value is >= 0 and <= 1;

    private static float ToLinear(float value)
        => value <= 0.04045f
            ? value / 12.92f
            : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);
}
