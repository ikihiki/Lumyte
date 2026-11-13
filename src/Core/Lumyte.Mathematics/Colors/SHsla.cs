using System.Numerics;

namespace Lumyte.Mathematics.Colors;

/// <summary>Stores a straight sRGB HSL model with periodic degree hue and unit components.</summary>
/// <typeparam name="T">The ordered numeric storage type.</typeparam>
public readonly record struct SHsla<T>
    where T : INumber<T>
{
    /// <summary>Initializes a new instance of the <see cref="SHsla{T}"/> struct.</summary>
    /// <param name="hueDegrees">A finite hue, reduced exactly to [0,360) before final rounding.</param>
    /// <param name="saturation">Saturation in [0,1].</param>
    /// <param name="lightness">The lightness in [0,1].</param>
    /// <param name="alpha">Alpha in [0,1].</param>
    public SHsla(T hueDegrees, T saturation, T lightness, T alpha)
    {
        Components = new(hueDegrees, saturation, lightness, alpha);
    }

    internal SHsla(Hsla<T> value)
    {
        Components = value;
    }

    /// <summary>Gets the canonical hue in degrees in [0,360).</summary>
    public T HueDegrees => Components.HueDegrees;

    /// <summary>Gets saturation in [0,1].</summary>
    public T Saturation => Components.Saturation;

    /// <summary>Gets lightness in [0,1].</summary>
    public T Lightness => Components.Lightness;

    /// <summary>Gets alpha in [0,1].</summary>
    public T Alpha => Components.Alpha;

    private Hsla<T> Components { get; }

    /// <summary>Converts to normalized straight sRGB using exact intermediate arithmetic.</summary>
    /// <typeparam name="TResult">The result numeric representation.</typeparam>
    /// <returns>The RGBA color rounded only at final component conversion.</returns>
    public SRgba<TResult> ToRgba<TResult>()
        where TResult : INumber<TResult> =>
        new(Components.ToRgba<TResult>());
}
