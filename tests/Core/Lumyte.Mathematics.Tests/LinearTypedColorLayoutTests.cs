using System.Numerics;
using System.Runtime.CompilerServices;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Mathematics.Tests;

/// <summary>Checks logical component layouts independently of binary encoding and color space.</summary>
public sealed class LinearTypedColorLayoutTests
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
        var gray = new LGray<byte>(128);
        Assert.Equal(new LRgba<byte>(128, 128, 128, 255), gray.ToRgba(255));
        Assert.Equal(new LRgba<double>(128d / 255, 128d / 255, 128d / 255, 1), gray.NormalizeUnorm<double>(255));
        Assert.Equal(new LRgba<double>(0.5, 0.5, 0.5, 1), new LGray<double>(0.5).ToRgba(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => gray.WritePixel(new byte[1], ColorComponentEncoding.Unorm8, ColorByteOrder.LittleEndian));
        Assert.Throws<ArgumentOutOfRangeException>(() => LGray<double>.FromRgba(new(0, 1, 0, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => LRgb<byte>.FromRgba(new(128, 128, 128, 255)));
        Assert.Throws<ArgumentOutOfRangeException>(() => LGray<double>.FromRgba(new(0, 0, 0, 0.5)));
        Assert.Null(typeof(LGray<byte>).GetMethod("ToRgba", Type.EmptyTypes));
        Assert.Null(typeof(LRgb<byte>).GetMethod("ToRgba", Type.EmptyTypes));
        Assert.Null(typeof(LBgr<byte>).GetMethod("ToRgba", Type.EmptyTypes));
    }

    /// <summary>Finite construction and layout-preserving checked casts retain HDR without storage tags.</summary>
    [Fact]
    public void LayoutStorageDoesNotImplyUnitOrBinaryLayout()
    {
        Assert.True(RuntimeHelpers.IsReferenceOrContainsReferences<LGray<BigInteger>>());
        Assert.True(RuntimeHelpers.IsReferenceOrContainsReferences<LBgra<BigInteger>>());
        BigInteger huge = BigInteger.One << 20000;
        Assert.Equal(huge, new LGray<BigInteger>(huge).Value);
        Assert.Throws<OverflowException>(() => new LGray<BigInteger>(huge).ConvertChecked<double>());
        Assert.Equal(new LBgra<decimal>(-2, 3, 4, 5), new LBgra<int>(-2, 3, 4, 5).ConvertChecked<decimal>());
        Assert.Throws<ArgumentOutOfRangeException>(() => new LGray<double>(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LGrayAlpha<float>(0, float.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LBgra<double>(0, 0, double.NaN, 1));
        Assert.Throws<ArgumentException>(() => LGray<double>.PixelSize(ColorComponentEncoding.Float32, ColorByteOrder.LittleEndian));
        Assert.Throws<ArgumentOutOfRangeException>(() => LGray<double>.PixelSize((ColorComponentEncoding)99, ColorByteOrder.LittleEndian));
    }

    private static void Check<T>()
        where T : INumber<T>
    {
        T one = T.One;
        T two = T.CreateChecked(2);
        T three = T.CreateChecked(3);
        T four = T.CreateChecked(4);
        var expected = new LRgba<T>(one, two, three, four);
        Assert.Equal(expected, new LRgb<T>(one, two, three).ToRgba(four));
        Assert.Equal(expected, new LBgr<T>(three, two, one).ToRgba(four));
        Assert.Equal(expected, new LBgra<T>(three, two, one, four).ToRgba());
        Assert.Equal(expected, new LArgb<T>(four, one, two, three).ToRgba());
        Assert.Equal(expected, new LAbgr<T>(four, three, two, one).ToRgba());
        Assert.Equal(new LRgba<T>(two, two, two, four), new LGrayAlpha<T>(two, four).ToRgba());
        Assert.Equal(new LRgba<T>(two, two, two, four), new LGray<T>(two).ToRgba(four));
        Assert.Equal(new LRgba<double>(0.25, 0.5, 0.75, 1), new LBgra<T>(three, two, one, four).NormalizeUnorm<double>(four));
        Assert.Equal(new LRgba<double>(0.25, 0.5, 0.75, 1), new LRgb<T>(one, two, three).NormalizeUnorm<double>(four));
        Assert.Equal(new LRgba<double>(0.5, 0.5, 0.5, 1), new LGray<T>(two).NormalizeUnorm<double>(four));
    }
}
