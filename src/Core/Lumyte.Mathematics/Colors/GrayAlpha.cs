using System.Numerics;

namespace Lumyte.Mathematics.Colors;

/// <summary>Stores finite GrayAlpha components without implicit normalization, transfer, or alpha association.</summary>
/// <typeparam name="T">The ordered numeric storage type.</typeparam>
internal readonly record struct GrayAlpha<T> : IColorPixel<GrayAlpha<T>>
    where T : INumber<T>
{
    /// <summary>Initializes a new instance of the <see cref="GrayAlpha{T}"/> struct with finite raw components.</summary>
    /// <param name="value">The gray component.</param>
    /// <param name="a">The A component.</param>
    public GrayAlpha(T value, T a)
    {
        if (!T.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "A finite component is required.");
        }

        if (!T.IsFinite(a))
        {
            throw new ArgumentOutOfRangeException(nameof(a), "A finite component is required.");
        }

        Value = value;
        A = a;
    }

    /// <summary>Gets the gray component.</summary>
    public T Value { get; }

    /// <summary>Gets the A component.</summary>
    public T A { get; }

    /// <summary>Creates this layout without discarding alpha or converting RGB to luminance.</summary>
    /// <param name="color">The source components; alpha is retained and RGB must be exactly equal.</param>
    /// <returns>The components in this layout.</returns>
    public static GrayAlpha<T> FromRgba(Rgba<T> color)
    {
        if (color.R != color.G || color.R != color.B)
        {
            throw new ArgumentOutOfRangeException(nameof(color), "Gray storage requires equal RGB components.");
        }

        return new(color.R, color.A);
    }

    /// <summary>Gets the supported pixel size, validating the encoding and byte order.</summary>
    /// <param name="encoding">The numeric component encoding.</param>
    /// <param name="byteOrder">The byte order within each component.</param>
    /// <returns>The byte count of one pixel.</returns>
    public static int PixelSize(ColorComponentEncoding encoding, ColorByteOrder byteOrder) => Rgba<T>.PixelSize(TypedPixelLayout.Resolve("GrayAlpha", encoding), byteOrder);

    /// <summary>Reads the first normalized or IEEE pixel in this layout.</summary>
    /// <param name="source">The source bytes; unused tail is ignored.</param>
    /// <param name="encoding">The numeric component encoding.</param>
    /// <param name="byteOrder">The byte order within each component.</param>
    /// <returns>The decoded components without transfer or alpha conversion.</returns>
    public static GrayAlpha<T> DecodePixel(ReadOnlySpan<byte> source, ColorComponentEncoding encoding, ColorByteOrder byteOrder) => FromRgba(Rgba<T>.DecodePixel(source, TypedPixelLayout.Resolve("GrayAlpha", encoding), byteOrder));

    /// <summary>Expands raw components to RGBA without normalization.</summary>
    /// <returns>The raw RGBA components.</returns>
    public Rgba<T> ToRgba() => new(Value, Value, Value, A);

    /// <summary>Normalizes integral codes by their exact ratio.</summary>
    /// <typeparam name="TResult">The result numeric type.</typeparam>
    /// <param name="maximum">The positive integral maximum code.</param>
    /// <returns>The normalized RGBA components.</returns>
    public Rgba<TResult> NormalizeUnorm<TResult>(T maximum)
        where TResult : INumber<TResult> => ToRgba().NormalizeUnorm<TResult>(maximum);

    /// <summary>Uses checked numeric casts without normalization or transfer conversion.</summary>
    /// <typeparam name="TResult">The target storage type.</typeparam>
    /// <returns>The same layout with checked finite components.</returns>
    public GrayAlpha<TResult> ConvertChecked<TResult>()
        where TResult : INumber<TResult>
    {
        Rgba<TResult> color = ToRgba().ConvertChecked<TResult>();
        return new(color.R, color.A);
    }

    /// <summary>Writes normalized or IEEE components atomically; raw code values are not automatically normalized.</summary>
    /// <param name="destination">The output bytes; unused tail is preserved.</param>
    /// <param name="encoding">The numeric component encoding.</param>
    /// <param name="byteOrder">The byte order within each component.</param>
    /// <returns>The number of bytes written.</returns>
    public int WritePixel(Span<byte> destination, ColorComponentEncoding encoding, ColorByteOrder byteOrder) => ToRgba().WritePixel(destination, TypedPixelLayout.Resolve("GrayAlpha", encoding), byteOrder);
}
