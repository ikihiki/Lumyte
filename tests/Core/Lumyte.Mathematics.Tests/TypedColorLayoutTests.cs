using System.Numerics;
using System.Runtime.CompilerServices;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Mathematics.Tests;

/// <summary>Checks logical component layouts independently of binary encoding and color space.</summary>
public sealed class TypedColorLayoutTests
{
    /// <summary>Every supported BCL number can store each layout without unmanaged constraints.</summary>
    [Fact]
    public void AllSeventeenNumericTypesSupportAllLayouts()
    {
        Check<byte>();
        Check<sbyte>();
        Check<short>();
        Check<ushort>();
        Check<int>();
        Check<uint>();
        Check<long>();
        Check<ulong>();
        Check<nint>();
        Check<nuint>();
        Check<Int128>();
        Check<UInt128>();
        Check<BigInteger>();
        Check<decimal>();
        Check<Half>();
        Check<float>();
        Check<double>();
    }

    /// <summary>Raw byte alpha is explicit, while named normalization supplies normalized opaque alpha.</summary>
    [Fact]
    public void GrayRawAndNormalizedAlphaAreDifferentOperations()
    {
        var gray = new Gray<byte>(128);
        Assert.Equal(new Rgba<byte>(128, 128, 128, 255), gray.ToRgba(255));
        Assert.Equal(new Rgba<double>(128d / 255, 128d / 255, 128d / 255, 1), gray.NormalizeUnorm<double>(255));
        Assert.Equal(new Rgba<double>(0.5, 0.5, 0.5, 1), new Gray<double>(0.5).ToRgba(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => gray.WritePixel(new byte[1], ColorComponentEncoding.Unorm8, ColorByteOrder.LittleEndian));
        Assert.Throws<ArgumentOutOfRangeException>(() => Gray<double>.FromRgba(new(0, 1, 0, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rgb<byte>.FromRgba(new(128, 128, 128, 255)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Gray<double>.FromRgba(new(0, 0, 0, 0.5)));
        Assert.Null(typeof(Gray<byte>).GetMethod("ToRgba", Type.EmptyTypes));
        Assert.Null(typeof(Rgb<byte>).GetMethod("ToRgba", Type.EmptyTypes));
        Assert.Null(typeof(Bgr<byte>).GetMethod("ToRgba", Type.EmptyTypes));
    }

    /// <summary>Finite construction and layout-preserving checked casts retain HDR without storage tags.</summary>
    [Fact]
    public void LayoutStorageDoesNotImplyUnitOrBinaryLayout()
    {
        Assert.True(RuntimeHelpers.IsReferenceOrContainsReferences<Gray<BigInteger>>());
        Assert.True(RuntimeHelpers.IsReferenceOrContainsReferences<Bgra<BigInteger>>());
        BigInteger huge = BigInteger.One << 20000;
        Assert.Equal(huge, new Gray<BigInteger>(huge).Value);
        Assert.Throws<OverflowException>(() => new Gray<BigInteger>(huge).ConvertChecked<double>());
        Assert.Equal(new Bgra<decimal>(-2, 3, 4, 5), new Bgra<int>(-2, 3, 4, 5).ConvertChecked<decimal>());
        Assert.Throws<ArgumentOutOfRangeException>(() => new Gray<double>(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GrayAlpha<float>(0, float.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Bgra<double>(0, 0, double.NaN, 1));
        Assert.Throws<ArgumentException>(() => Gray<double>.PixelSize(ColorComponentEncoding.Float32, ColorByteOrder.LittleEndian));
        Assert.Throws<ArgumentOutOfRangeException>(() => Gray<double>.PixelSize((ColorComponentEncoding)99, ColorByteOrder.LittleEndian));
    }

    private static void Check<T>()
        where T : INumber<T>
    {
        T one = T.One;
        T two = T.CreateChecked(2);
        T three = T.CreateChecked(3);
        T four = T.CreateChecked(4);
        var expected = new Rgba<T>(one, two, three, four);
        Assert.Equal(expected, new Rgb<T>(one, two, three).ToRgba(four));
        Assert.Equal(expected, new Bgr<T>(three, two, one).ToRgba(four));
        Assert.Equal(expected, new Bgra<T>(three, two, one, four).ToRgba());
        Assert.Equal(expected, new Argb<T>(four, one, two, three).ToRgba());
        Assert.Equal(expected, new Abgr<T>(four, three, two, one).ToRgba());
        Assert.Equal(new Rgba<T>(two, two, two, four), new GrayAlpha<T>(two, four).ToRgba());
        Assert.Equal(new Rgba<T>(two, two, two, four), new Gray<T>(two).ToRgba(four));
        Assert.Equal(new Rgba<double>(0.25, 0.5, 0.75, 1), new Bgra<T>(three, two, one, four).NormalizeUnorm<double>(four));
        Assert.Equal(new Rgba<double>(0.25, 0.5, 0.75, 1), new Rgb<T>(one, two, three).NormalizeUnorm<double>(four));
        Assert.Equal(new Rgba<double>(0.5, 0.5, 0.5, 1), new Gray<T>(two).NormalizeUnorm<double>(four));
    }
}
