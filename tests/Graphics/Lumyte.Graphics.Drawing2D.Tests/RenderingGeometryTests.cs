using System.Numerics;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks geometric coverage and numerical contracts independently of the GPU shaders.</summary>
public sealed class RenderingGeometryTests
{
    /// <summary>Collinear control points may overshoot both endpoints and must not collapse into one chord.</summary>
    [Fact]
    public void CollinearBezierOvershootRetainsItsExtrema()
    {
        var quadratic = new PathBuilder2D();
        quadratic.MoveTo(Vector2.Zero);
        quadratic.QuadraticTo(new Vector2(100, 0), Vector2.UnitX);
        PathFlattening2D.Contour quadraticContour = Assert.Single(PathFlattening2D.Flatten(quadratic.Build(), Matrix3x2.Identity));
        Assert.InRange(quadraticContour.Points.Max(point => point.X), (10000f / 199) - 0.125f, (10000f / 199) + 0.125f);

        var cubic = new PathBuilder2D();
        cubic.MoveTo(Vector2.Zero);
        cubic.CubicTo(new Vector2(100, 0), new Vector2(-100, 0), Vector2.UnitX);
        PathFlattening2D.Contour cubicContour = Assert.Single(PathFlattening2D.Flatten(cubic.Build(), Matrix3x2.Identity));
        Assert.True(cubicContour.Points.Max(point => point.X) > 28);
        Assert.True(cubicContour.Points.Min(point => point.X) < -28);
        Assert.Equal(Vector2.UnitX, cubicContour.Points[^1]);
    }

    /// <summary>A segment after Close begins at the closed contour's start and remains an open subpath.</summary>
    [Fact]
    public void CloseThenCurveStartsAtTheClosedContourOrigin()
    {
        var path = new PathBuilder2D();
        path.MoveTo(Vector2.Zero);
        path.LineTo(new Vector2(20, 0));
        path.LineTo(new Vector2(20, 20));
        path.Close();
        path.QuadraticTo(new Vector2(0, 20), new Vector2(20, 20));
        List<PathFlattening2D.Contour> contours = PathFlattening2D.Flatten(path.Build(), Matrix3x2.Identity);
        Assert.Equal(2, contours.Count);
        Assert.True(contours[0].Closed);
        Assert.False(contours[1].Closed);
        Assert.Equal(Vector2.Zero, contours[1].Points[0]);
        Assert.Equal(new Vector2(20, 20), contours[1].Points[^1]);
        Assert.Contains(contours[1].Points, point => point.X > 0 && point.X < 10 && point.Y > point.X);
    }

    /// <summary>Subdividing a curve uses output-pixel precision after rotation, shear, and nonuniform scale.</summary>
    [Fact]
    public void TransformedCubicStaysWithinItsPixelErrorBudget()
    {
        Vector2 start = new(0, 0);
        Vector2 control1 = new(0, 20);
        Vector2 control2 = new(30, -10);
        Vector2 end = new(40, 0);
        var transform = new Matrix3x2(40, 12, 17, 3, 5, -7);
        var path = new PathBuilder2D();
        path.MoveTo(start);
        path.CubicTo(control1, control2, end);
        PathFlattening2D.Contour contour = Assert.Single(PathFlattening2D.Flatten(path.Build(), transform));
        Vector2[] polyline = contour.Points.Select(point => Vector2.Transform(point, transform)).ToArray();
        for (int i = 0; i <= 1000; i++)
        {
            float t = i / 1000f;
            float u = 1 - t;
            Vector2 analytic = (start * u * u * u) + (control1 * 3 * u * u * t) + (control2 * 3 * u * t * t) + (end * t * t * t);
            Assert.InRange(DistanceToPolyline(Vector2.Transform(analytic, transform), polyline), 0, 0.126f);
        }
    }

    private static float DistanceToPolyline(Vector2 point, IReadOnlyList<Vector2> polyline)
    {
        float minimum = float.PositiveInfinity;
        for (int i = 1; i < polyline.Count; i++)
        {
            Vector2 start = polyline[i - 1];
            Vector2 delta = polyline[i] - start;
            float lengthSquared = delta.LengthSquared();
            float t = lengthSquared == 0 ? 0 : Math.Clamp(Vector2.Dot(point - start, delta) / lengthSquared, 0, 1);
            minimum = MathF.Min(minimum, Vector2.Distance(point, start + (delta * t)));
        }

        return minimum;
    }
}
