using System.Numerics;
using Rational = Lumyte.Mathematics.Geometry.GeometryRational;

namespace Lumyte.Mathematics.Colors;

public readonly partial record struct Rgba<T>
    where T : INumber<T>
{
    private static Rational ModelZero => new(BigInteger.Zero, BigInteger.One);

    private static Rational ModelOne => new(BigInteger.One, BigInteger.One);

    private static Rational ModelTwo => new(2, BigInteger.One);

    private static Rational ModelSixty => new(60, BigInteger.One);

    private static Rational ModelFullTurn => new(360, BigInteger.One);

    /// <summary>Creates normalized straight sRGB from HSL, with hue in degrees.</summary>
    /// <typeparam name="TModel">The input numeric representation.</typeparam>
    /// <param name="hue">A finite hue, periodically reduced to [0,360).</param>
    /// <param name="saturation">Saturation in [0,1].</param>
    /// <param name="lightness">The lightness in [0,1].</param>
    /// <param name="alpha">Alpha in [0,1].</param>
    /// <returns>The color, with each component rounded only at final conversion.</returns>
    public static Rgba<T> FromHslSrgbDegrees<TModel>(TModel hue, TModel saturation, TModel lightness, TModel alpha)
        where TModel : INumber<TModel>
    {
        Rational h = ModelHue(hue, nameof(hue));
        Rational s = ModelUnit(saturation, nameof(saturation));
        Rational l = ModelUnit(lightness, nameof(lightness));
        Rational a = ModelUnit(alpha, nameof(alpha));
        Rational c = (ModelOne - ModelAbs((ModelTwo * l) - ModelOne)) * s;
        return ModelFromChroma(h, c, l - (c / ModelTwo), a);
    }

    /// <summary>Creates normalized straight sRGB from HSV, with hue in degrees.</summary>
    /// <typeparam name="TModel">The input numeric representation.</typeparam>
    /// <param name="hue">A finite hue, periodically reduced to [0,360).</param>
    /// <param name="saturation">Saturation in [0,1].</param>
    /// <param name="value">The value in [0,1].</param>
    /// <param name="alpha">Alpha in [0,1].</param>
    /// <returns>The color, with each component rounded only at final conversion.</returns>
    public static Rgba<T> FromHsvSrgbDegrees<TModel>(TModel hue, TModel saturation, TModel value, TModel alpha)
        where TModel : INumber<TModel>
    {
        Rational h = ModelHue(hue, nameof(hue));
        Rational s = ModelUnit(saturation, nameof(saturation));
        Rational v = ModelUnit(value, nameof(value));
        Rational a = ModelUnit(alpha, nameof(alpha));
        Rational c = v * s;
        return ModelFromChroma(h, c, v - c, a);
    }

    /// <summary>Converts normalized straight sRGB to HSL; achromatic hue is zero.</summary>
    /// <typeparam name="TResult">The result numeric representation.</typeparam>
    /// <returns>Hue in degrees and unit saturation, lightness, and alpha.</returns>
    public (TResult HueDegrees, TResult Saturation, TResult Lightness, TResult Alpha) ToHslSrgbDegrees<TResult>()
        where TResult : INumber<TResult>
    {
        (Rational h, Rational d, Rational maximum, Rational minimum, Rational a) = ModelToChroma();
        Rational l = (maximum + minimum) / ModelTwo;
        Rational s = d.Numerator.IsZero ? ModelZero : d / (ModelOne - ModelAbs((ModelTwo * l) - ModelOne));
        return (ModelHueResult<TResult>(h), s.To<TResult>(), l.To<TResult>(), a.To<TResult>());
    }

    /// <summary>Converts normalized straight sRGB to HSV; achromatic hue is zero.</summary>
    /// <typeparam name="TResult">The result numeric representation.</typeparam>
    /// <returns>Hue in degrees and unit saturation, value, and alpha.</returns>
    public (TResult HueDegrees, TResult Saturation, TResult Value, TResult Alpha) ToHsvSrgbDegrees<TResult>()
        where TResult : INumber<TResult>
    {
        (Rational h, Rational d, Rational maximum, _, Rational a) = ModelToChroma();
        Rational s = maximum.Numerator.IsZero ? ModelZero : d / maximum;
        return (ModelHueResult<TResult>(h), s.To<TResult>(), maximum.To<TResult>(), a.To<TResult>());
    }

    private static Rational ModelAbs(Rational value) => new(BigInteger.Abs(value.Numerator), value.Denominator);

    private static int ModelCompare(Rational left, Rational right) =>
        (left.Numerator * right.Denominator).CompareTo(right.Numerator * left.Denominator);

    private static Rational ModelModulo(Rational value, int modulus)
    {
        BigInteger period = modulus * value.Denominator;
        BigInteger remainder = value.Numerator % period;
        return new(remainder.Sign < 0 ? remainder + period : remainder, value.Denominator);
    }

    private static Rational ModelUnit<TValue>(TValue value, string parameter)
        where TValue : INumber<TValue>
    {
        if (!TValue.IsFinite(value) || value < TValue.Zero || value > TValue.One)
        {
            throw new ArgumentOutOfRangeException(parameter, "A finite value in [0,1] is required.");
        }

        return Rational.From(value);
    }

    private static Rational ModelHue<TValue>(TValue value, string parameter)
        where TValue : INumber<TValue>
    {
        if (!TValue.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameter, "A finite hue is required.");
        }

        return ModelModulo(Rational.From(value), 360);
    }

    private static TResult ModelHueResult<TResult>(Rational hue)
        where TResult : INumber<TResult>
    {
        TResult result = hue.To<TResult>();

        // Only rounding may reach 360; compare without requiring 360 to fit TResult.
        return ModelCompare(Rational.From(result), ModelFullTurn) == 0 ? TResult.Zero : result;
    }

    private static Rgba<T> ModelFromChroma(Rational hue, Rational chroma, Rational offset, Rational alpha)
    {
        Rational scaledHue = hue / ModelSixty;
        Rational x = chroma * (ModelOne - ModelAbs(ModelModulo(scaledHue, 2) - ModelOne));
        int sector = (int)(scaledHue.Numerator / scaledHue.Denominator);
        (Rational r, Rational g, Rational b) = sector switch
        {
            0 => (chroma, x, ModelZero),
            1 => (x, chroma, ModelZero),
            2 => (ModelZero, chroma, x),
            3 => (ModelZero, x, chroma),
            4 => (x, ModelZero, chroma),
            _ => (chroma, ModelZero, x),
        };
        return new((r + offset).To<T>(), (g + offset).To<T>(), (b + offset).To<T>(), alpha.To<T>());
    }

    private (Rational Hue, Rational Chroma, Rational Maximum, Rational Minimum, Rational Alpha) ModelToChroma()
    {
        Rational r = ModelUnit(R, nameof(R));
        Rational g = ModelUnit(G, nameof(G));
        Rational b = ModelUnit(B, nameof(B));
        Rational a = ModelUnit(A, nameof(A));
        Rational maximum = ModelCompare(r, g) >= 0 ? r : g;
        maximum = ModelCompare(maximum, b) >= 0 ? maximum : b;
        Rational minimum = ModelCompare(r, g) <= 0 ? r : g;
        minimum = ModelCompare(minimum, b) <= 0 ? minimum : b;
        Rational d = maximum - minimum;
        Rational h = ModelZero;
        if (!d.Numerator.IsZero)
        {
            h = ModelCompare(maximum, r) == 0 ? (g - b) / d
                : ModelCompare(maximum, g) == 0 ? ((b - r) / d) + ModelTwo
                : ((r - g) / d) + new Rational(4, BigInteger.One);
            h = ModelModulo(ModelSixty * h, 360);
        }

        return (h, d, maximum, minimum, a);
    }
}
