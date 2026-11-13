using System.Numerics;

namespace Lumyte.Mathematics.Colors;

/// <summary>Stores a straight sRGB HSL model with periodic degree hue and unit components.</summary>
/// <typeparam name="T">The ordered numeric storage type.</typeparam>
internal readonly record struct Hsla<T>
    where T : INumber<T>
{
    /// <summary>Initializes a new instance of the <see cref="Hsla{T}"/> struct.</summary>
    /// <param name="hueDegrees">A finite hue, reduced exactly to [0,360) before final rounding.</param>
    /// <param name="saturation">Saturation in [0,1].</param>
    /// <param name="lightness">The lightness in [0,1].</param>
    /// <param name="alpha">Alpha in [0,1].</param>
    public Hsla(T hueDegrees, T saturation, T lightness, T alpha)
    {
        HueDegrees = Rgba<T>.CanonicalModelHue(hueDegrees, nameof(hueDegrees));
        Rgba<T>.ValidateModelUnit(saturation, nameof(saturation));
        Rgba<T>.ValidateModelUnit(lightness, nameof(lightness));
        Rgba<T>.ValidateModelUnit(alpha, nameof(alpha));
        Saturation = saturation;
        Lightness = lightness;
        Alpha = alpha;
    }

    /// <summary>Gets the canonical hue in degrees in [0,360).</summary>
    public T HueDegrees { get; }

    /// <summary>Gets saturation in [0,1].</summary>
    public T Saturation { get; }

    /// <summary>Gets lightness in [0,1].</summary>
    public T Lightness { get; }

    /// <summary>Gets alpha in [0,1].</summary>
    public T Alpha { get; }

    /// <summary>Converts to normalized straight sRGB using exact intermediate arithmetic.</summary>
    /// <typeparam name="TResult">The result numeric representation.</typeparam>
    /// <returns>The RGBA color rounded only at final component conversion.</returns>
    public Rgba<TResult> ToRgba<TResult>()
        where TResult : INumber<TResult> =>
        Rgba<TResult>.FromHslSrgbDegrees(HueDegrees, Saturation, Lightness, Alpha);
}
