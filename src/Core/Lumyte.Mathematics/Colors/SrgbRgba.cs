using System.Numerics;

namespace Lumyte.Mathematics.Colors;

/// <summary>Stores finite SrgbRgba components without inferring units or alpha association.</summary>
/// <typeparam name="T">The ordered numeric storage type.</typeparam>
public readonly record struct SrgbRgba<T> : IColorPixel<SrgbRgba<T>>
    where T : INumber<T>
{
    /// <summary>Initializes a new instance of the <see cref="SrgbRgba{T}"/> struct with finite components.</summary>
    /// <param name="r">The r value.</param>
    /// <param name="g">The g value.</param>
    /// <param name="b">The b value.</param>
    /// <param name="a">The a value.</param>
    public SrgbRgba(T r, T g, T b, T a)
    {
        Components = new(r, g, b, a);
    }

    internal SrgbRgba(Rgba<T> value)
    {
        Components = value;
    }

    /// <summary>Gets the R component.</summary>
    public T R => Components.R;

    /// <summary>Gets the G component.</summary>
    public T G => Components.G;

    /// <summary>Gets the B component.</summary>
    public T B => Components.B;

    /// <summary>Gets the A component.</summary>
    public T A => Components.A;

    internal Rgba<T> Components { get; }

    /// <summary>Attempts finite construction; invalid input returns false and default.</summary>
    /// <param name="r">The r value.</param>
    /// <param name="g">The g value.</param>
    /// <param name="b">The b value.</param>
    /// <param name="a">The a value.</param>
    /// <param name="result">The result value.</param>
    /// <returns>The resulting color, code, byte count, or success flag.</returns>
    public static bool TryCreate(T r, T g, T b, T a, out SrgbRgba<T> result)
    {
        bool success = Rgba<T>.TryCreate(r, g, b, a, out Rgba<T> value);
        result = success ? new(value) : default;
        return success;
    }

    /// <summary>Reads exactly four components.</summary>
    /// <param name="values">The values value.</param>
    /// <returns>The resulting color, code, byte count, or success flag.</returns>
    public static SrgbRgba<T> FromComponents(ReadOnlySpan<T> values) => new(Rgba<T>.FromComponents(values));

    /// <summary>Stores raw byte codes without normalization.</summary>
    /// <param name="r">The r value.</param>
    /// <param name="g">The g value.</param>
    /// <param name="b">The b value.</param>
    /// <param name="a">The a value.</param>
    /// <returns>The resulting color, code, byte count, or success flag.</returns>
    public static SrgbRgba<T> FromRgba8Codes(byte r, byte g, byte b, byte a) => new(Rgba<T>.FromRgba8Codes(r, g, b, a));

    /// <summary>Stores raw codes with bit positions 0xAARRGGBB.</summary>
    /// <param name="argb">The argb value.</param>
    /// <returns>The resulting color, code, byte count, or success flag.</returns>
    public static SrgbRgba<T> FromArgb32Codes(uint argb) => new(Rgba<T>.FromArgb32Codes(argb));

    /// <summary>Gets the pixel size, validating encoding and byte order.</summary>
    /// <param name="encoding">The encoding value.</param>
    /// <param name="byteOrder">The byteOrder value.</param>
    /// <returns>The resulting color, code, byte count, or success flag.</returns>
    public static int PixelSize(ColorComponentEncoding encoding, ColorByteOrder byteOrder) => Rgba<T>.PixelSize(encoding, byteOrder);

    /// <summary>Reads one normalized or IEEE pixel without transfer or alpha conversion.</summary>
    /// <param name="source">The source value.</param>
    /// <param name="encoding">The encoding value.</param>
    /// <param name="byteOrder">The byteOrder value.</param>
    /// <returns>The resulting color, code, byte count, or success flag.</returns>
    public static SrgbRgba<T> DecodePixel(ReadOnlySpan<byte> source, ColorComponentEncoding encoding, ColorByteOrder byteOrder) => new(Rgba<T>.DecodePixel(source, encoding, byteOrder));

    /// <summary>Converts a straight sRGB degree model with exact intermediate arithmetic.</summary>
    /// <typeparam name="TModel">The explicit numeric result or model type.</typeparam>
    /// <param name="hue">The hue value.</param>
    /// <param name="saturation">The saturation value.</param>
    /// <param name="lightness">The lightness value.</param>
    /// <param name="alpha">The alpha value.</param>
    /// <returns>The resulting color, code, byte count, or success flag.</returns>
    public static SrgbRgba<T> FromHslSrgbDegrees<TModel>(TModel hue, TModel saturation, TModel lightness, TModel alpha)
        where TModel : INumber<TModel> => new(Rgba<T>.FromHslSrgbDegrees(hue, saturation, lightness, alpha));

    /// <summary>Converts a straight sRGB degree model with exact intermediate arithmetic.</summary>
    /// <typeparam name="TModel">The explicit numeric result or model type.</typeparam>
    /// <param name="hue">The hue value.</param>
    /// <param name="saturation">The saturation value.</param>
    /// <param name="value">The value value.</param>
    /// <param name="alpha">The alpha value.</param>
    /// <returns>The resulting color, code, byte count, or success flag.</returns>
    public static SrgbRgba<T> FromHsvSrgbDegrees<TModel>(TModel hue, TModel saturation, TModel value, TModel alpha)
        where TModel : INumber<TModel> => new(Rgba<T>.FromHsvSrgbDegrees(hue, saturation, value, alpha));

    /// <summary>Copies four components atomically on short output.</summary>
    /// <param name="destination">The destination value.</param>
    public void CopyTo(Span<T> destination) => Components.CopyTo(destination);

    /// <summary>Replaces alpha without changing RGB or units.</summary>
    /// <param name="alpha">The alpha value.</param>
    /// <returns>The resulting color, code, byte count, or success flag.</returns>
    public SrgbRgba<T> WithAlpha(T alpha) => new(Components.WithAlpha(alpha));

    /// <summary>Performs checked storage casts without normalization or transfer.</summary>
    /// <typeparam name="TTo">The explicit numeric result or model type.</typeparam>
    /// <returns>The resulting color, code, byte count, or success flag.</returns>
    public SrgbRgba<TTo> ConvertChecked<TTo>()
        where TTo : INumber<TTo> => new(Components.ConvertChecked<TTo>());

    /// <summary>Packs integral byte codes with bit positions 0xAARRGGBB.</summary>
    /// <returns>The resulting color, code, byte count, or success flag.</returns>
    public uint ToArgb32Codes() => Components.ToArgb32Codes();

    /// <summary>Normalizes integral codes using exact ratios before final rounding.</summary>
    /// <typeparam name="TResult">The explicit numeric result or model type.</typeparam>
    /// <param name="maximum">The maximum value.</param>
    /// <returns>The resulting color, code, byte count, or success flag.</returns>
    public SrgbRgba<TResult> NormalizeUnorm<TResult>(T maximum)
        where TResult : INumber<TResult> => new(Components.NormalizeUnorm<TResult>(maximum));

    /// <summary>Quantizes unit values using exact nearest-even rounding.</summary>
    /// <typeparam name="TCode">The explicit numeric result or model type.</typeparam>
    /// <param name="maximum">The maximum value.</param>
    /// <returns>The resulting color, code, byte count, or success flag.</returns>
    public SrgbRgba<TCode> QuantizeUnorm<TCode>(TCode maximum)
        where TCode : IBinaryInteger<TCode> => new(Components.QuantizeUnorm(maximum));

    /// <summary>Writes one pixel atomically without transfer or alpha conversion.</summary>
    /// <param name="destination">The destination value.</param>
    /// <param name="encoding">The encoding value.</param>
    /// <param name="byteOrder">The byteOrder value.</param>
    /// <returns>The resulting color, code, byte count, or success flag.</returns>
    public int WritePixel(Span<byte> destination, ColorComponentEncoding encoding, ColorByteOrder byteOrder) => Components.WritePixel(destination, encoding, byteOrder);

    /// <summary>Decodes normalized straight sRGB, preserving alpha.</summary>
    /// <typeparam name="TResult">The explicit numeric result or model type.</typeparam>
    /// <returns>The resulting color, code, byte count, or success flag.</returns>
    public LinearRgba<TResult> ToLinear<TResult>()
        where TResult : IFloatingPointIeee754<TResult> => new(Components.DecodeSrgb<TResult>());

    /// <summary>Interpolates normalized sRGB through the complete linear premultiplied pipeline.</summary>
    /// <typeparam name="TResult">The explicit numeric result or model type.</typeparam>
    /// <param name="to">The to value.</param>
    /// <param name="amount">The amount value.</param>
    /// <returns>The resulting color, code, byte count, or success flag.</returns>
    public SrgbRgba<TResult> InterpolateSrgbLinearPremultiplied<TResult>(SrgbRgba<T> to, TResult amount)
        where TResult : IFloatingPointIeee754<TResult> => new(Components.InterpolateSrgbLinearPremultiplied(to.Components, amount));

    /// <summary>Converts normalized straight sRGB to a typed degree model.</summary>
    /// <typeparam name="TResult">The explicit numeric result or model type.</typeparam>
    /// <returns>The resulting color, code, byte count, or success flag.</returns>
    public SrgbHsla<TResult> ToHslSrgbDegrees<TResult>()
        where TResult : INumber<TResult> => new(Components.ToHslSrgbDegrees<TResult>());

    /// <summary>Converts normalized straight sRGB to a typed degree model.</summary>
    /// <typeparam name="TResult">The explicit numeric result or model type.</typeparam>
    /// <returns>The resulting color, code, byte count, or success flag.</returns>
    public SrgbHsva<TResult> ToHsvSrgbDegrees<TResult>()
        where TResult : INumber<TResult> => new(Components.ToHsvSrgbDegrees<TResult>());
}
