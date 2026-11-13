using System.Numerics;
using Lumyte.Mathematics.Geometry;

namespace Lumyte.Mathematics.Colors;

/// <summary>Stores finite numeric RGBA components without implicit units, transfer, or alpha association.</summary>
/// <typeparam name="T">The ordered numeric storage type.</typeparam>
internal readonly partial record struct Rgba<T> : IColorPixel<Rgba<T>>
    where T : INumber<T>
{
    /// <summary>Initializes a new instance of the <see cref="Rgba{T}"/> struct with finite components, including raw codes and HDR values.</summary>
    /// <param name="r">The red component.</param>
    /// <param name="g">The green component.</param>
    /// <param name="b">The blue component.</param>
    /// <param name="a">The alpha component.</param>
    public Rgba(T r, T g, T b, T a)
    {
        ValidateFinite(r, nameof(r));
        ValidateFinite(g, nameof(g));
        ValidateFinite(b, nameof(b));
        ValidateFinite(a, nameof(a));
        R = r;
        G = g;
        B = b;
        A = a;
    }

    /// <summary>Gets red.</summary>
    public T R { get; }

    /// <summary>Gets green.</summary>
    public T G { get; }

    /// <summary>Gets blue.</summary>
    public T B { get; }

    /// <summary>Gets alpha.</summary>
    public T A { get; }

    /// <summary>Attempts finite construction, returning default on invalid input.</summary>
    /// <param name="r">The red component.</param>
    /// <param name="g">The green component.</param>
    /// <param name="b">The blue component.</param>
    /// <param name="a">The alpha component.</param>
    /// <param name="result">The finite color, or default on failure.</param>
    /// <returns>The resulting color, code, or success flag.</returns>
    public static bool TryCreate(T r, T g, T b, T a, out Rgba<T> result)
    {
        result = default;
        if (!T.IsFinite(r) || !T.IsFinite(g) || !T.IsFinite(b) || !T.IsFinite(a))
        {
            return false;
        }

        result = new(r, g, b, a);
        return true;
    }

    /// <summary>Reads exactly four RGBA components.</summary>
    /// <param name="values">Exactly four RGBA values.</param>
    /// <returns>The resulting color, code, or success flag.</returns>
    public static Rgba<T> FromComponents(ReadOnlySpan<T> values)
    {
        if (values.Length != 4)
        {
            throw new ArgumentException("Exactly four components are required.", nameof(values));
        }

        return new(values[0], values[1], values[2], values[3]);
    }

    /// <summary>Stores byte codes without normalization.</summary>
    /// <param name="r">The red component.</param>
    /// <param name="g">The green component.</param>
    /// <param name="b">The blue component.</param>
    /// <param name="a">The alpha component.</param>
    /// <returns>The resulting color, code, or success flag.</returns>
    public static Rgba<T> FromRgba8Codes(byte r, byte g, byte b, byte a) => new(T.CreateChecked(r), T.CreateChecked(g), T.CreateChecked(b), T.CreateChecked(a));

    /// <summary>Stores the codes of an integer with bit positions 0xAARRGGBB.</summary>
    /// <param name="argb">The packed integer code.</param>
    /// <returns>The resulting color, code, or success flag.</returns>
    public static Rgba<T> FromArgb32Codes(uint argb) => FromRgba8Codes((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, (byte)(argb >> 24));

    /// <summary>Copies four components; a short destination remains unchanged.</summary>
    /// <param name="destination">The destination span.</param>
    public void CopyTo(Span<T> destination)
    {
        if (destination.Length < 4)
        {
            throw new ArgumentException("Four destination elements are required.", nameof(destination));
        }

        destination[0] = R;
        destination[1] = G;
        destination[2] = B;
        destination[3] = A;
    }

    /// <summary>Replaces alpha without changing RGB or assigning units.</summary>
    /// <param name="alpha">The replacement alpha.</param>
    /// <returns>The resulting color, code, or success flag.</returns>
    public Rgba<T> WithAlpha(T alpha) => new(R, G, B, alpha);

    /// <summary>Uses checked numeric casts without normalization or transfer conversion.</summary>
    /// <typeparam name="TTo">The target storage type.</typeparam>
    /// <returns>The resulting color, code, or success flag.</returns>
    public Rgba<TTo> ConvertChecked<TTo>()
        where TTo : INumber<TTo>
    {
        static TTo Convert(T value)
        {
            TTo result = TTo.CreateChecked(value);
            return TTo.IsFinite(result) ? result : throw new OverflowException("The finite component exceeds the target storage range.");
        }

        return new(Convert(R), Convert(G), Convert(B), Convert(A));
    }

    /// <summary>Packs integral byte codes with bit positions 0xAARRGGBB.</summary>
    /// <returns>The resulting color, code, or success flag.</returns>
    public uint ToArgb32Codes() => ((uint)Code(A) << 24) | ((uint)Code(R) << 16) | ((uint)Code(G) << 8) | Code(B);

    /// <summary>Normalizes integral codes using their exact ratio before final rounding.</summary>
    /// <typeparam name="TResult">The result numeric type.</typeparam>
    /// <param name="maximum">The positive integral maximum code.</param>
    /// <returns>The resulting color, code, or success flag.</returns>
    public Rgba<TResult> NormalizeUnorm<TResult>(T maximum)
        where TResult : INumber<TResult>
    {
        ValidateFinite(maximum, nameof(maximum));
        if (maximum <= T.Zero || !T.IsInteger(maximum))
        {
            throw new ArgumentOutOfRangeException(nameof(maximum));
        }

        var upper = GeometryRational.From(maximum);
        TResult Normalize(T value)
        {
            if (value < T.Zero || value > maximum || !T.IsInteger(value))
            {
                throw new ArgumentOutOfRangeException(nameof(maximum), "Integral codes in [0,maximum] are required.");
            }

            return (GeometryRational.From(value) / upper).To<TResult>();
        }

        return new(Normalize(R), Normalize(G), Normalize(B), Normalize(A));
    }

    /// <summary>Quantizes exact represented unit values times an integer maximum, rounding ties to even.</summary>
    /// <typeparam name="TCode">The integral code type.</typeparam>
    /// <param name="maximum">The positive integral maximum code.</param>
    /// <returns>The resulting color, code, or success flag.</returns>
    public Rgba<TCode> QuantizeUnorm<TCode>(TCode maximum)
        where TCode : IBinaryInteger<TCode>
    {
        if (maximum <= TCode.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maximum));
        }

        var upper = BigInteger.CreateChecked(maximum);
        TCode Quantize(T value)
        {
            if (value < T.Zero || value > T.One)
            {
                throw new ArgumentOutOfRangeException(nameof(maximum), "Unit components are required.");
            }

            var exact = GeometryRational.From(value);
            var quotient = BigInteger.DivRem(exact.Numerator * upper, exact.Denominator, out BigInteger rest);
            int comparison = (rest * 2).CompareTo(exact.Denominator);
            if (comparison > 0 || (comparison == 0 && !quotient.IsEven))
            {
                quotient++;
            }

            return TCode.CreateChecked(quotient);
        }

        return new(Quantize(R), Quantize(G), Quantize(B), Quantize(A));
    }

    private static byte Code(T value)
    {
        if (!T.IsInteger(value) || value < T.Zero || GeometryRational.From(value).Numerator > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "An integral byte code is required.");
        }

        return byte.CreateChecked(value);
    }

    private static void ValidateFinite(T value, string name)
    {
        if (!T.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(name, "A finite component is required.");
        }
    }
}
