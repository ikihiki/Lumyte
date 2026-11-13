using System.Numerics;

namespace Lumyte.Mathematics.Colors;

/// <summary>Stores a straight sRGB HSV model with periodic degree hue and unit components.</summary>
/// <typeparam name="T">The ordered numeric storage type.</typeparam>
public readonly record struct SHsva<T>
    where T : INumber<T>
{
    /// <summary>Initializes a new instance of the <see cref="SHsva{T}"/> struct.</summary>
    /// <param name="hueDegrees">A finite hue, reduced exactly to [0,360) before final rounding.</param>
    /// <param name="saturation">Saturation in [0,1].</param>
    /// <param name="value">The value in [0,1].</param>
    /// <param name="alpha">Alpha in [0,1].</param>
    public SHsva(T hueDegrees, T saturation, T value, T alpha)
    {
        Components = new(hueDegrees, saturation, value, alpha);
    }

    internal SHsva(Hsva<T> value)
    {
        Components = value;
    }

    /// <summary>Gets the canonical hue in degrees in [0,360).</summary>
    public T HueDegrees => Components.HueDegrees;

    /// <summary>Gets saturation in [0,1].</summary>
    public T Saturation => Components.Saturation;

    /// <summary>Gets value in [0,1].</summary>
    public T Value => Components.Value;

    /// <summary>Gets alpha in [0,1].</summary>
    public T Alpha => Components.Alpha;

    private Hsva<T> Components { get; }

    /// <summary>Converts to normalized straight sRGB using exact intermediate arithmetic.</summary>
    /// <typeparam name="TResult">The result numeric representation.</typeparam>
    /// <returns>The RGBA color rounded only at final component conversion.</returns>
    public SRgba<TResult> ToRgba<TResult>()
        where TResult : INumber<TResult> =>
        new(Components.ToRgba<TResult>());
}
