using System.Numerics;

namespace Lumyte.Mathematics.Colors;

/// <summary>Stores finite Argb components without implicit normalization, transfer, or alpha association.</summary>
/// <typeparam name="T">The ordered numeric storage type.</typeparam>
internal readonly record struct Argb<T> : IColorPixel<Argb<T>>
    where T : INumber<T>
{
    /// <summary>Initializes a new instance of the <see cref="Argb{T}"/> struct with finite raw components.</summary>
    /// <param name="a">The A component.</param>
    /// <param name="r">The R component.</param>
    /// <param name="g">The G component.</param>
    /// <param name="b">The B component.</param>
    public Argb(T a, T r, T g, T b)
    {
        if (!T.IsFinite(a))
        {
            throw new ArgumentOutOfRangeException(nameof(a), "A finite component is required.");
        }

        if (!T.IsFinite(r))
        {
            throw new ArgumentOutOfRangeException(nameof(r), "A finite component is required.");
        }

        if (!T.IsFinite(g))
        {
            throw new ArgumentOutOfRangeException(nameof(g), "A finite component is required.");
        }

        if (!T.IsFinite(b))
        {
            throw new ArgumentOutOfRangeException(nameof(b), "A finite component is required.");
        }

        A = a;
        R = r;
        G = g;
        B = b;
    }

    /// <summary>Gets the A component.</summary>
    public T A { get; }

    /// <summary>Gets the R component.</summary>
    public T R { get; }

    /// <summary>Gets the G component.</summary>
    public T G { get; }

    /// <summary>Gets the B component.</summary>
    public T B { get; }

    /// <summary>Creates this layout without discarding alpha or converting RGB to luminance.</summary>
    /// <param name="color">The source components; alpha is retained.</param>
    /// <returns>The components in this layout.</returns>
    public static Argb<T> FromRgba(Rgba<T> color) => new(color.A, color.R, color.G, color.B);

    /// <summary>Gets the supported pixel size, validating the encoding and byte order.</summary>
    /// <param name="encoding">The numeric component encoding.</param>
    /// <param name="byteOrder">The byte order within each component.</param>
    /// <returns>The byte count of one pixel.</returns>
    public static int PixelSize(ColorComponentEncoding encoding, ColorByteOrder byteOrder) => Rgba<T>.PixelSize(TypedPixelLayout.Resolve("Argb", encoding), byteOrder);

    /// <summary>Reads the first normalized or IEEE pixel in this layout.</summary>
    /// <param name="source">The source bytes; unused tail is ignored.</param>
    /// <param name="encoding">The numeric component encoding.</param>
    /// <param name="byteOrder">The byte order within each component.</param>
    /// <returns>The decoded components without transfer or alpha conversion.</returns>
    public static Argb<T> DecodePixel(ReadOnlySpan<byte> source, ColorComponentEncoding encoding, ColorByteOrder byteOrder) => FromRgba(Rgba<T>.DecodePixel(source, TypedPixelLayout.Resolve("Argb", encoding), byteOrder));

    /// <summary>Expands raw components to RGBA without normalization.</summary>
    /// <returns>The raw RGBA components.</returns>
    public Rgba<T> ToRgba() => new(R, G, B, A);

    /// <summary>Normalizes integral codes by their exact ratio.</summary>
    /// <typeparam name="TResult">The result numeric type.</typeparam>
    /// <param name="maximum">The positive integral maximum code.</param>
    /// <returns>The normalized RGBA components.</returns>
    public Rgba<TResult> NormalizeUnorm<TResult>(T maximum)
        where TResult : INumber<TResult> => ToRgba().NormalizeUnorm<TResult>(maximum);

    /// <summary>Uses checked numeric casts without normalization or transfer conversion.</summary>
    /// <typeparam name="TResult">The target storage type.</typeparam>
    /// <returns>The same layout with checked finite components.</returns>
    public Argb<TResult> ConvertChecked<TResult>()
        where TResult : INumber<TResult>
    {
        Rgba<TResult> color = ToRgba().ConvertChecked<TResult>();
        return new(color.A, color.R, color.G, color.B);
    }

    /// <summary>Writes normalized or IEEE components atomically; raw code values are not automatically normalized.</summary>
    /// <param name="destination">The output bytes; unused tail is preserved.</param>
    /// <param name="encoding">The numeric component encoding.</param>
    /// <param name="byteOrder">The byte order within each component.</param>
    /// <returns>The number of bytes written.</returns>
    public int WritePixel(Span<byte> destination, ColorComponentEncoding encoding, ColorByteOrder byteOrder) => ToRgba().WritePixel(destination, TypedPixelLayout.Resolve("Argb", encoding), byteOrder);
}
