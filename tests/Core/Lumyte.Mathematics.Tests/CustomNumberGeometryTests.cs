using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using Lumyte.Mathematics.Colors;
using Lumyte.Mathematics.Geometry;
using Xunit;

namespace Lumyte.Mathematics.Tests;

/// <summary>Exercises genuine managed custom numeric storage and native generic arithmetic.</summary>
public sealed class CustomNumberGeometryTests
{
    /// <summary>The numeric fixture is not unmanaged, and its default is a valid zero.</summary>
    [Fact]
    public void CustomNumberHasManagedStorageAndDecimalSemantics()
    {
        Assert.True(RuntimeHelpers.IsReferenceOrContainsReferences<ManagedDecimal>());
        Assert.Equal(ManagedDecimal.Zero, default);
        Assert.Equal(N(1.25m), ManagedDecimal.Parse("1.25", CultureInfo.InvariantCulture));
        Assert.Equal(N(3.75m), checked(N(1.25m) + N(2.5m)));
        Assert.Equal(1.25m, decimal.CreateChecked(N(1.25m)));
        Assert.Equal(N(1.25m), ManagedDecimal.CreateChecked(1.25m));
        Assert.Equal(N(255), ManagedDecimal.CreateSaturating(255));
        Assert.Equal((byte)255, byte.CreateSaturating(N(256)));
        Assert.Equal(byte.CreateTruncating(256m), byte.CreateTruncating(N(256)));
        Assert.Throws<OverflowException>(() => byte.CreateChecked(N(256)));
        Assert.Throws<OverflowException>(() => checked(N(decimal.MaxValue) + N(1)));
    }

    /// <summary>Points and segments retain native squared metrics and support explicit roots.</summary>
    [Fact]
    public void PointsAndSegmentsUseCustomArithmeticAndExplicitMetrics()
    {
        var point = new Point2D<ManagedDecimal>(N(3), N(4));
        Assert.Equal(N(25), point.DistanceSquared(default));
        Assert.Equal(25m, point.DistanceSquared<decimal>(default));
        Assert.Equal(5m, point.DistanceTo<decimal>(default));
        Assert.Equal(5d, point.DistanceTo<double>(default));
        Assert.Throws<NotSupportedException>(() => point.DistanceTo(default));
        Assert.Equal(new Point2D<decimal>(3, 4), point.ConvertChecked<decimal>());
        var point3 = new Point3D<ManagedDecimal>(N(2), N(3), N(6));
        Assert.Equal(N(49), point3.DistanceSquared(default));
        Assert.Equal(7m, point3.DistanceTo<decimal>(default));
        Assert.Equal(7d, point3.DistanceTo<double>(default));
        Assert.Throws<NotSupportedException>(() => point3.DistanceTo(default));
        var segment = new Segment2D<ManagedDecimal>(default, new(N(1), N(1)));
        var query = new Point2D<ManagedDecimal>(N(1), N(0));
        Assert.Equal(new Point2D<ManagedDecimal>(N(0.5m), N(0.5m)), segment.ClosestPoint(query));
        Assert.Equal(N(0.5m), segment.DistanceSquared(query));
        Assert.Equal(0.5m, segment.DistanceSquared<decimal>(query));
        Assert.Equal(Math.Sqrt(0.5), segment.DistanceTo<double>(query), 14);
        Assert.InRange(segment.DistanceTo<decimal>(query), 0.707106781186547524400844362m, 0.707106781186547524400844363m);
        Assert.True(segment.Intersects(new(new(N(0), N(1)), new(N(1), N(0)))));
        Assert.False(segment.Intersects(new(new(N(2), N(0)), new(N(2), N(1)))));
        Assert.Throws<NotSupportedException>(() => segment.DistanceTo(query));
        var segment3 = new Segment3D<ManagedDecimal>(default, new(N(1), N(1), N(0)));
        var query3 = new Point3D<ManagedDecimal>(N(1), N(0), N(2));
        Assert.Equal(new Point3D<ManagedDecimal>(N(0.5m), N(0.5m), N(0)), segment3.ClosestPoint(query3));
        Assert.Equal(N(4.5m), segment3.DistanceSquared(query3));
        Assert.Equal(4.5m, segment3.DistanceSquared<decimal>(query3));
        Assert.Equal(Math.Sqrt(4.5), segment3.DistanceTo<double>(query3), 14);
        Assert.InRange(segment3.DistanceTo<decimal>(query3), 2.121320343559642573202533086m, 2.121320343559642573202533087m);
        Assert.Throws<NotSupportedException>(() => segment3.DistanceTo(query3));
    }

