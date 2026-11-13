using System.Numerics;
using System.Runtime.CompilerServices;
using Lumyte.Animation;
using Lumyte.Mathematics.Colors;

namespace Lumyte.UI;

/// <summary>Provides UI value interpolation without changing the model's color or nullable contracts.</summary>
public static class UiAnimationInterpolators
{
    /// <summary>Gets the sRGB straight-alpha interpolator using linear premultiplied intermediate colors.</summary>
    public static IAnimationInterpolator<SRgba<float>> Color { get; } = new ColorInterpolator();

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
        public void Validate(T? value)
        {
            if (value is { } present)
            {
                inner.Validate(present);
            }
        }

        public T? Interpolate(T? from, T? to, float amount) => from is { } first && to is { } last ? inner.Interpolate(first, last, amount) : amount >= 1 ? to : from;
    }

    private sealed class ColorInterpolator : IAnimationInterpolator<SRgba<float>>
    {
        public void Validate(SRgba<float> value) => UiColorPolicy.Unit(value, nameof(value));

        public SRgba<float> Interpolate(SRgba<float> from, SRgba<float> to, float amount)
        {
            UiColorPolicy.Unit(from, nameof(from));
            UiColorPolicy.Unit(to, nameof(to));
            Vector4 result = Lumyte.Mathematics.Colors.Color.InterpolateSrgbLinearPremultipliedRaw(
                new(from.R, from.G, from.B, from.A), new(to.R, to.G, to.B, to.A), amount);
            UiColorPolicy.Validate(result.X, result.Y, result.Z, result.W, nameof(amount));
            return new(result.X, result.Y, result.Z, result.W);
        }
    }
}
