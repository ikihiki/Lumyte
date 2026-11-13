using System.Numerics;

namespace Lumyte.Mathematics.Colors;

/// <summary>Stores finite Srgb Bgra components without implicit normalization, transfer, or alpha association.</summary>
/// <typeparam name="T">The ordered numeric storage type.</typeparam>
public readonly record struct SrgbBgra<T> : IColorPixel<SrgbBgra<T>>
    where T : INumber<T>
{
    private readonly Bgra<T> _components;

    /// <summary>Initializes a new instance of the <see cref="SrgbBgra{T}"/> struct with finite raw _components.</summary>
    /// <param name="b">The B component.</param>
    /// <param name="g">The G component.</param>
    /// <param name="r">The R component.</param>
    /// <param name="a">The A component.</param>
    public SrgbBgra(T b, T g, T r, T a)
    {
        _components = new(b, g, r, a);
    }

    private SrgbBgra(Bgra<T> components)
    {
        _components = components;
    }

    /// <summary>Gets the B component.</summary>
    public T B => _components.B;

    /// <summary>Gets the G component.</summary>
    public T G => _components.G;

    /// <summary>Gets the R component.</summary>
    public T R => _components.R;

    /// <summary>Gets the A component.</summary>
    public T A => _components.A;

    /// <summary>Creates the same-space layout, rejecting alpha loss or unequal gray _components.</summary>
    /// <param name="color">The same-space source _components.</param>
    /// <returns>The components in this layout.</returns>
    public static SrgbBgra<T> FromRgba(SrgbRgba<T> color) => new(Bgra<T>.FromRgba(color.Components));

    /// <summary>Gets the supported pixel size, validating encoding and byte order.</summary>
    /// <param name="encoding">The numeric component encoding.</param>
    /// <param name="byteOrder">The byte order within each component.</param>
    /// <returns>The byte count of one pixel.</returns>
    public static int PixelSize(ColorComponentEncoding encoding, ColorByteOrder byteOrder) => Bgra<T>.PixelSize(encoding, byteOrder);

    /// <summary>Reads normalized or IEEE components without transfer or alpha conversion.</summary>
    /// <param name="source">The source bytes; unused tail is ignored.</param>
    /// <param name="encoding">The numeric component encoding.</param>
    /// <param name="byteOrder">The byte order within each component.</param>
    /// <returns>The decoded same-space _components.</returns>
    public static SrgbBgra<T> DecodePixel(ReadOnlySpan<byte> source, ColorComponentEncoding encoding, ColorByteOrder byteOrder) => new(Bgra<T>.DecodePixel(source, encoding, byteOrder));

    /// <summary>Expands raw components to same-space RGBA without normalization.</summary>
    /// <returns>The raw same-space RGBA _components.</returns>
    public SrgbRgba<T> ToRgba() => new(_components.ToRgba());

    /// <summary>Normalizes integral codes by their exact ratio, supplying alpha one for alpha-less layouts.</summary>
    /// <typeparam name="TResult">The result numeric type.</typeparam>
    /// <param name="maximum">The positive integral maximum code.</param>
    /// <returns>The normalized same-space RGBA _components.</returns>
    public SrgbRgba<TResult> NormalizeUnorm<TResult>(T maximum)
        where TResult : INumber<TResult> => new(_components.NormalizeUnorm<TResult>(maximum));

    /// <summary>Uses checked numeric casts without normalization or transfer conversion.</summary>
    /// <typeparam name="TResult">The target storage type.</typeparam>
    /// <returns>The same space and layout with checked finite _components.</returns>
    public SrgbBgra<TResult> ConvertChecked<TResult>()
        where TResult : INumber<TResult> => new(_components.ConvertChecked<TResult>());

    /// <summary>Explicitly applies the transfer function to normalized components, retaining layout and alpha.</summary>
    /// <typeparam name="TResult">The IEEE transfer result type.</typeparam>
    /// <returns>The corresponding layout in the other color space.</returns>
    public LinearBgra<TResult> ToLinear<TResult>()
        where TResult : IFloatingPointIeee754<TResult> => LinearBgra<TResult>.FromRgba(ToRgba().ToLinear<TResult>());

    /// <summary>Writes normalized or IEEE components atomically without transfer or alpha conversion.</summary>
    /// <param name="destination">The output bytes; unused tail is preserved.</param>
    /// <param name="encoding">The numeric component encoding.</param>
    /// <param name="byteOrder">The byte order within each component.</param>
    /// <returns>The number of bytes written.</returns>
    public int WritePixel(Span<byte> destination, ColorComponentEncoding encoding, ColorByteOrder byteOrder) => _components.WritePixel(destination, encoding, byteOrder);
}
