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
        var gray = new SGray<byte>(128);
        Assert.Equal(new SRgba<byte>(128, 128, 128, 255), gray.ToRgba(255));
        Assert.Equal(new SRgba<double>(128d / 255, 128d / 255, 128d / 255, 1), gray.NormalizeUnorm<double>(255));
        Assert.Equal(new SRgba<double>(0.5, 0.5, 0.5, 1), new SGray<double>(0.5).ToRgba(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => gray.WritePixel(new byte[1], ColorComponentEncoding.Unorm8, ColorByteOrder.LittleEndian));
        Assert.Throws<ArgumentOutOfRangeException>(() => SGray<double>.FromRgba(new(0, 1, 0, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => SRgb<byte>.FromRgba(new(128, 128, 128, 255)));
        Assert.Throws<ArgumentOutOfRangeException>(() => SGray<double>.FromRgba(new(0, 0, 0, 0.5)));
        Assert.Null(typeof(SGray<byte>).GetMethod("ToRgba", Type.EmptyTypes));
        Assert.Null(typeof(SRgb<byte>).GetMethod("ToRgba", Type.EmptyTypes));
        Assert.Null(typeof(SBgr<byte>).GetMethod("ToRgba", Type.EmptyTypes));
    }

    /// <summary>Finite construction and layout-preserving checked casts retain HDR without storage tags.</summary>
    [Fact]
    public void LayoutStorageDoesNotImplyUnitOrBinaryLayout()
    {
        Assert.True(RuntimeHelpers.IsReferenceOrContainsReferences<SGray<BigInteger>>());
        Assert.True(RuntimeHelpers.IsReferenceOrContainsReferences<SBgra<BigInteger>>());
        BigInteger huge = BigInteger.One << 20000;
        Assert.Equal(huge, new SGray<BigInteger>(huge).Value);
        Assert.Throws<OverflowException>(() => new SGray<BigInteger>(huge).ConvertChecked<double>());
        Assert.Equal(new SBgra<decimal>(-2, 3, 4, 5), new SBgra<int>(-2, 3, 4, 5).ConvertChecked<decimal>());
        Assert.Throws<ArgumentOutOfRangeException>(() => new SGray<double>(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SGrayAlpha<float>(0, float.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SBgra<double>(0, 0, double.NaN, 1));
        Assert.Throws<ArgumentException>(() => SGray<double>.PixelSize(ColorComponentEncoding.Float32, ColorByteOrder.LittleEndian));
        Assert.Throws<ArgumentOutOfRangeException>(() => SGray<double>.PixelSize((ColorComponentEncoding)99, ColorByteOrder.LittleEndian));
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
        var expected = new SRgba<T>(one, two, three, four);
        Assert.Equal(expected, new SRgb<T>(one, two, three).ToRgba(four));
        Assert.Equal(expected, new SBgr<T>(three, two, one).ToRgba(four));
        Assert.Equal(expected, new SBgra<T>(three, two, one, four).ToRgba());
        Assert.Equal(expected, new SArgb<T>(four, one, two, three).ToRgba());
        Assert.Equal(expected, new SAbgr<T>(four, three, two, one).ToRgba());
        Assert.Equal(new SRgba<T>(two, two, two, four), new SGrayAlpha<T>(two, four).ToRgba());
        Assert.Equal(new SRgba<T>(two, two, two, four), new SGray<T>(two).ToRgba(four));
        Assert.Equal(new SRgba<double>(0.25, 0.5, 0.75, 1), new SBgra<T>(three, two, one, four).NormalizeUnorm<double>(four));
        Assert.Equal(new SRgba<double>(0.25, 0.5, 0.75, 1), new SRgb<T>(one, two, three).NormalizeUnorm<double>(four));
        Assert.Equal(new SRgba<double>(0.5, 0.5, 0.5, 1), new SGray<T>(two).NormalizeUnorm<double>(four));
    }
}
