using System.Numerics;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>CPU validation of raw GPU inputs, subdivision bounds and fixed reservations.</summary>
public sealed class ComputeGeometryPlanTests
{
    /// <summary>Quadratic raw controls survive; only capacity is subdivided on the CPU.</summary>
    [Fact]
    public void QuadraticKeepsRawControlsAndUsesBinary64Bound()
    {
        var builder = new PathBuilder2D();
        builder.MoveTo(Vector2.Zero);
        builder.QuadraticTo(new(0, 16), new(16, 0));
        var transform = Matrix3x2.CreateScale(2, 3);
        var plan = ComputeGeometryPlan2D.Create(builder.Build(), transform, FillRule2D.EvenOdd);
        ComputeCurveInput2D curve = plan.Curves[1];
        double m = 2 * Math.Sqrt((32.0 * 32) + (96.0 * 96));
        Assert.Equal((int)Math.Ceiling(Math.Sqrt(m)), curve.SubdivisionCount);
        Assert.True(m / (8 * curve.SubdivisionCount * curve.SubdivisionCount) <= 0.125);
        Assert.Equal(new Vector2(0, 16), curve.P1);
        Assert.Equal(new Vector2(16, 0), curve.P2);
        Assert.Equal(transform, plan.Transform);
        Assert.Equal(FillRule2D.EvenOdd, plan.FillRule);
        Assert.Equal(3, plan.Curves.Count);
        Assert.True(plan.Curves[2].ImplicitClose);
        Assert.Equal(curve.SubdivisionCount, plan.Curves[2].EdgeOffset);
        Assert.Equal(curve.SubdivisionCount + 1L, plan.Capacity.Edges);
    }

    /// <summary>Cubic uses the larger of both transformed second differences.</summary>
    [Fact]
    public void CubicUsesBothSecondDifferences()
    {
        var builder = new PathBuilder2D();
        builder.MoveTo(Vector2.Zero);
        builder.CubicTo(new(1, 0), new(2, 0), new(3, 100));
        var plan = ComputeGeometryPlan2D.Create(builder.Build(), Matrix3x2.Identity);
        Assert.Equal(25, plan.Curves[1].SubdivisionCount);
        Assert.Equal(new Vector2(3, 100), plan.Curves[1].P3);
    }

    /// <summary>Close followed by another edge starts a separate contour at the prior start.</summary>
    [Fact]
    public void ClosingTopologyAndOpenStrokeArePreserved()
    {
        var builder = new PathBuilder2D();
        builder.MoveTo(new(2, 2));
        builder.LineTo(new(6, 2));
        builder.Close();
        builder.Close();
        builder.LineTo(new(2, 6));
        var fill = ComputeGeometryPlan2D.Create(builder.Build(), Matrix3x2.Identity);
        var stroke = ComputeGeometryPlan2D.Create(builder.Build(), Matrix3x2.Identity, stroke: new());
        Assert.Equal(2, fill.ContourCount);
        Assert.Equal(4, fill.Capacity.Edges);
        Assert.Equal(3, stroke.Capacity.Edges);
        Assert.Single(fill.Curves, c => c.ImplicitClose);
        Assert.DoesNotContain(stroke.Curves, c => c.ImplicitClose);
        Assert.Equal(new Vector2(2, 2), fill.Curves[4].P0);
        Assert.Equal(1, fill.Curves[4].ContourIndex);
    }

    /// <summary>Stroke reservations count generated triangle edges rather than original curve edges.</summary>
    /// <param name="cap">The open-end style.</param>
    /// <param name="join">The internal join style.</param>
    [Theory]
    [InlineData(LineCap2D.Butt, LineJoin2D.Miter)]
    [InlineData(LineCap2D.Square, LineJoin2D.Bevel)]
    [InlineData(LineCap2D.Round, LineJoin2D.Round)]
    public void StrokeBudgetsExpandedTrianglesAndReferences(LineCap2D cap, LineJoin2D join)
    {
        var builder = new PathBuilder2D();
        builder.MoveTo(new(10, 10));
        builder.LineTo(new(20, 10));
        var plan = ComputeGeometryPlan2D.Create(
            builder.Build(),
            Matrix3x2.Identity,
            stroke: new() { Width = 4, Cap = cap, Join = join });
        long expected = 2 + (5L * Math.Max(2, plan.RoundDivisions));
        Assert.Equal(expected, plan.Capacity.Triangles);
        Assert.Equal(expected * 3 * plan.TileBounds.Count, plan.Capacity.EdgeReferences);
        Assert.True(plan.Capacity.EdgeReferences > plan.Capacity.Edges);
        Assert.True(plan.Capacity.BackdropElements > plan.TileBounds.Count);
        Assert.True(plan.Capacity.ScanElements > plan.Capacity.EdgeReferences);
    }

