using System.Numerics;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Raw-curve stroke tangent-error and admission regressions.</summary>
public sealed class ComputeStrokeBoundTests
{
    /// <summary>The thin-device arch still needs logical-normal refinement.</summary>
    [Fact]
    public void NonuniformArchRequires385NotCenterlineOne()
    {
        Path2D path = Arch();
        Matrix3x2 transform = Matrix3x2.CreateScale(16, 0.25f) * Matrix3x2.CreateTranslation(64, 8);
        var fill = ComputeGeometryPlan2D.Create(path, transform);
        var stroke = ComputeGeometryPlan2D.Create(path, transform, stroke: new() { Width = 6, Cap = LineCap2D.Butt, Join = LineJoin2D.Bevel });
        Assert.Equal(1, fill.Curves[1].SubdivisionCount);
        Assert.Equal(385, stroke.Curves[1].SubdivisionCount);
        double n = stroke.Curves[1].SubdivisionCount;
        Assert.True((1 / (8 * n * n)) + (Math.Sqrt(256.0625) * 3 * 4 / (2 * 2 * n)) <= 0.125);
        double previous = n - 1;
        Assert.True((1 / (8 * previous * previous)) + (Math.Sqrt(256.0625) * 3 * 4 / (2 * 2 * previous)) > 0.125);
        Assert.Throws<NotSupportedException>(() => ComputeGeometryPlan2D.Create(path, transform, stroke: new() { Width = 6 }, maxWorkingBytes: 1024));
    }

    /// <summary>Width, shear and cubic controls influence reservations for every style.</summary>
    /// <param name="cap">The cap style.</param>
    /// <param name="join">The join style.</param>
    [Theory]
    [InlineData(LineCap2D.Butt, LineJoin2D.Bevel)]
    [InlineData(LineCap2D.Square, LineJoin2D.Miter)]
    [InlineData(LineCap2D.Round, LineJoin2D.Round)]
    public void CubicShearAndWidthRefineWithoutChangingControls(LineCap2D cap, LineJoin2D join)
    {
        var builder = new PathBuilder2D();
        builder.MoveTo(new(-1, 0));
        builder.CubicTo(new(-0.5f, 1), new(0.5f, 1), new(1, 0));
        Path2D path = builder.Build();
        var transform = new Matrix3x2(2, 0.25f, 1, 0.5f, 0, 0);
        var thin = ComputeGeometryPlan2D.Create(path, transform, stroke: new() { Width = 0.5f, Cap = cap, Join = join });
        var wide = ComputeGeometryPlan2D.Create(path, transform, stroke: new() { Width = 2, Cap = cap, Join = join });
        Assert.True(wide.Curves[1].SubdivisionCount > thin.Curves[1].SubdivisionCount);
        Assert.Equal(new Vector2(-0.5f, 1), wide.Curves[1].P1);
        Assert.Equal(2, wide.Curves.Count);
    }

    /// <summary>Uncertifiable cusps reject; zero derivatives on a constant ray remain supported.</summary>
    [Fact]
    public void CuspRejectsButConstantNormalAndCollapsedCurvesSurvive()
    {
        var cusp = new PathBuilder2D();
        cusp.MoveTo(Vector2.Zero);
        cusp.QuadraticTo(Vector2.One, Vector2.Zero);
        Assert.Throws<NotSupportedException>(() => ComputeGeometryPlan2D.Create(cusp.Build(), Matrix3x2.Identity, stroke: new()));
        var ray = new PathBuilder2D();
        ray.MoveTo(Vector2.Zero);
        ray.CubicTo(Vector2.Zero, new(1, 2), new(2, 4));
        var straight = ComputeGeometryPlan2D.Create(ray.Build(), Matrix3x2.Identity, stroke: new());
        Assert.Equal(1, straight.Curves[1].SubdivisionCount);
        var collapsed = new PathBuilder2D();
        collapsed.MoveTo(Vector2.One);
        collapsed.QuadraticTo(Vector2.One, Vector2.One);
        Assert.Equal(1, ComputeGeometryPlan2D.Create(collapsed.Build(), Matrix3x2.Identity, stroke: new()).Curves[1].SubdivisionCount);
    }

    /// <summary>Almost-rays and zero endpoint speed cannot bypass certification.</summary>
    [Fact]
    public void NonconstantZeroEndpointAndNearCollinearTurnaroundReject()
    {
        var zeroEndpoint = new PathBuilder2D();
        zeroEndpoint.MoveTo(Vector2.Zero);
        zeroEndpoint.CubicTo(Vector2.Zero, new(1, 0), new(2, 1));
        Assert.Throws<NotSupportedException>(() => ComputeGeometryPlan2D.Create(zeroEndpoint.Build(), Matrix3x2.Identity, stroke: new()));
        var near = new PathBuilder2D();
        near.MoveTo(Vector2.Zero);
        near.QuadraticTo(new(1, 1), new(0, float.Epsilon));
        Assert.Throws<NotSupportedException>(() => ComputeGeometryPlan2D.Create(near.Build(), Matrix3x2.Identity, stroke: new()));
    }

    /// <summary>Excess tangent refinement is rejected, never clamped.</summary>
    [Fact]
    public void ExcessiveRefinementRejectsBeforeRawAllocation()
        => Assert.Throws<NotSupportedException>(() => ComputeGeometryPlan2D.Create(Arch(), Matrix3x2.CreateScale(16, 0.25f), stroke: new() { Width = 100000 }));

    private static Path2D Arch()
    {
        var builder = new PathBuilder2D();
        builder.MoveTo(new(-1, 0));
        builder.QuadraticTo(new(0, 1), new(1, 0));
        return builder.Build();
    }
}
