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
        var gray = new SrgbGray<byte>(128);
        Assert.Equal(new SrgbRgba<byte>(128, 128, 128, 255), gray.ToRgba(255));
        Assert.Equal(new SrgbRgba<double>(128d / 255, 128d / 255, 128d / 255, 1), gray.NormalizeUnorm<double>(255));
        Assert.Equal(new SrgbRgba<double>(0.5, 0.5, 0.5, 1), new SrgbGray<double>(0.5).ToRgba(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => gray.WritePixel(new byte[1], ColorComponentEncoding.Unorm8, ColorByteOrder.LittleEndian));
        Assert.Throws<ArgumentOutOfRangeException>(() => SrgbGray<double>.FromRgba(new(0, 1, 0, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => SrgbRgb<byte>.FromRgba(new(128, 128, 128, 255)));
        Assert.Throws<ArgumentOutOfRangeException>(() => SrgbGray<double>.FromRgba(new(0, 0, 0, 0.5)));
        Assert.Null(typeof(SrgbGray<byte>).GetMethod("ToRgba", Type.EmptyTypes));
        Assert.Null(typeof(SrgbRgb<byte>).GetMethod("ToRgba", Type.EmptyTypes));
        Assert.Null(typeof(SrgbBgr<byte>).GetMethod("ToRgba", Type.EmptyTypes));
    }

    /// <summary>Finite construction and layout-preserving checked casts retain HDR without storage tags.</summary>
    [Fact]
    public void LayoutStorageDoesNotImplyUnitOrBinaryLayout()
    {
        Assert.True(RuntimeHelpers.IsReferenceOrContainsReferences<SrgbGray<BigInteger>>());
        Assert.True(RuntimeHelpers.IsReferenceOrContainsReferences<SrgbBgra<BigInteger>>());
        BigInteger huge = BigInteger.One << 20000;
        Assert.Equal(huge, new SrgbGray<BigInteger>(huge).Value);
        Assert.Throws<OverflowException>(() => new SrgbGray<BigInteger>(huge).ConvertChecked<double>());
        Assert.Equal(new SrgbBgra<decimal>(-2, 3, 4, 5), new SrgbBgra<int>(-2, 3, 4, 5).ConvertChecked<decimal>());
        Assert.Throws<ArgumentOutOfRangeException>(() => new SrgbGray<double>(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SrgbGrayAlpha<float>(0, float.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SrgbBgra<double>(0, 0, double.NaN, 1));
        Assert.Throws<ArgumentException>(() => SrgbGray<double>.PixelSize(ColorComponentEncoding.Float32, ColorByteOrder.LittleEndian));
        Assert.Throws<ArgumentOutOfRangeException>(() => SrgbGray<double>.PixelSize((ColorComponentEncoding)99, ColorByteOrder.LittleEndian));
    }

    /// <summary>Reference-containing custom numbers retain both explicit-space roles.</summary>
    [Fact]
    public void ManagedCustomNumberSupportsEverySpaceLayout()
    {
        Type custom = typeof(CustomNumberGeometryTests).GetNestedType("ManagedDecimal", System.Reflection.BindingFlags.NonPublic)!;
        typeof(TypedColorLayoutTests).GetMethod(nameof(Check), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.MakeGenericMethod(custom).Invoke(null, null);
        typeof(LinearTypedColorLayoutTests).GetMethod("Check", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.MakeGenericMethod(custom).Invoke(null, null);
    }

    private static void Check<T>()
        where T : INumber<T>
    {
        T one = T.One;
        T two = T.CreateChecked(2);
        T three = T.CreateChecked(3);
        T four = T.CreateChecked(4);
        var expected = new SrgbRgba<T>(one, two, three, four);
        Assert.Equal(expected, new SrgbRgb<T>(one, two, three).ToRgba(four));
        Assert.Equal(expected, new SrgbBgr<T>(three, two, one).ToRgba(four));
        Assert.Equal(expected, new SrgbBgra<T>(three, two, one, four).ToRgba());
        Assert.Equal(expected, new SrgbArgb<T>(four, one, two, three).ToRgba());
        Assert.Equal(expected, new SrgbAbgr<T>(four, three, two, one).ToRgba());
        Assert.Equal(new SrgbRgba<T>(two, two, two, four), new SrgbGrayAlpha<T>(two, four).ToRgba());
        Assert.Equal(new SrgbRgba<T>(two, two, two, four), new SrgbGray<T>(two).ToRgba(four));
        Assert.Equal(new SrgbRgba<double>(0.25, 0.5, 0.75, 1), new SrgbBgra<T>(three, two, one, four).NormalizeUnorm<double>(four));
        Assert.Equal(new SrgbRgba<double>(0.25, 0.5, 0.75, 1), new SrgbRgb<T>(one, two, three).NormalizeUnorm<double>(four));
        Assert.Equal(new SrgbRgba<double>(0.5, 0.5, 0.5, 1), new SrgbGray<T>(two).NormalizeUnorm<double>(four));
    }
}