    /// <summary>All shape families accept reference-containing custom scalars and perform predicates.</summary>
    [Fact]
    public void ShapesUseCustomStoragePredicatesAndFactories()
    {
        var rect = new Rect2D<ManagedDecimal>(N(0), N(0), N(4), N(4));
        Assert.True(rect.Contains(new(N(4), N(4))));
        Assert.False(rect.ContainsHalfOpen(new(N(4), N(4))));
        Assert.Equal(rect, Rect2D<ManagedDecimal>.FromCenterSize(new(N(2), N(2)), N(4), N(4)));
        Assert.Equal(rect, rect.Offset(N(1), N(2)).OffsetInverse(N(1), N(2)));
        Assert.Equal(new Rect2D<decimal>(0, 0, 4, 4), rect.ConvertChecked<decimal>());
        var bounds = new Bounds2D<ManagedDecimal>(N(0), N(0), N(4), N(4));
        Assert.True(bounds.Contains(N(2), N(3)));
        Assert.False(bounds.Contains(N(5), N(3)));
        Assert.True(Bounds2D<ManagedDecimal>.Empty.IsEmpty);
        Assert.False(Bounds2D<ManagedDecimal>.TryGetUnbounded(out _));
        var box = AxisAlignedBox3D<ManagedDecimal>.FromCenterSize(new(N(2), N(2), N(2)), N(4), N(4), N(4));
        Assert.True(box.Contains(new(N(4), N(4), N(4))));
        Assert.False(box.Contains(new(N(5), N(4), N(4))));
        Assert.Equal(box, box.Union(default));
        Assert.True(new Circle2D<ManagedDecimal>(default, N(5)).Contains(new(N(3), N(4))));
        Assert.False(new Circle2D<ManagedDecimal>(default, N(4)).Contains(new(N(3), N(4))));
        Assert.True(new Sphere3D<ManagedDecimal>(default, N(7)).Contains(new(N(2), N(3), N(6))));
        Assert.False(new Sphere3D<ManagedDecimal>(default, N(6)).Contains(new(N(2), N(3), N(6))));
        Assert.True(new Ellipse2D<ManagedDecimal>(default, N(5), N(4)).Contains(new(N(0), N(4))));
        Assert.False(new Ellipse2D<ManagedDecimal>(default, N(5), N(4)).Contains(new(N(5), N(4))));
        var rounded = new RoundedRect2D<ManagedDecimal>(rect, N(2));
        Assert.True(rounded.Contains(new(N(0), N(2))));
        Assert.False(rounded.Contains(default));
        Assert.True(default(Circle2D<ManagedDecimal>).Contains(default));
        Assert.True(default(Sphere3D<ManagedDecimal>).Contains(default));
        Assert.True(default(Ellipse2D<ManagedDecimal>).Contains(default));
        Assert.True(default(RoundedRect2D<ManagedDecimal>).Contains(default));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Circle2D<ManagedDecimal>(default, N(-1)));
    }

    /// <summary>Polygon winding, Bezier interpolation, flattening and affine transforms are generic.</summary>
    [Fact]
    public void PolygonsCurvesAndAffineTransformsSupportCustomNumbers()
    {
        Point2D<ManagedDecimal>[] polygon = [default, new(N(4), N(0)), new(N(4), N(4)), new(N(0), N(4))];
        Assert.True(Geometry2D.ContainsPolygonClosed<ManagedDecimal>(polygon, new(N(2), N(2))));
        Assert.True(Geometry2D.ContainsPolygonEvenOddClosed<ManagedDecimal>(polygon, new(N(0), N(2))));
        Assert.False(Geometry2D.ContainsPolygonClosed<ManagedDecimal>(polygon, new(N(5), N(2))));
        Point2D<ManagedDecimal> start = default;
        var control = new Point2D<ManagedDecimal>(N(2), N(4));
        var end = new Point2D<ManagedDecimal>(N(4), N(0));
        Assert.Equal(new Point2D<ManagedDecimal>(N(2), N(2)), Geometry2D.EvaluateQuadraticBezier(start, control, end, N(0.5m)));
        Assert.Equal(new Point2D<ManagedDecimal>(N(2), N(3)), Geometry2D.EvaluateCubicBezier(start, new(N(0), N(4)), new(N(4), N(4)), end, N(0.5m)));
        Assert.Equal(new Point2D<decimal>(2, 2), Geometry2D.EvaluateQuadraticBezier<ManagedDecimal, decimal>(start, control, end, 0.5m));
        var polyline = new List<Point2D<double>>();
        Assert.True(Geometry2D.TryFlattenQuadraticBezier<ManagedDecimal, double>(start, control, end, 0.1, polyline, 12, 1024));
        Assert.Equal(new Point2D<double>(0, 0), polyline[0]);
        Assert.Equal(new Point2D<double>(4, 0), polyline[^1]);
        Assert.True(polyline.Count > 2);
        var cubic = new List<Point2D<decimal>>();
        Assert.True(Geometry2D.TryFlattenCubicBezier<ManagedDecimal, decimal>(start, new(N(0), N(4)), new(N(4), N(4)), end, 0.1m, cubic, 12, 1024));
        Assert.True(cubic.Count > 2);
        var point = new Point2D<ManagedDecimal>(N(2), N(3));
        Assert.Equal(new Point2D<ManagedDecimal>(N(8), N(8)), Geometry2D.TransformAffineChecked(point, N(2), N(0), N(0), N(2), N(4), N(2)));
        var matrix = new Matrix3x2(0.5f, 0, 0, 2, 1, -1);
        Assert.Equal(new Point2D<ManagedDecimal>(N(2), N(5)), Geometry2D.TransformAffineFused(point, matrix));
        Assert.Equal(new Point2D<decimal>(2, 5), Geometry2D.TransformAffineFused<ManagedDecimal, decimal>(point, matrix));
    }

    /// <summary>Cross-type metrics preserve custom fractions rather than truncating coordinates.</summary>
    [Fact]
    public void CustomCrossTypeMetricsRejectFractionalIntegralResults()
    {
        var point = new Point2D<ManagedDecimal>(N(0.9m), N(0));
        var point3 = new Point3D<ManagedDecimal>(N(0.9m), N(0), N(0));
        Assert.Throws<ArithmeticException>(() => point.DistanceSquared<int>(default));
        Assert.Throws<ArithmeticException>(() => point.DistanceTo<int>(default));
        Assert.Throws<ArithmeticException>(() => point3.DistanceSquared<int>(default));
        Assert.Throws<ArithmeticException>(() => point3.DistanceTo<int>(default));
        Assert.Equal(0.81m, point.DistanceSquared<decimal>(default));
        Assert.Equal(0.81m, point3.DistanceSquared<decimal>(default));
        var segment = new Segment2D<ManagedDecimal>(default, new(N(1), N(1)));
        var query = new Point2D<ManagedDecimal>(N(1), N(0));
        var segment3 = new Segment3D<ManagedDecimal>(default, new(N(1), N(1), N(0)));
        var query3 = new Point3D<ManagedDecimal>(N(1), N(0), N(0));
        Assert.Throws<ArithmeticException>(() => segment.ClosestPoint<int>(query));
        Assert.Throws<ArithmeticException>(() => segment.DistanceSquared<int>(query));
        Assert.Throws<ArithmeticException>(() => segment.DistanceTo<int>(query));
        Assert.Throws<ArithmeticException>(() => segment3.ClosestPoint<int>(query3));
        Assert.Throws<ArithmeticException>(() => segment3.DistanceSquared<int>(query3));
        Assert.Throws<ArithmeticException>(() => segment3.DistanceTo<int>(query3));
        Assert.Equal(0.5m, segment.DistanceSquared<decimal>(query));
        Assert.Equal(0.5m, segment3.DistanceSquared<decimal>(query3));
    }

    /// <summary>Cross-type arithmetic subtracts before narrowing and permits unsigned distances.</summary>
    [Fact]
    public void CustomCrossTypeMetricsPreserveOffsetsAndUnsignedDifferences()
    {
        const decimal Offset = 100000000000000000000m;
        var start = new Point2D<ManagedDecimal>(N(Offset), N(Offset));
        var end = new Point2D<ManagedDecimal>(N(Offset + 3), N(Offset + 4));
        var start3 = new Point3D<ManagedDecimal>(N(Offset), N(Offset), N(Offset));
        var end3 = new Point3D<ManagedDecimal>(N(Offset + 2), N(Offset + 3), N(Offset + 6));
        Assert.Equal(25f, start.DistanceSquared<float>(end));
        Assert.Equal(5f, start.DistanceTo<float>(end));
        Assert.Equal(49f, start3.DistanceSquared<float>(end3));
        Assert.Equal(7f, start3.DistanceTo<float>(end3));
        Assert.Equal(25u, start.DistanceSquared<uint>(end));
        Assert.Equal(5u, start.DistanceTo<uint>(end));
        Assert.Equal(49u, start3.DistanceSquared<uint>(end3));
        Assert.Equal(7u, start3.DistanceTo<uint>(end3));
        var segment = new Segment2D<ManagedDecimal>(start, new(N(Offset + 4), N(Offset)));
        var query = new Point2D<ManagedDecimal>(N(Offset + 2), N(Offset + 3));
        var segment3 = new Segment3D<ManagedDecimal>(start3, new(N(Offset + 4), N(Offset), N(Offset)));
        var query3 = new Point3D<ManagedDecimal>(N(Offset + 2), N(Offset + 3), N(Offset + 4));
        Assert.Equal(9f, segment.DistanceSquared<float>(query));
        Assert.Equal(3f, segment.DistanceTo<float>(query));
        Assert.Equal(25f, segment3.DistanceSquared<float>(query3));
        Assert.Equal(5f, segment3.DistanceTo<float>(query3));
        Assert.Equal(9u, segment.DistanceSquared<uint>(query));
        Assert.Equal(3u, segment.DistanceTo<uint>(query));
        Assert.Equal(25u, segment3.DistanceSquared<uint>(query3));
        Assert.Equal(5u, segment3.DistanceTo<uint>(query3));
    }

    /// <summary>Color storage and exact normalization support reference-containing custom numbers.</summary>
    [Fact]
    public void ColorsSupportManagedCustomNumbers()
    {
        Assert.True(RuntimeHelpers.IsReferenceOrContainsReferences<SrgbRgba<ManagedDecimal>>());
        Assert.True(RuntimeHelpers.IsReferenceOrContainsReferences<SrgbGray<ManagedDecimal>>());
        var gray = new SrgbGray<ManagedDecimal>(N(128));
        Assert.Equal(new SrgbRgba<decimal>(128m / 255, 128m / 255, 128m / 255, 1), gray.NormalizeUnorm<decimal>(N(255)));
        Assert.Equal(N(255), gray.ToRgba(N(255)).A);
        var rgba = new SrgbRgba<ManagedDecimal>(N(0), N(1), N(0), N(1));
        var bgra = SrgbBgra<ManagedDecimal>.FromRgba(rgba);
        byte[] encoded = new byte[4];
        Lumyte.Mathematics.Colors.Color.WritePixels<SrgbBgra<ManagedDecimal>>(new[] { bgra }, encoded, ColorComponentEncoding.Unorm8, ColorByteOrder.BigEndian);
        var decoded = new SrgbBgra<ManagedDecimal>[1];
        Lumyte.Mathematics.Colors.Color.DecodePixels<SrgbBgra<ManagedDecimal>>(encoded, decoded, ColorComponentEncoding.Unorm8, ColorByteOrder.BigEndian);
        Assert.Equal(bgra, decoded[0]);
        var codes = SrgbRgba<ManagedDecimal>.FromRgba8Codes(1, 2, 3, 4);
        Assert.Equal(new SrgbRgba<decimal>(0.25m, 0.5m, 0.75m, 1), codes.NormalizeUnorm<decimal>(N(4)));
        Assert.Equal(new SrgbRgba<int>(1, 2, 3, 4), codes.NormalizeUnorm<ManagedDecimal>(N(4)).QuantizeUnorm(4));
        Assert.Equal(0x04010203u, codes.ToArgb32Codes());
        Assert.Equal(N(99), codes.WithAlpha(N(99)).A);
        Assert.Equal(new SrgbRgba<decimal>(1, 2, 3, 4), codes.ConvertChecked<decimal>());
    }

    /// <summary>Color storage and exact normalization support reference-containing custom numbers.</summary>
    [Fact]
    public void LinearColorsSupportManagedCustomNumbers()
    {
        Assert.True(RuntimeHelpers.IsReferenceOrContainsReferences<LinearRgba<ManagedDecimal>>());
        Assert.True(RuntimeHelpers.IsReferenceOrContainsReferences<LinearGray<ManagedDecimal>>());
        var gray = new LinearGray<ManagedDecimal>(N(128));
        Assert.Equal(new LinearRgba<decimal>(128m / 255, 128m / 255, 128m / 255, 1), gray.NormalizeUnorm<decimal>(N(255)));
        Assert.Equal(N(255), gray.ToRgba(N(255)).A);
        var rgba = new LinearRgba<ManagedDecimal>(N(0), N(1), N(0), N(1));
        var bgra = LinearBgra<ManagedDecimal>.FromRgba(rgba);
        byte[] encoded = new byte[4];
        Lumyte.Mathematics.Colors.Color.WritePixels<LinearBgra<ManagedDecimal>>(new[] { bgra }, encoded, ColorComponentEncoding.Unorm8, ColorByteOrder.BigEndian);
        var decoded = new LinearBgra<ManagedDecimal>[1];
        Lumyte.Mathematics.Colors.Color.DecodePixels<LinearBgra<ManagedDecimal>>(encoded, decoded, ColorComponentEncoding.Unorm8, ColorByteOrder.BigEndian);
        Assert.Equal(bgra, decoded[0]);
        var codes = LinearRgba<ManagedDecimal>.FromRgba8Codes(1, 2, 3, 4);
        Assert.Equal(new LinearRgba<decimal>(0.25m, 0.5m, 0.75m, 1), codes.NormalizeUnorm<decimal>(N(4)));
        Assert.Equal(new LinearRgba<int>(1, 2, 3, 4), codes.NormalizeUnorm<ManagedDecimal>(N(4)).QuantizeUnorm(4));
        Assert.Equal(0x04010203u, codes.ToArgb32Codes());
        Assert.Equal(N(99), codes.WithAlpha(N(99)).A);
        Assert.Equal(new LinearRgba<decimal>(1, 2, 3, 4), codes.ConvertChecked<decimal>());
    }

    private static ManagedDecimal N(decimal value) => new(value);

    /// <summary>A decimal-backed number with a reference field, deliberately not unmanaged.</summary>
    private readonly struct ManagedDecimal(decimal value) : INumber<ManagedDecimal>
    {
        private readonly decimal _value = value;
        private readonly string? _representation = value.ToString(CultureInfo.InvariantCulture);

        /// <summary>Gets the additive identity.</summary>
        public static ManagedDecimal AdditiveIdentity => Zero;

        /// <summary>Gets the multiplicative identity.</summary>
        public static ManagedDecimal MultiplicativeIdentity => One;

        /// <summary>Gets zero.</summary>
        public static ManagedDecimal Zero => new(0);

        /// <summary>Gets one.</summary>
        public static ManagedDecimal One => new(1);

        /// <summary>Gets the decimal radix.</summary>
        public static int Radix => 10;

        /// <summary>Adds two values.</summary>
        public static ManagedDecimal operator +(ManagedDecimal left, ManagedDecimal right) => new(left._value + right._value);

        /// <summary>Adds two values with overflow checking.</summary>
        public static ManagedDecimal operator checked +(ManagedDecimal left, ManagedDecimal right) => new(checked(left._value + right._value));

        /// <summary>Subtracts two values.</summary>
        public static ManagedDecimal operator -(ManagedDecimal left, ManagedDecimal right) => new(left._value - right._value);

        /// <summary>Subtracts two values with overflow checking.</summary>
        public static ManagedDecimal operator checked -(ManagedDecimal left, ManagedDecimal right) => new(checked(left._value - right._value));

        /// <summary>Multiplies two values.</summary>
        public static ManagedDecimal operator *(ManagedDecimal left, ManagedDecimal right) => new(left._value * right._value);

        /// <summary>Multiplies two values with overflow checking.</summary>
        public static ManagedDecimal operator checked *(ManagedDecimal left, ManagedDecimal right) => new(checked(left._value * right._value));

        /// <summary>Divides two values.</summary>
        public static ManagedDecimal operator /(ManagedDecimal left, ManagedDecimal right) => new(left._value / right._value);

        /// <summary>Divides two values with overflow checking.</summary>
        public static ManagedDecimal operator checked /(ManagedDecimal left, ManagedDecimal right) => new(checked(left._value / right._value));

        /// <summary>Computes the remainder of two values.</summary>
        public static ManagedDecimal operator %(ManagedDecimal left, ManagedDecimal right) => new(left._value % right._value);

        /// <summary>Compares two values.</summary>
        public static bool operator ==(ManagedDecimal left, ManagedDecimal right) => left._value == right._value;

        /// <summary>Compares two values.</summary>
        public static bool operator !=(ManagedDecimal left, ManagedDecimal right) => left._value != right._value;

        /// <summary>Compares two values.</summary>
        public static bool operator <(ManagedDecimal left, ManagedDecimal right) => left._value < right._value;

        /// <summary>Compares two values.</summary>
        public static bool operator <=(ManagedDecimal left, ManagedDecimal right) => left._value <= right._value;

        /// <summary>Compares two values.</summary>
        public static bool operator >(ManagedDecimal left, ManagedDecimal right) => left._value > right._value;

        /// <summary>Compares two values.</summary>
        public static bool operator >=(ManagedDecimal left, ManagedDecimal right) => left._value >= right._value;

        /// <summary>Applies the unary numeric operator.</summary>
        public static ManagedDecimal operator +(ManagedDecimal value) => new(+value._value);

        /// <summary>Applies the unary numeric operator.</summary>
        public static ManagedDecimal operator -(ManagedDecimal value) => new(-value._value);

        /// <summary>Applies the checked unary numeric operator.</summary>
        public static ManagedDecimal operator checked -(ManagedDecimal value) => new(checked(-value._value));

        /// <summary>Applies the unary numeric operator.</summary>
        public static ManagedDecimal operator ++(ManagedDecimal value) => new(value._value + 1);

        /// <summary>Applies the checked unary numeric operator.</summary>
        public static ManagedDecimal operator checked ++(ManagedDecimal value) => new(checked(value._value + 1));

        /// <summary>Applies the unary numeric operator.</summary>
        public static ManagedDecimal operator --(ManagedDecimal value) => new(value._value - 1);

        /// <summary>Applies the checked unary numeric operator.</summary>
        public static ManagedDecimal operator checked --(ManagedDecimal value) => new(checked(value._value - 1));

        /// <summary>Delegates decimal magnitude arithmetic.</summary>
        public static ManagedDecimal Abs(ManagedDecimal x) => new(DecimalAbs(x._value));

        /// <summary>Delegates decimal magnitude arithmetic.</summary>
        public static ManagedDecimal MaxMagnitude(ManagedDecimal x, ManagedDecimal y) => new(DecimalMaxMagnitude(x._value, y._value));

        /// <summary>Delegates decimal magnitude arithmetic.</summary>
        public static ManagedDecimal MaxMagnitudeNumber(ManagedDecimal x, ManagedDecimal y) => new(DecimalMaxMagnitudeNumber(x._value, y._value));

        /// <summary>Delegates decimal magnitude arithmetic.</summary>
        public static ManagedDecimal MinMagnitude(ManagedDecimal x, ManagedDecimal y) => new(DecimalMinMagnitude(x._value, y._value));

        /// <summary>Delegates decimal magnitude arithmetic.</summary>
        public static ManagedDecimal MinMagnitudeNumber(ManagedDecimal x, ManagedDecimal y) => new(DecimalMinMagnitudeNumber(x._value, y._value));

        /// <summary>Delegates decimal classification.</summary>
        public static bool IsCanonical(ManagedDecimal value) => Classify<decimal>(value._value, "IsCanonical");

        /// <summary>Delegates decimal classification.</summary>
        public static bool IsComplexNumber(ManagedDecimal value) => Classify<decimal>(value._value, "IsComplexNumber");

        /// <summary>Delegates decimal classification.</summary>
        public static bool IsEvenInteger(ManagedDecimal value) => Classify<decimal>(value._value, "IsEvenInteger");

        /// <summary>Delegates decimal classification.</summary>
        public static bool IsFinite(ManagedDecimal value) => Classify<decimal>(value._value, "IsFinite");

        /// <summary>Delegates decimal classification.</summary>
        public static bool IsImaginaryNumber(ManagedDecimal value) => Classify<decimal>(value._value, "IsImaginaryNumber");

        /// <summary>Delegates decimal classification.</summary>
        public static bool IsInfinity(ManagedDecimal value) => Classify<decimal>(value._value, "IsInfinity");

        /// <summary>Delegates decimal classification.</summary>
        public static bool IsInteger(ManagedDecimal value) => Classify<decimal>(value._value, "IsInteger");

        /// <summary>Delegates decimal classification.</summary>
        public static bool IsNaN(ManagedDecimal value) => Classify<decimal>(value._value, "IsNaN");

        /// <summary>Delegates decimal classification.</summary>
        public static bool IsNegative(ManagedDecimal value) => Classify<decimal>(value._value, "IsNegative");

        /// <summary>Delegates decimal classification.</summary>
        public static bool IsNegativeInfinity(ManagedDecimal value) => Classify<decimal>(value._value, "IsNegativeInfinity");

        /// <summary>Delegates decimal classification.</summary>
        public static bool IsNormal(ManagedDecimal value) => Classify<decimal>(value._value, "IsNormal");

        /// <summary>Delegates decimal classification.</summary>
        public static bool IsOddInteger(ManagedDecimal value) => Classify<decimal>(value._value, "IsOddInteger");

        /// <summary>Delegates decimal classification.</summary>
        public static bool IsPositive(ManagedDecimal value) => Classify<decimal>(value._value, "IsPositive");

        /// <summary>Delegates decimal classification.</summary>
        public static bool IsPositiveInfinity(ManagedDecimal value) => Classify<decimal>(value._value, "IsPositiveInfinity");

        /// <summary>Delegates decimal classification.</summary>
        public static bool IsRealNumber(ManagedDecimal value) => Classify<decimal>(value._value, "IsRealNumber");

        /// <summary>Delegates decimal classification.</summary>
        public static bool IsSubnormal(ManagedDecimal value) => Classify<decimal>(value._value, "IsSubnormal");

        /// <summary>Delegates decimal classification.</summary>
        public static bool IsZero(ManagedDecimal value) => Classify<decimal>(value._value, "IsZero");

        /// <summary>Parses a decimal representation.</summary>
        public static ManagedDecimal Parse(string s, IFormatProvider? provider) => new(decimal.Parse(s, provider));

        /// <summary>Attempts to parse a decimal representation.</summary>
        public static bool TryParse(string? s, IFormatProvider? provider, out ManagedDecimal result)
        {
            bool success = decimal.TryParse(s, provider, out decimal parsed);
            result = new(parsed);
            return success;
        }

        /// <summary>Parses a decimal representation.</summary>
        public static ManagedDecimal Parse(string s, NumberStyles style, IFormatProvider? provider) => new(decimal.Parse(s, style, provider));

        /// <summary>Attempts to parse a decimal representation.</summary>
        public static bool TryParse(string? s, NumberStyles style, IFormatProvider? provider, out ManagedDecimal result)
        {
            bool success = decimal.TryParse(s, style, provider, out decimal parsed);
            result = new(parsed);
            return success;
        }

        /// <summary>Parses a decimal representation.</summary>
        public static ManagedDecimal Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => new(decimal.Parse(s, provider));

        /// <summary>Attempts to parse a decimal representation.</summary>
        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out ManagedDecimal result)
        {
            bool success = decimal.TryParse(s, provider, out decimal parsed);
            result = new(parsed);
            return success;
        }

        /// <summary>Parses a decimal representation.</summary>
        public static ManagedDecimal Parse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider) => new(decimal.Parse(s, style, provider));

        /// <summary>Attempts to parse a decimal representation.</summary>
        public static bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out ManagedDecimal result)
        {
            bool success = decimal.TryParse(s, style, provider, out decimal parsed);
            result = new(parsed);
            return success;
        }

        /// <summary>Creates a value using decimal conversion semantics.</summary>
        public static ManagedDecimal CreateChecked<TOther>(TOther value)
            where TOther : INumberBase<TOther> => value is ManagedDecimal same ? same : new(decimal.CreateChecked(value));

        /// <summary>Converts from another numeric type.</summary>
        static bool INumberBase<ManagedDecimal>.TryConvertFromChecked<TOther>(TOther value, out ManagedDecimal result)
        {
            result = CreateChecked(value);
            return true;
        }

        /// <summary>Converts to another numeric type through decimal.</summary>
        static bool INumberBase<ManagedDecimal>.TryConvertToChecked<TOther>(ManagedDecimal value, out TOther result)
        {
            result = TOther.CreateChecked(value._value);
            return true;
        }

        /// <summary>Creates a value using decimal conversion semantics.</summary>
        public static ManagedDecimal CreateSaturating<TOther>(TOther value)
            where TOther : INumberBase<TOther> => value is ManagedDecimal same ? same : new(decimal.CreateSaturating(value));

        /// <summary>Converts from another numeric type.</summary>
        static bool INumberBase<ManagedDecimal>.TryConvertFromSaturating<TOther>(TOther value, out ManagedDecimal result)
        {
            result = CreateSaturating(value);
            return true;
        }

        /// <summary>Converts to another numeric type through decimal.</summary>
        static bool INumberBase<ManagedDecimal>.TryConvertToSaturating<TOther>(ManagedDecimal value, out TOther result)
        {
            result = TOther.CreateSaturating(value._value);
            return true;
        }

        /// <summary>Creates a value using decimal conversion semantics.</summary>
        public static ManagedDecimal CreateTruncating<TOther>(TOther value)
            where TOther : INumberBase<TOther> => value is ManagedDecimal same ? same : new(decimal.CreateTruncating(value));

        /// <summary>Converts from another numeric type.</summary>
        static bool INumberBase<ManagedDecimal>.TryConvertFromTruncating<TOther>(TOther value, out ManagedDecimal result)
        {
            result = CreateTruncating(value);
            return true;
        }

        /// <summary>Converts to another numeric type through decimal.</summary>
        static bool INumberBase<ManagedDecimal>.TryConvertToTruncating<TOther>(ManagedDecimal value, out TOther result)
        {
            result = TOther.CreateTruncating(value._value);
            return true;
        }

        /// <summary>Compares numeric values.</summary>
        public int CompareTo(ManagedDecimal other) => _value.CompareTo(other._value);

        /// <summary>Compares a boxed numeric value.</summary>
        public int CompareTo(object? obj) => obj is null ? 1 : obj is ManagedDecimal other ? CompareTo(other) : throw new ArgumentException("Expected a managed decimal.", nameof(obj));

        /// <summary>Tests numeric equality independently of representation.</summary>
        public bool Equals(ManagedDecimal other) => _value == other._value;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is ManagedDecimal other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => _value.GetHashCode();

        /// <inheritdoc/>
        public override string ToString() => _representation ?? "0";

        /// <summary>Formats the underlying decimal.</summary>
        public string ToString(string? format, IFormatProvider? formatProvider) => _value.ToString(format, formatProvider);

        /// <summary>Formats into a character span.</summary>
        public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider) => _value.TryFormat(destination, out charsWritten, format, provider);

        private static decimal DecimalAbs(decimal x) => decimal.Abs(x);

        private static decimal DecimalMaxMagnitude(decimal x, decimal y) => Magnitude<decimal>(x, y, "MaxMagnitude");

        private static decimal DecimalMaxMagnitudeNumber(decimal x, decimal y) => Magnitude<decimal>(x, y, "MaxMagnitudeNumber");

        private static decimal DecimalMinMagnitude(decimal x, decimal y) => Magnitude<decimal>(x, y, "MinMagnitude");

        private static decimal DecimalMinMagnitudeNumber(decimal x, decimal y) => Magnitude<decimal>(x, y, "MinMagnitudeNumber");

        private static T Magnitude<T>(T x, T y, string operation)
            where T : INumberBase<T> => operation switch
            {
                "MaxMagnitude" => T.MaxMagnitude(x, y),
                "MaxMagnitudeNumber" => T.MaxMagnitudeNumber(x, y),
                "MinMagnitude" => T.MinMagnitude(x, y),
                _ => T.MinMagnitudeNumber(x, y),
            };

        private static bool Classify<T>(T value, string operation)
            where T : INumberBase<T> => operation switch
            {
                "IsCanonical" => T.IsCanonical(value),
                "IsComplexNumber" => T.IsComplexNumber(value),
                "IsEvenInteger" => T.IsEvenInteger(value),
                "IsFinite" => T.IsFinite(value),
                "IsImaginaryNumber" => T.IsImaginaryNumber(value),
                "IsInfinity" => T.IsInfinity(value),
                "IsInteger" => T.IsInteger(value),
                "IsNaN" => T.IsNaN(value),
                "IsNegative" => T.IsNegative(value),
                "IsNegativeInfinity" => T.IsNegativeInfinity(value),
                "IsNormal" => T.IsNormal(value),
                "IsOddInteger" => T.IsOddInteger(value),
                "IsPositive" => T.IsPositive(value),
                "IsPositiveInfinity" => T.IsPositiveInfinity(value),
                "IsRealNumber" => T.IsRealNumber(value),
                "IsSubnormal" => T.IsSubnormal(value),
                "IsZero" => T.IsZero(value),
                _ => throw new ArgumentException("Unknown classification.", nameof(operation)),
            };
    }
}
