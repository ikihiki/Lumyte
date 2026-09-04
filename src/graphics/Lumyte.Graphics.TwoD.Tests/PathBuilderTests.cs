using System.Numerics;

namespace Lumyte.Graphics.TwoD.Tests;

public sealed class PathBuilderTests
{
    [Fact]
    public void BuilderPreservesCurveControlBounds()
    {
        PathGeometry path = new PathBuilder()
            .MoveTo(new(2, 3))
            .QuadraticTo(new(8, 1), new(10, 5))
            .CubicTo(new(11, 7), new(4, 9), new(2, 3))
            .Build();

        Assert.Equal(new Rect(2, 1, 9, 8), path.Bounds);
        Assert.False(path.IsEmpty);
    }

    [Fact]
    public void DrawableSegmentRequiresMoveTo()
    {
        var builder = new PathBuilder();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => builder.LineTo(Vector2.One));

        Assert.Contains("MoveTo", exception.Message, StringComparison.Ordinal);
    }
}
