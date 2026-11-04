using System.Numerics;
using Xunit;

namespace Lumyte.UI.Tests;

/// <summary>Tests box constraint validation, tightening and finite size selection.</summary>
public sealed class UiBoxConstraintsTests
{
    /// <summary>Tests that the default value represents a valid tight zero box.</summary>
    [Fact]
    public void DefaultIsATightZeroBox()
    {
        UiBoxConstraints constraints = default;
        Assert.True(constraints.IsTight);
        Assert.True(constraints.HasBoundedWidth);
        Assert.True(constraints.HasBoundedHeight);
        Assert.Equal(Vector2.Zero, constraints.Constrain(new(20, 30)));
    }

    /// <summary>Tests invalid finite minima.</summary>
    /// <param name="value">The invalid minimum.</param>
    [Theory]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void InvalidMinimaAreRejected(float value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new UiBoxConstraints(value, float.PositiveInfinity, 0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UiBoxConstraints(0, 10, value, float.PositiveInfinity));
    }

    /// <summary>Tests invalid maxima.</summary>
    /// <param name="value">The invalid maximum.</param>
    [Theory]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.NegativeInfinity)]
    public void InvalidMaximaAreRejected(float value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new UiBoxConstraints(0, value, 0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UiBoxConstraints(0, 10, 0, value));
    }

    /// <summary>Tests minimum and maximum ordering on both axes.</summary>
    [Fact]
    public void InvertedRangesAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new UiBoxConstraints(11, 10, 0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UiBoxConstraints(0, 10, 11, 10));
    }

    /// <summary>Tests unbounded axes without producing infinite final sizes.</summary>
    [Fact]
    public void UnboundedMaximaPreserveFiniteCandidates()
    {
        var constraints = new UiBoxConstraints(2, float.PositiveInfinity, 4, 100);
        Assert.False(constraints.HasBoundedWidth);
        Assert.True(constraints.HasBoundedHeight);
        Assert.False(constraints.IsTight);
        Assert.Equal(new Vector2(2, 4), constraints.Constrain(Vector2.Zero));
        Assert.Equal(new Vector2(float.MaxValue, 100), constraints.Constrain(new(float.MaxValue, 200)));
        Assert.Equal(new UiBoxConstraints(0, float.PositiveInfinity, 0, 100), constraints.Loosen());
    }

    /// <summary>Tests tight and loose construction.</summary>
    [Fact]
    public void TightAndLooseExpressFixedAndMaximumSizes()
    {
        var tight = UiBoxConstraints.Tight(new(20, 30));
        Assert.True(tight.IsTight);
        Assert.Equal(new Vector2(20, 30), tight.Constrain(Vector2.Zero));
        Assert.Equal(new UiBoxConstraints(0, 20, 0, 30), UiBoxConstraints.Loose(new(20, 30)));
        Assert.False(UiBoxConstraints.Loose(new(float.PositiveInfinity, float.PositiveInfinity)).HasBoundedHeight);
    }

    /// <summary>Tests clamping of requested dimensions while preserving unspecified axes.</summary>
    [Fact]
    public void TightenPreservesParentConstraintsAndUnspecifiedAxes()
    {
        var constraints = new UiBoxConstraints(10, 100, 20, float.PositiveInfinity);
        Assert.Equal(new UiBoxConstraints(100, 100, 20, float.PositiveInfinity), constraints.Tighten(width: 200));
        Assert.Equal(new UiBoxConstraints(10, 100, 20, 20), constraints.Tighten(height: 2));
        Assert.Equal(constraints, constraints.Tighten());
        Assert.Equal(new UiBoxConstraints(40, 40, 50, 50), constraints.Tighten(40, 50));
        var tight = UiBoxConstraints.Tight(new(800, 600));
        Assert.Equal(tight, tight.Tighten(160, 20));
    }

    /// <summary>Tests rejection of invalid requested dimensions and candidate sizes.</summary>
    /// <param name="value">The invalid dimension.</param>
    [Theory]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void NonFiniteOrNegativeRequestedSizesAreRejected(float value)
    {
        var constraints = UiBoxConstraints.Loose(new(100, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => constraints.Tighten(width: value));
        Assert.Throws<ArgumentOutOfRangeException>(() => constraints.Tighten(height: value));
        Assert.Throws<ArgumentOutOfRangeException>(() => constraints.Constrain(new(value, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => constraints.Constrain(new(0, value)));
        Assert.Throws<ArgumentOutOfRangeException>(() => UiBoxConstraints.Tight(new(value, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => UiBoxConstraints.Tight(new(0, value)));
    }
}
