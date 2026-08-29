using System.Numerics;

using Xunit;

namespace Lumyte.Mathematics.Tests;

public sealed class Geometry2DTests
{
    [Fact]
    public void ClosestPointProjectsOntoTheSegment()
    {
        Vector2 closest = Geometry2D.ClosestPointOnSegment(
            new Vector2(5f, 3f),
            Vector2.Zero,
            new Vector2(10f, 0f));

        Assert.Equal(new Vector2(5f, 0f), closest);
    }

    [Theory]
    [InlineData(-2f, 0f)]
    [InlineData(12f, 10f)]
    public void ClosestPointClampsToSegmentEndpoints(float pointX, float expectedX)
    {
        Vector2 closest = Geometry2D.ClosestPointOnSegment(
            new Vector2(pointX, 0f),
            Vector2.Zero,
            new Vector2(10f, 0f));

        Assert.Equal(new Vector2(expectedX, 0f), closest);
    }

    [Fact]
    public void DegenerateSegmentUsesItsStartPoint()
    {
        var point = new Vector2(3f, 4f);

        Vector2 closest = Geometry2D.ClosestPointOnSegment(
            point,
            Vector2.Zero,
            Vector2.Zero);
        float distance = Geometry2D.DistancePointToSegment(
            point,
            Vector2.Zero,
            Vector2.Zero);

        Assert.Equal(Vector2.Zero, closest);
        Assert.Equal(5f, distance);
    }
}
