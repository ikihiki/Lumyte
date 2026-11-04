using System.Runtime.CompilerServices;
using Lumyte.Animation;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.UI;

/// <summary>Provides UI value interpolation without changing the model's color or nullable contracts.</summary>
public static class UiAnimationInterpolators
{
    /// <summary>Gets the sRGB straight-alpha interpolator using linear premultiplied intermediate colors.</summary>
    public static IAnimationInterpolator<Color2D> Color { get; } = new ColorInterpolator();

    /// <summary>Interpolates non-null endpoints and switches intervals involving null at their endpoint.</summary>
    /// <typeparam name="T">The non-null value type.</typeparam>
    /// <param name="inner">The borrowed non-null interpolator.</param>
    /// <returns>The nullable value interpolator.</returns>
    public static IAnimationInterpolator<T?> Nullable<T>(IAnimationInterpolator<T> inner)
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(inner);
        return NullableCache<T>.Values.GetValue(inner, static value => new(value));
    }

    private static class NullableCache<T>
        where T : struct
    {
        internal static ConditionalWeakTable<IAnimationInterpolator<T>, NullableInterpolator<T>> Values { get; } = new();
    }

    private sealed class NullableInterpolator<T>(IAnimationInterpolator<T> inner) : IAnimationInterpolator<T?>
        where T : struct
    {
        public T? Interpolate(T? from, T? to, float amount) => from is { } first && to is { } last ? inner.Interpolate(first, last, amount) : amount >= 1 ? to : from;
    }

    private sealed class ColorInterpolator : IAnimationInterpolator<Color2D>
    {
        public Color2D Interpolate(Color2D from, Color2D to, float amount)
        {
            if (amount == 0)
            {
                return from;
            }

            if (amount == 1)
            {
                return to;
            }

            double alpha = from.A + ((to.A - from.A) * (double)amount);
            if (alpha == 0)
            {
                return default;
            }

            float Channel(float first, float last) => Encode(((Decode(first) * from.A) + (((Decode(last) * to.A) - (Decode(first) * from.A)) * amount)) / alpha);
            return new(Channel(from.R, to.R), Channel(from.G, to.G), Channel(from.B, to.B), (float)alpha);
        }

        private static double Decode(float value) => value <= 0.04045f ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);

        private static float Encode(double value) => (float)(value <= 0.0031308 ? value * 12.92 : (1.055 * Math.Pow(value, 1 / 2.4)) - 0.055);
    }
}