    /// <summary>Move-only contours remain available for round or square isolated-point generation.</summary>
    [Fact]
    public void IsolatedPointsAndSingularTransformsRemainEncodable()
    {
        var builder = new PathBuilder2D();
        builder.MoveTo(new(1, 2));
        var plan = ComputeGeometryPlan2D.Create(
            builder.Build(),
            Matrix3x2.CreateScale(0),
            stroke: new() { Cap = LineCap2D.Round });
        Assert.Single(plan.Curves);
        Assert.Equal(0, plan.Capacity.Edges);
        Assert.Equal(8, plan.RoundDivisions);
        Assert.Equal(32, plan.Capacity.Triangles);
        Assert.True(plan.TileBounds.Count > 0);
    }

    /// <summary>Large winding interiors reserve backdrop even without interior boundary edges.</summary>
    [Fact]
    public void BackdropAndScanIncludeInteriorTiles()
    {
        var builder = new PathBuilder2D();
        builder.MoveTo(Vector2.Zero);
        builder.LineTo(new(256, 0));
        builder.LineTo(new(256, 256));
        builder.LineTo(new(0, 256));
        builder.Close();
        var plan = ComputeGeometryPlan2D.Create(builder.Build(), Matrix3x2.Identity);
        Assert.Equal(plan.TileBounds.Count, plan.Capacity.DrawReferences);
        Assert.Equal((plan.TileBounds.Width + 1L) * (plan.TileBounds.Height + 1L), plan.Capacity.BackdropElements);
        Assert.True(plan.Capacity.DrawReferences > plan.Capacity.Edges);
    }

    /// <summary>One path's complete reservation is rejected before its encoded array allocation.</summary>
    [Fact]
    public void BudgetsRejectWithoutClamping()
    {
        var builder = new PathBuilder2D();
        builder.MoveTo(Vector2.Zero);
        builder.LineTo(Vector2.One);
        Path2D path = builder.Build();
        var plan = ComputeGeometryPlan2D.Create(path, Matrix3x2.Identity);
        Assert.Throws<NotSupportedException>(() => ComputeGeometryPlan2D.Create(
            path,
            Matrix3x2.Identity,
            maxWorkingBytes: plan.Capacity.WorkingBytes - 1));
        plan.Capacity.Validate(plan.Capacity.WorkingBytes);
        Assert.Throws<NotSupportedException>(() => plan.Capacity.Validate(long.MaxValue, paintClipVectors: 262144));
        Assert.Throws<NotSupportedException>(() => plan.Capacity.Validate(long.MaxValue, otherWorkingBytes: long.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => ComputeGeometryPlan2D.Create(path, Matrix3x2.Identity, maxWorkingBytes: 0));
    }

    /// <summary>Cumulative limits and additions are checked rather than wrapping counters.</summary>
    [Fact]
    public void CumulativeReferenceAndOverflowLimitsAreChecked()
    {
        var capacity = new ComputeGeometryCapacity2D(0, 0, 4194304, 1, 0, 0, 0, 0);
        Assert.Throws<NotSupportedException>(() => capacity.Validate(long.MaxValue));
        var huge = new ComputeGeometryCapacity2D(long.MaxValue, 0, 0, 0, 0, 0, 0, 0);
        Assert.Throws<OverflowException>(() => huge.Combine(huge));
    }

    /// <summary>Nonfinite transforms, enormous round strokes and oversized curves are not downgraded.</summary>
    [Fact]
    public void InvalidAndUnsupportedInputsFailExplicitly()
    {
        var builder = new PathBuilder2D();
        builder.MoveTo(Vector2.Zero);
        builder.QuadraticTo(new(0, float.MaxValue), Vector2.One);
        Assert.Throws<NotSupportedException>(() => ComputeGeometryPlan2D.Create(builder.Build(), Matrix3x2.Identity));
        var point = new PathBuilder2D();
        point.MoveTo(Vector2.Zero);
        Assert.Throws<NotSupportedException>(() => ComputeGeometryPlan2D.Create(
            point.Build(),
            Matrix3x2.Identity,
            stroke: new() { Width = float.MaxValue, Cap = LineCap2D.Round }));
        Assert.Throws<ArgumentException>(() => ComputeGeometryPlan2D.Create(
            point.Build(),
            new(float.NaN, 0, 0, 1, 0, 0)));
    }

    /// <summary>Large raw inputs cannot evade cumulative geometry limits through closing edges.</summary>
    [Fact]
    public void ClosingEdgeCannotEscapePathEdgeLimit()
    {
        var segments = new PathSegment2D[65537];
        segments[0] = new(PathVerb2D.Move, Vector2.Zero, default, default);
        Array.Fill(segments, new PathSegment2D(PathVerb2D.Line, Vector2.One, default, default), 1, 65536);
        Assert.Throws<NotSupportedException>(() => ComputeGeometryPlan2D.Create(new(segments), Matrix3x2.Identity));
    }
}
