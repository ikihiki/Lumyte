using System.Numerics;

namespace Lumyte.Mathematics.Colors;

/// <summary>Stores straight-alpha sRGB eight-bit codes, with transparent black as the default value.</summary>
/// <remarks>This semantic color representation does not guarantee a native ABI or memory layout. Conversions preserve hidden RGB at zero alpha.</remarks>
public readonly record struct Srgb8
{
    /// <summary>Initializes a new instance of the <see cref="Srgb8"/> struct from eight-bit codes.</summary>
    /// <param name="r">The red sRGB code.</param>
    /// <param name="g">The green sRGB code.</param>
    /// <param name="b">The blue sRGB code.</param>
    /// <param name="a">The alpha code, defaulting to opaque.</param>
    public Srgb8(byte r, byte g, byte b, byte a = 255)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    /// <summary>Gets the red sRGB code.</summary>
    public byte R { get; }

    /// <summary>Gets the green sRGB code.</summary>
    public byte G { get; }

    /// <summary>Gets the blue sRGB code.</summary>
    public byte B { get; }

    /// <summary>Gets the alpha code.</summary>
    public byte A { get; }

    /// <summary>Normalizes each code by 255f without allocation, sRGB decoding, or premultiplication.</summary>
    /// <param name="color">The eight-bit sRGB color.</param>
    /// <returns>The normalized straight-alpha sRGB color.</returns>
    public static implicit operator Rgba<float>(Srgb8 color) => new(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);

    /// <summary>Validates the unit interval and quantizes each component using nearest-even rounding without clamping.</summary>
    /// <param name="color">The normalized straight-alpha sRGB color.</param>
    /// <returns>The quantized eight-bit color.</returns>
    public static explicit operator Srgb8(Rgba<float> color) => FromNormalized(color);

    /// <summary>Replaces alpha without changing RGB.</summary>
    /// <param name="alpha">The replacement alpha code.</param>
    /// <returns>The color with the specified alpha.</returns>
    public Srgb8 WithAlpha(byte alpha) => new(R, G, B, alpha);

    /// <summary>Returns raw RGBA codes without normalization.</summary>
    /// <returns>The unchanged eight-bit components.</returns>
    public Rgba<byte> ToCodes() => new(R, G, B, A);

    /// <summary>Constructs a color from raw RGBA codes without normalization.</summary>
    /// <param name="codes">The eight-bit components.</param>
    /// <returns>The color with the specified codes.</returns>
    public static Srgb8 FromCodes(Rgba<byte> codes) => new(codes.R, codes.G, codes.B, codes.A);

    /// <summary>Normalizes all codes without changing transfer or alpha association.</summary>
    /// <typeparam name="TResult">The result component type; fractional results must be representable.</typeparam>
    /// <returns>The normalized straight-alpha sRGB color.</returns>
    public Rgba<TResult> ToNormalized<TResult>()
        where TResult : INumber<TResult> => ToCodes().NormalizeUnorm<TResult>(255);

    /// <summary>Quantizes normalized straight-alpha sRGB using exact nearest-even UNORM conversion without clamping.</summary>
    /// <typeparam name="T">The numeric component type.</typeparam>
    /// <param name="color">RGBA components in the unit interval.</param>
    /// <returns>The quantized eight-bit color.</returns>
    public static Srgb8 FromNormalized<T>(Rgba<T> color)
        where T : INumber<T> => FromCodes(color.QuantizeUnorm<byte>(255));
}
