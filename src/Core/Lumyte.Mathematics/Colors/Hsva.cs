using System.Numerics;

namespace Lumyte.Mathematics.Colors;

/// <summary>Stores a straight sRGB HSV model with periodic degree hue and unit components.</summary>
/// <typeparam name="T">The ordered numeric storage type.</typeparam>
public readonly record struct Hsva<T>
    where T : INumber<T>
{
    /// <summary>Initializes a new instance of the <see cref="Hsva{T}"/> struct.</summary>
    /// <param name="hueDegrees">A finite hue, reduced exactly to [0,360) before final rounding.</param>
    /// <param name="saturation">Saturation in [0,1].</param>
    /// <param name="value">The value in [0,1].</param>
    /// <param name="alpha">Alpha in [0,1].</param>
    public Hsva(T hueDegrees, T saturation, T value, T alpha)
    {
        HueDegrees = Rgba<T>.CanonicalModelHue(hueDegrees, nameof(hueDegrees));
        Rgba<T>.ValidateModelUnit(saturation, nameof(saturation));
        Rgba<T>.ValidateModelUnit(value, nameof(value));
        Rgba<T>.ValidateModelUnit(alpha, nameof(alpha));
        Saturation = saturation;
        Value = value;
        Alpha = alpha;
    }

    /// <summary>Gets the canonical hue in degrees in [0,360).</summary>
    public T HueDegrees { get; }

    /// <summary>Gets saturation in [0,1].</summary>
    public T Saturation { get; }

    /// <summary>Gets value in [0,1].</summary>
    public T Value { get; }

    /// <summary>Gets alpha in [0,1].</summary>
    public T Alpha { get; }

    /// <summary>Converts to normalized straight sRGB using exact intermediate arithmetic.</summary>
    /// <typeparam name="TResult">The result numeric representation.</typeparam>
    /// <returns>The RGBA color rounded only at final component conversion.</returns>
    public Rgba<TResult> ToRgba<TResult>()
        where TResult : INumber<TResult> =>
        Rgba<TResult>.FromHsvSrgbDegrees(HueDegrees, Saturation, Value, Alpha);
}
