using System.Numerics;

namespace Lumyte.Mathematics.Colors;

/// <summary>Stores finite LinearRgba components without inferring units or alpha association.</summary>
/// <typeparam name="T">The ordered numeric storage type.</typeparam>
public readonly record struct LinearRgba<T> : IColorPixel<LinearRgba<T>>
    where T : INumber<T>
{
    /// <summary>Initializes a new instance of the <see cref="LinearRgba{T}"/> struct with finite components.</summary>
    /// <param name="r">The r value.</param>
    /// <param name="g">The g value.</param>
    /// <param name="b">The b value.</param>
    /// <param name="a">The a value.</param>
    public LinearRgba(T r, T g, T b, T a)
    {
        Components = new(r, g, b, a);
    }

    internal LinearRgba(Rgba<T> value)
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
    public static bool TryCreate(T r, T g, T b, T a, out LinearRgba<T> result)
    {
        bool success = Rgba<T>.TryCreate(r, g, b, a, out Rgba<T> value);
        result = success ? new(value) : default;
        return success;
    }

    /// <summary>Reads exactly four components.</summary>
    /// <param name="values">The values value.</param>
    /// <returns>The resulting color, code, byte count, or success flag.</returns>
    public static LinearRgba<T> FromComponents(ReadOnlySpan<T> values) => new(Rgba<T>.FromComponents(values));

    /// <summary>Stores raw byte codes without normalization.</summary>
    /// <param name="r">The r value.</param>
    /// <param name="g">The g value.</param>
    /// <param name="b">The b value.</param>
    /// <param name="a">The a value.</param>
    /// <returns>The resulting color, code, byte count, or success flag.</returns>
    public static LinearRgba<T> FromRgba8Codes(byte r, byte g, byte b, byte a) => new(Rgba<T>.FromRgba8Codes(r, g, b, a));

    /// <summary>Stores raw codes with bit positions 0xAARRGGBB.</summary>
    /// <param name="argb">The argb value.</param>
    /// <returns>The resulting color, code, byte count, or success flag.</returns>
    public static LinearRgba<T> FromArgb32Codes(uint argb) => new(Rgba<T>.FromArgb32Codes(argb));

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
    public static LinearRgba<T> DecodePixel(ReadOnlySpan<byte> source, ColorComponentEncoding encoding, ColorByteOrder byteOrder) => new(Rgba<T>.DecodePixel(source, encoding, byteOrder));

    /// <summary>Copies four components atomically on short output.</summary>
    /// <param name="destination">The destination value.</param>
    public void CopyTo(Span<T> destination) => Components.CopyTo(destination);

    /// <summary>Replaces alpha without changing RGB or units.</summary>
    /// <param name="alpha">The alpha value.</param>
    /// <returns>The resulting color, code, byte count, or success flag.</returns>
    public LinearRgba<T> WithAlpha(T alpha) => new(Components.WithAlpha(alpha));

    /// <summary>Performs checked storage casts without normalization or transfer.</summary>
    /// <typeparam name="TTo">The explicit numeric result or model type.</typeparam>
    /// <returns>The resulting color, code, byte count, or success flag.</returns>
    public LinearRgba<TTo> ConvertChecked<TTo>()
        where TTo : INumber<TTo> => new(Components.ConvertChecked<TTo>());

    /// <summary>Packs integral byte codes with bit positions 0xAARRGGBB.</summary>
    /// <returns>The resulting color, code, byte count, or success flag.</returns>
    public uint ToArgb32Codes() => Components.ToArgb32Codes();

    /// <summary>Normalizes integral codes using exact ratios before final rounding.</summary>
    /// <typeparam name="TResult">The explicit numeric result or model type.</typeparam>
    /// <param name="maximum">The maximum value.</param>
    /// <returns>The resulting color, code, byte count, or success flag.</returns>
    public LinearRgba<TResult> NormalizeUnorm<TResult>(T maximum)
        where TResult : INumber<TResult> => new(Components.NormalizeUnorm<TResult>(maximum));

    /// <summary>Quantizes unit values using exact nearest-even rounding.</summary>
    /// <typeparam name="TCode">The explicit numeric result or model type.</typeparam>
    /// <param name="maximum">The maximum value.</param>
    /// <returns>The resulting color, code, byte count, or success flag.</returns>
    public LinearRgba<TCode> QuantizeUnorm<TCode>(TCode maximum)
        where TCode : IBinaryInteger<TCode> => new(Components.QuantizeUnorm(maximum));

    /// <summary>Writes one pixel atomically without transfer or alpha conversion.</summary>
    /// <param name="destination">The destination value.</param>
    /// <param name="encoding">The encoding value.</param>
    /// <param name="byteOrder">The byteOrder value.</param>
    /// <returns>The resulting color, code, byte count, or success flag.</returns>
    public int WritePixel(Span<byte> destination, ColorComponentEncoding encoding, ColorByteOrder byteOrder) => Components.WritePixel(destination, encoding, byteOrder);

    /// <summary>Encodes normalized straight linear RGB, preserving alpha.</summary>
    /// <typeparam name="TResult">The explicit numeric result or model type.</typeparam>
    /// <returns>The resulting color, code, byte count, or success flag.</returns>
    public SrgbRgba<TResult> ToSrgb<TResult>()
        where TResult : IFloatingPointIeee754<TResult> => new(Components.EncodeSrgb<TResult>());

    /// <summary>Associates linear RGB with unit alpha without clipping HDR.</summary>
    /// <typeparam name="TResult">The explicit numeric result or model type.</typeparam>
    /// <returns>The resulting color, code, byte count, or success flag.</returns>
    public LinearRgba<TResult> PremultiplyLinear<TResult>()
        where TResult : INumber<TResult> => new(Components.PremultiplyLinear<TResult>());

    /// <summary>Unassociates linear RGB; zero alpha produces transparent black.</summary>
    /// <typeparam name="TResult">The explicit numeric result or model type.</typeparam>
    /// <returns>The resulting color, code, byte count, or success flag.</returns>
    public LinearRgba<TResult> UnpremultiplyLinear<TResult>()
        where TResult : INumber<TResult> => new(Components.UnpremultiplyLinear<TResult>());

    /// <summary>Computes linear luminance with exact coefficients and preserves alpha.</summary>
    /// <typeparam name="TResult">The explicit numeric result or model type.</typeparam>
    /// <returns>The resulting color, code, byte count, or success flag.</returns>
    public LinearRgba<TResult> ToLinearGrayscale<TResult>()
        where TResult : INumber<TResult> => new(Components.ToLinearGrayscale<TResult>());

    /// <summary>Composites this linear premultiplied source over a destination.</summary>
    /// <param name="destination">The destination value.</param>
    /// <returns>The resulting color, code, byte count, or success flag.</returns>
    public LinearRgba<T> SourceOverLinearPremultiplied(LinearRgba<T> destination) => new(Components.SourceOverLinearPremultiplied(destination.Components));

    /// <summary>Adds linear premultiplied colors, clipping alpha only.</summary>
    /// <param name="destination">The destination value.</param>
    /// <returns>The resulting color, code, byte count, or success flag.</returns>
    public LinearRgba<T> AddLinearPremultiplied(LinearRgba<T> destination) => new(Components.AddLinearPremultiplied(destination.Components));

    /// <summary>Adds linear premultiplied colors, clipping all components.</summary>
    /// <param name="destination">The destination value.</param>
    /// <returns>The resulting color, code, byte count, or success flag.</returns>
    public LinearRgba<T> AddUnitLinearPremultiplied(LinearRgba<T> destination) => new(Components.AddUnitLinearPremultiplied(destination.Components));

    /// <summary>Applies a straight-linear BCL row-vector matrix and bias, then associates the result.</summary>
    /// <param name="matrix">The matrix value.</param>
    /// <param name="offset">The offset value.</param>
    /// <returns>The resulting color, code, byte count, or success flag.</returns>
    public LinearRgba<float> ApplyMatrixLinearPremultiplied(Matrix4x4 matrix, Vector4 offset) => new(Components.ApplyMatrixLinearPremultiplied(matrix, offset));
}
