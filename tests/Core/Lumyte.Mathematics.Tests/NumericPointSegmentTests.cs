using System.Numerics;
using Lumyte.Mathematics.Geometry;
using Xunit;

namespace Lumyte.Mathematics.Tests;

/// <summary>Exercises ordered numeric point and segment storage without fractional truncation.</summary>
public sealed class NumericPointSegmentTests
{
    /// <summary>All built-in ordered numeric types support storage and representable metrics.</summary>
    [Fact]
    public void BuiltinNumericTypesSupportMetrics()
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
        Check<Half>();
        Check<float>();
        Check<double>();
        Check<decimal>();
    }

    /// <summary>Integer distances and projections never silently truncate.</summary>
    [Fact]
    public void FractionalIntegerResultsRequireExplicitPrecision()
    {
        var origin = new Point2D<int>(0, 0);
        var diagonal = new Point2D<int>(1, 1);
        Assert.Throws<ArithmeticException>(() => origin.DistanceTo(diagonal));
        Assert.Equal(Math.Sqrt(2), origin.DistanceTo<double>(diagonal), 14);
        Assert.Equal(2, origin.DistanceSquared(diagonal));
        var segment = new Segment2D<int>(origin, diagonal);
        var query = new Point2D<int>(1, 0);
        Assert.Throws<ArithmeticException>(() => segment.ClosestPoint(query));
        Assert.Throws<ArithmeticException>(() => segment.DistanceTo(query));
        Assert.Throws<ArithmeticException>(() => segment.DistanceSquared(query));
        Assert.Equal(new Point2D<decimal>(0.5m, 0.5m), segment.ClosestPoint<decimal>(query));
        Assert.Equal(0.5m, segment.DistanceSquared<decimal>(query));
        Assert.Equal(Math.Sqrt(0.5), segment.DistanceTo<double>(query), 14);
    }

    /// <summary>Unsigned subtraction and overflowing products do not corrupt predicates.</summary>
    [Fact]
    public void UnsignedAndDecimalIntersectionsAreExact()
    {
        var first = new Segment2D<ulong>(new(ulong.MaxValue, 0), new(0, ulong.MaxValue));
        var second = new Segment2D<ulong>(new(0, 0), new(ulong.MaxValue, ulong.MaxValue));
        Assert.True(first.Intersects(second));
        var decimalFirst = new Segment2D<decimal>(new(decimal.MinValue, 0), new(decimal.MaxValue, 0));
        Assert.True(decimalFirst.Intersects(new(new(0, -1), new(0, 1))));
        Assert.Equal(ulong.MaxValue, new Point2D<ulong>(ulong.MaxValue, 0).DistanceTo(default));
    }

    /// <summary>Huge integer coordinates retain low-order geometry far beyond double range.</summary>
    [Fact]
    public void BigIntegerGeometryDoesNotNarrowCoordinates()
    {
        var huge = BigInteger.Pow(10, 400);
        var start = new Point2D<BigInteger>(huge, huge);
        var end = new Point2D<BigInteger>(huge + 3, huge + 4);
        Assert.Equal(new BigInteger(5), start.DistanceTo(end));
        Assert.Equal(new BigInteger(25), start.DistanceSquared(end));
        var axis = new Segment2D<BigInteger>(start, new(huge + 10, huge));
        Assert.Equal(new Point2D<BigInteger>(huge + 3, huge), axis.ClosestPoint(end));
        Assert.Equal(new BigInteger(4), axis.DistanceTo(end));
        Assert.True(axis.Intersects(new(new(huge + 3, huge - 1), new(huge + 3, huge + 1))));
        Assert.Equal(huge * 5, new Point2D<BigInteger>(huge * 3, huge * 4).DistanceTo(default));
    }

    /// <summary>Decimal square roots stay decimal and avoid squared range overflow.</summary>
    [Fact]
    public void DecimalMetricsKeepDecimalPrecision()
    {
        decimal distance = new Point2D<decimal>(1, 1).DistanceTo(default);
        Assert.InRange(distance, 1.414213562373095048801688724m, 1.414213562373095048801688725m);
        Assert.Equal(decimal.MaxValue, new Point2D<decimal>(decimal.MaxValue, 0).DistanceTo(default));
        Assert.Equal(0.0000000000000000000000000001m, new Point2D<decimal>(0.0000000000000000000000000001m, 0).DistanceTo(default));
    }

    /// <summary>Three-dimensional projection supports integral and fractional result choices.</summary>
    [Fact]
    public void ThreeDimensionalMetricsSupportExplicitResults()
    {
        var segment = new Segment3D<int>(new(0, 0, 0), new(1, 1, 0));
        var query = new Point3D<int>(1, 0, 2);
        Assert.Throws<ArithmeticException>(() => segment.ClosestPoint(query));
        Assert.Equal(new Point3D<double>(0.5, 0.5, 0), segment.ClosestPoint<double>(query));
        Assert.Equal(4.5m, segment.DistanceSquared<decimal>(query));
        Assert.Equal(Math.Sqrt(4.5), segment.DistanceTo<double>(query), 14);
        Assert.Equal(new Point3D<decimal>(1, 0, 2), query.ConvertChecked<decimal>());
        Assert.Throws<OverflowException>(() => new Point2D<int>(256, 0).ConvertChecked<byte>());
    }

    /// <summary>Explicit floating results scale huge intermediates and tiny projection parameters.</summary>
    [Fact]
    public void ExplicitMetricsScaleBeforeConversion()
    {
        var huge = BigInteger.Pow(10, 400);
        var segment = new Segment2D<BigInteger>(default, new(huge, 0));
        Assert.Equal(new Point2D<double>(1, 0), segment.ClosestPoint<double>(new(1, huge)));
        var large = BigInteger.Pow(10, 200);
        double distance = new Point2D<BigInteger>(large, large).DistanceTo<double>(default);
        Assert.InRange(distance / 1e200, 1.414213562373094, 1.414213562373096);
        var oblique = new Segment2D<BigInteger>(default, new(huge, 1));
        double squared = oblique.DistanceSquared<double>(new(1, 1));
        Assert.InRange(squared, 0.99999999999999, 1.00000000000001);
    }

    /// <summary>Explicit metrics convert only the final exact result, never the input coordinates.</summary>
    [Fact]
    public void ExplicitIeeeMetricsDoNotTruncateCoordinates()
    {
        Assert.Throws<ArithmeticException>(() => new Point2D<double>(0.9, 0).DistanceSquared<int>(default));
        Assert.Throws<ArithmeticException>(() => new Point2D<double>(1.9, 0).DistanceTo<int>(default));
        Assert.Throws<ArithmeticException>(() => new Point3D<double>(0.9, 0, 0).DistanceSquared<int>(default));
        Assert.Throws<ArithmeticException>(() => new Point3D<double>(1.9, 0, 0).DistanceTo<int>(default));
        Assert.Equal(3u, default(Point2D<double>).DistanceTo<uint>(new(3, 0)));
        Assert.Equal(49u, default(Point3D<double>).DistanceSquared<uint>(new(2, 3, 6)));
        Assert.Equal(1f, new Point2D<double>(16777217, 0).DistanceTo<float>(new(16777216, 0)));
        Assert.Equal(1f, new Point3D<double>(16777217, 0, 0).DistanceSquared<float>(new(16777216, 0, 0)));
        Assert.Equal(1m, new Point2D<double>(double.Epsilon, 0).DistanceTo<decimal>(new(1, 0)));
        Assert.Equal(1, new Point2D<Half>((Half)1, (Half)0).DistanceTo<int>(default));
        Assert.Throws<ArithmeticException>(() => new Point2D<float>(0.5f, 0).DistanceTo<int>(default));
    }

    /// <summary>Projection and distances retain the original IEEE coordinates for explicit result types.</summary>
    [Fact]
    public void ExplicitIeeeSegmentsDoNotTruncateCoordinates()
    {
        var segment = new Segment2D<double>(new(0.9, 0), new(2.9, 0));
        Assert.Throws<ArithmeticException>(() => segment.ClosestPoint<int>(new(1.9, 0)));
        Assert.Throws<ArithmeticException>(() => segment.DistanceTo<int>(new(1.9, 0.9)));
        Assert.Throws<ArithmeticException>(() => segment.DistanceSquared<int>(new(1.9, 0.9)));
        var space = new Segment3D<double>(new(0.9, 0, 0), new(2.9, 0, 0));
        Assert.Throws<ArithmeticException>(() => space.ClosestPoint<int>(new(1.9, 0, 0)));
        Assert.Throws<ArithmeticException>(() => space.DistanceTo<int>(new(1.9, 0.9, 0)));
        Assert.Throws<ArithmeticException>(() => space.DistanceSquared<int>(new(1.9, 0.9, 0)));
        var narrow = new Segment2D<double>(new(16777216, 0), new(16777218, 0));
        Assert.Equal(1f, narrow.DistanceTo<float>(new(16777217, 1)));
        Assert.Equal(1u, segment.DistanceTo<uint>(new(1.9, 1)));
    }

    /// <summary>Decimal rational conversion rounds once to a 96-bit mantissa using nearest-even.</summary>
    [Fact]
    public void DecimalRationalMetricsRoundOnceToNearestEven()
    {
        var thirds = new Segment3D<int>(default, new(1, 1, 1));
        Assert.Equal(0.6666666666666666666666666667m, thirds.ClosestPoint<decimal>(new(2, 0, 0)).X);
        var large = new Segment3D<BigInteger>(new(new BigInteger(10000000000000000000000000000m), 0, 0), new(new BigInteger(10000000000000000000000000000m) + 1, 1, 1));
        Assert.Equal(10000000000000000000000000001m, large.ClosestPoint<decimal>(new(new BigInteger(10000000000000000000000000000m) + 2, 0, 0)).X);
        var offset = new BigInteger(10000000000000000000000000000m);
        var halfway = new Segment2D<BigInteger>(new(offset, 0), new(offset + 1, 1));
        Assert.Equal(10000000000000000000000000000m, halfway.ClosestPoint<decimal>(new(offset + 1, 0)).X);
        var halfwayOdd = new Segment2D<BigInteger>(new(offset + 1, 0), new(offset + 2, 1));
        Assert.Equal(10000000000000000000000000002m, halfwayOdd.ClosestPoint<decimal>(new(offset + 2, 0)).X);
        const decimal Tiny = 0.0000000000000000000000000001m;
        Assert.Equal(Tiny, new Point2D<decimal>(Tiny, Tiny).DistanceTo(default));
        Assert.Equal(2 * Tiny, new Point3D<decimal>(Tiny, Tiny, Tiny).DistanceTo(default));
    }

    /// <summary>IEEE results round the full rational once, including signed halfway cases.</summary>
    [Fact]
    public void IeeeRationalMetricsRoundOnceToTargetPrecision()
    {
        Assert.Equal(16777218f, new Point2D<decimal>(16777217.1m, 0).DistanceTo<float>(default));
        Assert.Equal(9007199254740994d, new Point2D<decimal>(9007199254740993.1m, 0).DistanceTo<double>(default));
        Assert.Equal((Half)2050, new Point2D<decimal>(2049.1m, 0).DistanceTo<Half>(default));
        var segment = new Segment2D<decimal>(new(16777216, 0), new(16777218, 0));
        Assert.Equal(16777218f, segment.ClosestPoint<float>(new(16777217.1m, 0)).X);
        CheckRationalRounding<float>(16777216m);
        CheckRationalRounding<double>(9007199254740992m);
        CheckRationalRounding<Half>(2048m);
        CheckBinaryBoundaries<Half>(10, 15);
        CheckBinaryBoundaries<float>(23, 127);
        CheckBinaryBoundaries<double>(52, 1023);
    }

    /// <summary>Factories reject nonfinite values and reset failed outputs.</summary>
    [Fact]
    public void CoordinateFactoriesRejectNonfiniteValues()
    {
        CheckNonfinite<Half>();
        CheckNonfinite<float>();
        CheckNonfinite<double>();
    }

    /// <summary>Span copies retain reference-containing arbitrary-precision coordinates without aliasing.</summary>
    [Fact]
    public void BigIntegerCoordinateSpansPreserveFullPrecision()
    {
        var huge = BigInteger.Pow(10, 400);
        BigInteger[] coordinates = [huge, huge + 1, huge + 2];
        var plane = Point2D<BigInteger>.FromCoordinates(coordinates.AsSpan(0, 2));
        var space = Point3D<BigInteger>.FromCoordinates(coordinates);
        coordinates[0] = 0;
        var destination = new BigInteger[3];
        space.CopyTo(destination);
        Assert.Equal(huge, destination[0]);
        Assert.Equal(huge + 1, destination[1]);
        Assert.Equal(huge + 2, destination[2]);
        plane.CopyTo(destination);
        Assert.Equal(huge, destination[0]);
        Assert.Equal(huge + 2, destination[2]);
    }

    private static void CheckRationalRounding<T>(decimal lower)
        where T : IFloatingPointIeee754<T>
    {
        foreach (decimal sign in new[] { 1m, -1m })
        {
            var segment = new Segment2D<decimal>(new(sign * lower, 0), new(sign * (lower + 4), 0));
            Assert.Equal(T.CreateChecked(sign * lower), segment.ClosestPoint<T>(new(sign * (lower + 1), 0)).X);
            Assert.Equal(T.CreateChecked(sign * (lower + 4)), segment.ClosestPoint<T>(new(sign * (lower + 3), 0)).X);
            Assert.Equal(T.CreateChecked(sign * (lower + 2)), segment.ClosestPoint<T>(new(sign * (lower + 1.1m), 0)).X);
        }

        // A decimal perturbation smaller than double/float precision must not disappear
        // in a wider floating intermediate before rounding to float/Half.
        decimal midpoint = typeof(T) == typeof(Half) ? 1.00048828125m : 1.000000059604644775390625m;
        if (typeof(T) != typeof(double))
        {
            var segment = new Segment2D<decimal>(new(1, 0), new(2, 0));
            Assert.Equal(T.BitIncrement(T.One), segment.ClosestPoint<T>(new(midpoint + 0.0000000000000000000000000001m, 0)).X);
        }
    }

    private static void CheckBinaryBoundaries<T>(int fractionBits, int bias)
        where T : IFloatingPointIeee754<T>
    {
        // Projection onto (n, 1) produces y = k / (n*n + 1).
        int subnormalBits = bias + fractionBits - 1;
        BigInteger n = BigInteger.One << ((subnormalBits / 2) + 1);
        BigInteger unit = BigInteger.One << ((2 * ((subnormalBits / 2) + 1)) - subnormalBits);
        var segment = new Segment2D<BigInteger>(default, new(n, 1));
        T epsilon = T.BitIncrement(T.Zero);
        Assert.Equal(epsilon, segment.ClosestPoint<T>(new(0, unit)).Y);
        Assert.Equal(T.Zero, segment.ClosestPoint<T>(new(0, unit / 2)).Y);
        var negative = new Segment2D<BigInteger>(default, new(n, -1));
        Assert.Equal(-epsilon, negative.ClosestPoint<T>(new(0, -unit)).Y);
        Assert.True(T.IsNegative(negative.ClosestPoint<T>(new(0, -unit / 2)).Y));

        // An axis projection exposes exact binary halfway values, including the
        // transition from the largest subnormal to the smallest normal.
        double targetEpsilon = double.CreateChecked(epsilon);
        if (typeof(T) != typeof(double))
        {
            var binary = new Segment2D<double>(new(-1, 0), new(1, 0));
            foreach (int sign in new[] { 1, -1 })
            {
                T zero = binary.ClosestPoint<T>(new(sign * targetEpsilon / 2, 0)).X;
                Assert.Equal(T.Zero, zero);
                Assert.Equal(sign < 0, T.IsNegative(zero));
                Assert.Equal(epsilon + epsilon, binary.ClosestPoint<T>(new(sign * 1.5 * targetEpsilon, 0)).X * T.CreateChecked(sign));
                double minimumNormal = double.ScaleB(targetEpsilon, fractionBits);
                Assert.Equal(T.CreateChecked(sign * minimumNormal), binary.ClosestPoint<T>(new(sign * (minimumNormal - (targetEpsilon / 2)), 0)).X);
            }
        }

        // Exact integer midpoints at the finite/infinite boundary round to even.
        BigInteger max = ((BigInteger.One << (fractionBits + 1)) - 1) << (bias - fractionBits);
        BigInteger halfUlp = BigInteger.One << (bias - fractionBits - 1);
        foreach (int sign in new[] { 1, -1 })
        {
            var boundary = new Segment2D<BigInteger>(new(sign * (max - (2 * halfUlp)), 0), new(sign * (max + (2 * halfUlp)), 0));
            Assert.Equal(T.CreateChecked(sign * max), boundary.ClosestPoint<T>(new(sign * (max + halfUlp - 1), 0)).X);
            Assert.Throws<OverflowException>(() => boundary.ClosestPoint<T>(new(sign * (max + halfUlp), 0)));
            Assert.Throws<OverflowException>(() => boundary.ClosestPoint<T>(new(sign * (max + halfUlp + 1), 0)));
        }
    }

    private static void CheckNonfinite<T>()
        where T : IFloatingPointIeee754<T>
    {
        foreach (T invalid in new[] { T.NaN, T.PositiveInfinity, T.NegativeInfinity })
        {
            Assert.False(Point2D<T>.TryCreate(invalid, T.Zero, out Point2D<T> plane));
            Assert.Equal(default, plane);
            Assert.False(Point2D<T>.TryCreate(T.Zero, invalid, out plane));
            Assert.Equal(default, plane);
            Assert.False(Point3D<T>.TryCreate(invalid, T.Zero, T.Zero, out Point3D<T> space));
            Assert.Equal(default, space);
            Assert.False(Point3D<T>.TryCreate(T.Zero, invalid, T.Zero, out space));
            Assert.Equal(default, space);
            Assert.False(Point3D<T>.TryCreate(T.Zero, T.Zero, invalid, out space));
            Assert.Equal(default, space);
            Assert.Throws<ArgumentOutOfRangeException>(() => Point2D<T>.FromCoordinates(new[] { T.Zero, invalid }));
            Assert.Throws<ArgumentOutOfRangeException>(() => Point3D<T>.FromCoordinates(new[] { T.Zero, T.Zero, invalid }));
        }
    }

    private static void CheckCoordinates<T>()
        where T : INumber<T>
    {
        T[] coordinates = [T.One, T.CreateChecked(2), T.CreateChecked(3)];
        Assert.Equal(new Point2D<T>(T.Zero, T.Zero), Point2D<T>.Origin);
        Assert.Equal(new Point3D<T>(T.Zero, T.Zero, T.Zero), Point3D<T>.Origin);
        Assert.True(Point2D<T>.TryCreate(coordinates[0], coordinates[1], out Point2D<T> plane));
        Assert.Equal(plane, Point2D<T>.FromCoordinates(coordinates.AsSpan(0, 2)));
        Assert.True(Point3D<T>.TryCreate(coordinates[0], coordinates[1], coordinates[2], out Point3D<T> space));
        Assert.Equal(space, Point3D<T>.FromCoordinates(coordinates));
        (T x, T y) = plane;
        Assert.Equal(coordinates[0], x);
        Assert.Equal(coordinates[1], y);
        (T spaceX, T spaceY, T z) = space;
        Assert.Equal(x, spaceX);
        Assert.Equal(y, spaceY);
        Assert.Equal(coordinates[2], z);
        T[] destination = [T.Zero, T.Zero, T.Zero, T.CreateChecked(9)];
        plane.CopyTo(destination.AsSpan(0, 2));
        Assert.Equal(x, destination[0]);
        Assert.Equal(y, destination[1]);
        space.CopyTo(destination);
        Assert.Equal(coordinates[2], destination[2]);
        Assert.Equal(T.CreateChecked(9), destination[3]);
        for (int length = 0; length < 5; length++)
        {
            var input = new T[length];
            if (length != 2)
            {
                Assert.Equal("coordinates", Assert.Throws<ArgumentException>(() => Point2D<T>.FromCoordinates(input)).ParamName);
            }

            if (length != 3)
            {
                Assert.Equal("coordinates", Assert.Throws<ArgumentException>(() => Point3D<T>.FromCoordinates(input)).ParamName);
            }

            T[] rejected = [T.CreateChecked(9), T.CreateChecked(9)];
            if (length < 2)
            {
                Assert.Equal("destination", Assert.Throws<ArgumentException>(() => plane.CopyTo(rejected.AsSpan(0, length))).ParamName);
                Assert.Equal(T.CreateChecked(9), rejected[0]);
                Assert.Equal(T.CreateChecked(9), rejected[1]);
            }

            if (length < 3)
            {
                Assert.Equal("destination", Assert.Throws<ArgumentException>(() => space.CopyTo(rejected.AsSpan(0, length))).ParamName);
                Assert.Equal(T.CreateChecked(9), rejected[0]);
                Assert.Equal(T.CreateChecked(9), rejected[1]);
            }
        }
    }

    private static void Check<T>()
        where T : INumber<T>
    {
        CheckCoordinates<T>();
        var a = new Point2D<T>(T.CreateChecked(3), T.CreateChecked(4));
        Assert.Equal(T.CreateChecked(5), a.DistanceTo(default));
        Assert.Equal(T.CreateChecked(25), a.DistanceSquared(default));
        var b = new Point3D<T>(T.CreateChecked(2), T.CreateChecked(3), T.CreateChecked(6));
        Assert.Equal(T.CreateChecked(7), b.DistanceTo(default));
        var segment = new Segment2D<T>(default, new(T.CreateChecked(10), T.Zero));
        Assert.Equal(new Point2D<T>(T.CreateChecked(3), T.Zero), segment.ClosestPoint(a));
        Assert.Equal(T.CreateChecked(4), segment.DistanceTo(a));
    }
}
