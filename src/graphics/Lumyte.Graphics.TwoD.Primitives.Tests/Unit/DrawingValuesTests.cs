using System.Numerics;

namespace Lumyte.Graphics.TwoD.Primitives.Tests;

public sealed class DrawingValuesTests
{
    [Fact]
    public void GradientOwnsAndOrdersItsStops()
    {
        GradientStop[] stops = [new(1, Color.White), new(0, new Color(1, 0, 0))];
        var brush = Brush.LinearGradient(Vector2.Zero, Vector2.One, stops);

        stops[1] = new(0, Color.Transparent);

        Assert.Collection(brush.Stops, stop => Assert.Equal(new GradientStop(0, new Color(1, 0, 0)), stop), stop => Assert.Equal(new GradientStop(1, Color.White), stop));
        Assert.Throws<NotSupportedException>(() => ((IList<GradientStop>)brush.Stops)[0] = default);
    }

    [Fact]
    public void OddDashPatternRepeatsToPreserveAlternatingCoverage()
    {
        float[] dashes = [2, 3, 4];
        var stroke = new StrokeStyle(1, dashes: dashes);

        dashes[0] = 9;

        Assert.Equal(new float[] { 2, 3, 4, 2, 3, 4 }, stroke.Dashes);
    }

    [Fact]
    public void ClosingAtTheStartStillClosesTheStrokeJoin()
    {
        var path = new PathBuilder().MoveTo(Vector2.Zero).LineTo(Vector2.One).LineTo(Vector2.Zero).Close().Build();

        Assert.Equal(PathSegmentKind.Close, path.Segments[^1].Kind);
    }

    [Fact]
    public void PathBuildKeepsCurvesAndOwnsItsSegments()
    {
        var builder = new PathBuilder().MoveTo(Vector2.Zero).CubicTo(new(1, 0), new(1, 1), Vector2.One);
        var original = builder.Build();

        builder.LineTo(new(2, 2));

        Assert.Collection(original.Segments, segment => Assert.Equal(PathSegmentKind.Move, segment.Kind), segment => Assert.Equal(new PathSegment(PathSegmentKind.Cubic, Vector2.One, new(1, 0), new(1, 1)), segment));
    }

    [Fact]
    public void PolygonOwnsItsTriangleVertices()
    {
        Vector2[] points = [Vector2.Zero, Vector2.UnitX, Vector2.One, Vector2.UnitY];
        var geometry = PolygonGeometry.FromConvexPolygon(points);
        points[0] = new(20, 20);

        Assert.Equal(new[] { Vector2.Zero, Vector2.UnitX, Vector2.One, Vector2.Zero, Vector2.One, Vector2.UnitY }, geometry.Vertices);
    }

    [Fact]
    public void SrgbColorConvertsBeforePremultiplication()
    {
        var color = Color.FromSrgb(0.5f, 0, 1, 0.5f);

        Assert.InRange(color.Red, 0.2140f, 0.2141f);
        Assert.Equal(new Vector4(color.Red * 0.5f, 0, 0.5f, 0.5f), color.Premultiplied());
    }
}
