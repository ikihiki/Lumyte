using System.Numerics;
using Lumyte.Mathematics.Geometry;
using Xunit;

namespace Lumyte.Mathematics.Tests;

/// <summary>Checks the explicit arithmetic contract shared with clip normalization.</summary>
public sealed class ClipGeometryTests
{
    /// <summary>Double transforms match the original nested double FMA bit for bit.</summary>
    [Fact]
    public void TransformMatchesDoubleOracle()
    {
        var random = new Random(917);
        for (int i = 0; i < 1000; i++)
        {
            float Next() => (float)((random.NextDouble() - 0.5) * 100000);
            Vector2 p = new(Next(), Next());
            Matrix3x2 m = new(Next(), Next(), Next(), Next(), Next(), Next());
            Point2D<double> actual = Geometry2D.TransformAffineFused(new Point2D<double>(p.X, p.Y), m);
            Assert.Equal(BitConverter.DoubleToInt64Bits(Math.FusedMultiplyAdd(m.M11, p.X, Math.FusedMultiplyAdd(m.M21, p.Y, m.M31))), BitConverter.DoubleToInt64Bits(actual.X));
            Assert.Equal(BitConverter.DoubleToInt64Bits(Math.FusedMultiplyAdd(m.M12, p.X, Math.FusedMultiplyAdd(m.M22, p.Y, m.M32))), BitConverter.DoubleToInt64Bits(actual.Y));
        }
    }

    /// <summary>Single-precision affine evaluation delegates to BCL fused vector operations.</summary>
    [Fact]
    public void FloatTransformMatchesBclFusedOracle()
    {
        var random = new Random(911);
        for (int i = 0; i < 1000; i++)
        {
            float Next() => (float)((random.NextDouble() - 0.5) * 100000);
            Vector2 p = new(Next(), Next());
            Matrix3x2 m = new(Next(), Next(), Next(), Next(), Next(), Next());
            Point2D<float> actual = Geometry2D.TransformAffineFused(new Point2D<float>(p.X, p.Y), m);
            Assert.Equal(BitConverter.SingleToInt32Bits(MathF.FusedMultiplyAdd(m.M11, p.X, MathF.FusedMultiplyAdd(m.M21, p.Y, m.M31))), BitConverter.SingleToInt32Bits(actual.X));
            Assert.Equal(BitConverter.SingleToInt32Bits(MathF.FusedMultiplyAdd(m.M12, p.X, MathF.FusedMultiplyAdd(m.M22, p.Y, m.M32))), BitConverter.SingleToInt32Bits(actual.Y));
        }
    }

    /// <summary>FMA contraction, half-open Y transitions, and strict boundary orientation are explicit.</summary>
    [Fact]
    public void WindingPreservesFusedOrientationAndHalfOpenEdges()
    {
        double a = Math.BitIncrement(1d), c = Math.BitIncrement(a);
        Assert.Equal(1, Geometry2D.GetWindingContributionFused(0d, 0d, a, c, 1d, a));
        Assert.Equal(0, Geometry2D.GetWindingContributionFused(0d, 0d, 1d, 1d, 0.5d, 0.5d));
        Assert.Equal(1, Geometry2D.GetWindingContributionFused(1d, 0d, 1d, 1d, 0d, 0d));
        Assert.Equal(0, Geometry2D.GetWindingContributionFused(1d, 0d, 1d, 1d, 0d, 1d));
        Assert.Equal(-1, Geometry2D.GetWindingContributionFused(1f, 1f, 1f, 0f, 0f, 0f));
        Assert.Equal(0, Geometry2D.GetWindingContributionFused(0d, 0d, 1d, 0d, 0.5d, 0d));
    }

    /// <summary>All contours contribute before fill-rule interpretation, with reversal cancelling holes.</summary>
    [Fact]
    public void MultipleContoursAccumulateBeforeFillRule()
    {
        Segment2D<double>[] contour = [new(new(0, 0), new(2, 0)), new(new(2, 0), new(2, 2)), new(new(2, 2), new(0, 2)), new(new(0, 2), new(0, 0))];
        Segment2D<double>[] twice = [.. contour, .. contour];
        Assert.Equal(2, Geometry2D.AccumulateWindingFused<double>(twice, new(1, 1)));
        Segment2D<double>[] cancelled = [.. contour, .. contour.Select(edge => new Segment2D<double>(edge.End, edge.Start))];
        Assert.Equal(0, Geometry2D.AccumulateWindingFused<double>(cancelled, new(1, 1)));
        Assert.Equal(0, Geometry2D.AccumulateWindingFused<double>([], new(1, 1)));
    }

    /// <summary>Finite point storage rejects invalid affine output rather than admitting invalid coordinates.</summary>
    [Fact]
    public void NonFiniteAffineOutputIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Geometry2D.TransformAffineFused(new Point2D<double>(1, 1), new Matrix3x2(float.NaN, 0, 0, 1, 0, 0)));
        Assert.Equal(new Point2D<float>(3, 4), Geometry2D.TransformAffineFused(new Point2D<float>(1, 1), Matrix3x2.CreateTranslation(2, 3)));
    }
}
