using System.Numerics;

using Lumyte.Graphics.TwoD;

namespace Lumyte.Graphics.Native.Passes.Tests;

public sealed class NativePathPreparationTests
{
    [Theory]
    [InlineData(StrokeCap.Butt, false)]
    [InlineData(StrokeCap.Square, true)]
    [InlineData(StrokeCap.Round, true)]
    public void CapsDefineCoverageBeyondTheEndpoint(StrokeCap cap, bool expected)
    {
        var path = new PathBuilder().MoveTo(new(4, 8)).LineTo(new(20, 8)).Build();
        List<Vector4> edges = [];

        NativePathPreparation.StrokeEdges(edges, NativePathPreparation.Flatten(path, .02f), new(4, cap: cap), .02f);

        Assert.Equal(expected, Contains(edges, new(3, 8)));
    }

    [Fact]
    public void DashOffsetMovesVisibleIntervals()
    {
        var path = new PathBuilder().MoveTo(new(0, 4)).LineTo(new(30, 4)).Build();
        List<Vector4> edges = [];

        NativePathPreparation.StrokeEdges(edges, NativePathPreparation.Flatten(path, .02f), new(2, dashes: [4, 2], dashOffset: 3), .02f);

        Assert.Equal([true, false, true], new[] { .5f, 2f, 4f }.Select(x => Contains(edges, new(x, 4))));
    }

    [Theory]
    [InlineData(StrokeJoin.Miter, 4, true)]
    [InlineData(StrokeJoin.Bevel, 4, false)]
    [InlineData(StrokeJoin.Miter, 1, false)]
    public void MiterLimitControlsTheOutsideCorner(StrokeJoin join, float limit, bool expected)
    {
        var path = new PathBuilder().MoveTo(new(4, 12)).LineTo(new(12, 12)).LineTo(new(12, 4)).Build();
        List<Vector4> edges = [];

        NativePathPreparation.StrokeEdges(edges, NativePathPreparation.Flatten(path, .02f), new(4, join, miterLimit: limit), .02f);

        Assert.Equal(expected, Contains(edges, new(13.7f, 13.7f)));
    }

    [Fact]
    public void RoundJoinDoesNotFillBeyondShortButtEndedSegments()
    {
        var path = new PathBuilder().MoveTo(new(0, 0)).LineTo(new(1, 0)).LineTo(new(1, 1)).Build();
        List<Vector4> edges = [];

        NativePathPreparation.StrokeEdges(edges, NativePathPreparation.Flatten(path, .02f), new(4, StrokeJoin.Round), .02f);

        Assert.False(Contains(edges, new(1.5f, 1.5f)));
    }

    [Fact]
    public void CurveSubdivisionPreservesAClosedCurvedRegion()
    {
        var path = new PathBuilder().MoveTo(new(0, 0)).CubicTo(new(0, 20), new(20, 20), new(20, 0)).Close().Build();
        List<Vector4> edges = [];

        NativePathPreparation.FillEdges(edges, NativePathPreparation.Flatten(path, .02f), Matrix3x2.Identity);

        Assert.Equal([true, false], new[] { new Vector2(10, 10), new Vector2(10, 18) }.Select(point => Contains(edges, point)));
    }

    private static bool Contains(IEnumerable<Vector4> edges, Vector2 p)
    {
        int crossings = 0;
        foreach (Vector4 edge in edges)
        {
            if (edge.Y <= p.Y && edge.W > p.Y || edge.W <= p.Y && edge.Y > p.Y)
            {
                float x = edge.X + (p.Y - edge.Y) * (edge.Z - edge.X) / (edge.W - edge.Y);
                if (x > p.X)
                { crossings += edge.W > edge.Y ? 1 : -1; }
            }
        }
        return crossings != 0;
    }
}
