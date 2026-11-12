using System.Numerics;
using Lumyte.Mathematics.Geometry;
using Xunit;

namespace Lumyte.Mathematics.Tests;

/// <summary>Regressions for PR128comment4552694976 and the downward IEEE clamp policy.</summary>
public sealed class ReviewRoundedSubnormalTests
{
    /// <summary>Odd subnormal extents clamp down to a safe representable radius.</summary>
    [Fact]
    public void DoubleOddSubnormalClampRoundsDown() => CheckOddSubnormals<double>();

    /// <summary>Single precision follows the same downward-clamp and containment policy.</summary>
    [Fact]
    public void FloatOddSubnormalClampRoundsDown() => CheckOddSubnormals<float>();

    /// <summary>Half precision follows the same downward-clamp and containment policy.</summary>
    [Fact]
    public void HalfOddSubnormalClampRoundsDown() => CheckOddSubnormals<Half>();

    /// <summary>The reported case now constructs safely; constructor rejection is no longer accepted.</summary>
    [Fact]
    public void ReportedDoubleCaseConstructsWithSafeDownwardClamp()
    {
        var rounded = new RoundedRect2D<double>(new(0, 0, 3 * double.Epsilon, 3 * double.Epsilon), 2 * double.Epsilon);
        Assert.Equal(double.Epsilon, rounded.Radius);
        Assert.True(rounded.Contains(new(double.Epsilon, double.Epsilon)));
        _ = rounded.Contains(default);
    }

    /// <summary>Finite extremes clamp without overflowing twice the requested radius.</summary>
    [Fact]
    public void FiniteExtremeClampAndZeroExtentRemainValid()
    {
        CheckExtreme<double>(double.MaxValue);
        CheckExtreme<float>(float.MaxValue);
        CheckExtreme<Half>(Half.MaxValue);
    }

    /// <summary>Discrete scalars retain exact-half rejection.</summary>
    [Fact]
    public void IntegerScalarClampPolicyIsUnchanged()
    {
        var bounds = new Rect2D<int>(0, 0, 3, 3);
        Assert.Throws<ArgumentException>(() => new RoundedRect2D<int>(bounds, 2));
        Assert.False(RoundedRect2D<int>.TryCreate(bounds, 2, out _));
        Assert.Equal(1, new RoundedRect2D<int>(bounds, 1).Radius);
    }

    /// <summary>Decimal's smallest odd extents retain exact-half rejection, unlike IEEE rounding.</summary>
    [Fact]
    public void DecimalHalfRepresentabilityIsPreserved()
    {
        const decimal Unit = 0.0000000000000000000000000001m;
        var bounds = new Rect2D<decimal>(0, 0, 3 * Unit, 3 * Unit);
        Assert.Throws<ArgumentException>(() => new RoundedRect2D<decimal>(bounds, 2 * Unit));
        Assert.False(RoundedRect2D<decimal>.TryCreate(bounds, 2 * Unit, out RoundedRect2D<decimal> rejected));
        Assert.Equal(default, rejected);
        var small = new RoundedRect2D<decimal>(bounds, Unit);
        Assert.True(small.Contains(new(Unit, Unit)));
        Assert.False(small.Contains(default));
        var ordinary = new RoundedRect2D<decimal>(new(0, 0, 3, 3), 2);
        Assert.Equal(1.5m, ordinary.Radius);
        Assert.True(ordinary.Contains(new(1.5m, 0)));
    }

    private static void CheckOddSubnormals<T>()
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        T unit = T.Epsilon;
        foreach (int multiple in new[] { 1, 3, 5, 7 })
        {
            T extent = T.CreateChecked(multiple) * unit;
            T tooLarge = T.CreateChecked((multiple + 1) / 2) * unit;
            foreach (Rect2D<T> bounds in new[]
            {
                new Rect2D<T>(T.Zero, T.Zero, extent, extent),
                new Rect2D<T>(T.Zero, T.Zero, extent, T.One),
                new Rect2D<T>(T.Zero, T.Zero, T.One, extent),
            })
            {
                T floorHalf = T.CreateChecked(multiple / 2) * unit;
                var clamped = new RoundedRect2D<T>(bounds, tooLarge);
                Assert.Equal(floorHalf, clamped.Radius);
                Assert.True(T.IsFinite(clamped.Radius));
                Assert.True(clamped.Radius <= bounds.Width - clamped.Radius);
                Assert.True(clamped.Radius <= bounds.Height - clamped.Radius);
                Assert.True(RoundedRect2D<T>.TryCreate(bounds, tooLarge, out RoundedRect2D<T> created));
                Assert.Equal(clamped, created);
                Assert.True(clamped.Contains(new(floorHalf, floorHalf)));

                // Tiny IEEE distances may round onto the boundary; containment must remain safe.
                _ = clamped.Contains(default);
                _ = clamped.Contains(new(bounds.Width, bounds.Height));
                Assert.False(clamped.Contains(new(-unit, T.Zero)));

                // A fitting radius must not require the unrepresentable half extent.
                T fitting = T.CreateChecked(multiple / 2) * unit;
                Assert.True(RoundedRect2D<T>.TryCreate(bounds, fitting, out RoundedRect2D<T> rounded));
                Assert.Equal(fitting, rounded.Radius);
                Assert.True(rounded.Contains(new(fitting, fitting)));
                _ = rounded.Contains(default);
                _ = rounded.Contains(new(bounds.Width, bounds.Height));

                var square = new RoundedRect2D<T>(bounds, T.Zero);
                Assert.Equal(T.Zero, square.Radius);
                Assert.True(square.Contains(default));
                Assert.True(square.Contains(new(bounds.Width, bounds.Height)));
                Assert.False(square.Contains(new(-unit, T.Zero)));
            }
        }

        foreach (T invalid in new[] { T.NaN, T.PositiveInfinity, T.NegativeInfinity, -unit })
        {
            var bounds = new Rect2D<T>(T.Zero, T.Zero, T.One, T.One);
            Assert.Throws<ArgumentOutOfRangeException>(() => new RoundedRect2D<T>(bounds, invalid));
            Assert.False(RoundedRect2D<T>.TryCreate(bounds, invalid, out RoundedRect2D<T> rejected));
            Assert.Equal(default, rejected);
        }

        // Ordinary exact halves must not be decremented or expanded.
        T three = T.CreateChecked(3);
        T nominalHalf = three / (T.One + T.One);
        var nominal = new RoundedRect2D<T>(new(T.Zero, T.Zero, three, three), three);
        Assert.Equal(nominalHalf, nominal.Radius);
        Assert.True(nominal.Contains(new(nominalHalf, T.Zero)));

        // Even subnormal halves remain representable and clamp successfully.
        var even = new RoundedRect2D<T>(new(T.Zero, T.Zero, unit + unit, unit + unit), T.One);
        Assert.Equal(unit, even.Radius);
        Assert.True(even.Contains(new(unit, unit)));
        Assert.True(even.Contains(new(T.Zero, unit)));
    }

    private static void CheckExtreme<T>(T maximum)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        var bounds = new Rect2D<T>(T.Zero, T.Zero, maximum, maximum);
        Assert.True(RoundedRect2D<T>.TryCreate(bounds, maximum, out RoundedRect2D<T> rounded));
        Assert.Equal(maximum / (T.One + T.One), rounded.Radius);
        Assert.True(T.IsFinite(rounded.Radius));
        Assert.True(rounded.Radius <= maximum - rounded.Radius);
        Assert.True(rounded.Contains(new(rounded.Radius, rounded.Radius)));
        Assert.False(rounded.Contains(default));
        var zero = new RoundedRect2D<T>(new(T.Zero, T.Zero, T.Zero, maximum), maximum);
        Assert.Equal(T.Zero, zero.Radius);
        Assert.True(zero.Contains(default));
        Assert.True(zero.Contains(new(T.Zero, maximum)));
        Assert.True(default(RoundedRect2D<T>).Contains(default));
    }
}
