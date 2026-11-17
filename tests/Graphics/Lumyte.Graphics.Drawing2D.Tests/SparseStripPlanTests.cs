using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Independent scalar membership oracles for the CPU sparse representation.</summary>
public sealed class SparseStripPlanTests
{
    /// <summary>Known interiors are compressed, order and original paint/clip identity survive.</summary>
    [Fact]
    public void RectanglesCompressWithoutBoundaryMasksAndPreserveOrder()
    {
        var first = new FillRectCommand2D(new(1, 2, 6, 4), Paint(), State());
        var second = new FillCircleCommand2D(new(4, 4), 2, Paint(), State());
        var list = new DrawList2D([first, second]);
        SparseStripPlan2D plan = SparseStripBuilder2D.Create(list, Target());
        Assert.Same(first, plan.Commands[0]);
        Assert.Same(second, plan.Commands[1]);
        SparseStrip2D[] runs = plan.Strips.Where(strip => strip.CommandOrdinal == 0).ToArray();
        Assert.Equal(4, runs.Length);
        Assert.All(runs, strip =>
        {
            Assert.Equal(1, strip.X);
            Assert.Equal(6, strip.Length);
            Assert.Equal(-1, strip.MaskOffset);
        });
        Assert.Equal(plan.Strips.OrderBy(strip => strip.CommandOrdinal), plan.Strips);
        Expand(plan, 0);
        Expand(plan, 1);
    }

    /// <summary>Bits retain correlation rather than reducing the same population count to alpha.</summary>
    [Fact]
    public void BoundaryBitsAreNotAveragedAndHonorHalfOpenEdges()
    {
        var command = new FillRectCommand2D(new(0.125f, 0.125f, 0.5f, 0.5f), Paint(), State());
        SparseStripPlan2D plan = SparseStripBuilder2D.Create(command, Target());
        Assert.Equal((ushort)0x0033, Assert.Single(plan.BoundaryMasks));
        Assert.Equal((ushort)0x0033, Expand(plan, 0)[0, 0]);
    }

    /// <summary>Random self-intersections, holes, reflections, DPR and origins use an independent FMA oracle.</summary>
    /// <param name="rule">The path fill rule.</param>
    [Theory]
    [InlineData(FillRule2D.NonZero)]
    [InlineData(FillRule2D.EvenOdd)]
    public void PathsMatchIndependentOrderedFusedOracle(FillRule2D rule)
    {
        var random = new Random(712684);
        for (int trial = 0; trial < 40; trial++)
        {
            var builder = new PathBuilder2D();
            var contours = new List<Vector2[]>();
            for (int c = 0; c < 2; c++)
            {
                var points = new Vector2[7];
                for (int i = 0; i < points.Length; i++)
                {
                    points[i] = new(random.Next(-8, 72) / 8f, random.Next(-8, 72) / 8f);
                }

                contours.Add(points);
                builder.MoveTo(points[0]);
                foreach (Vector2 point in points.Skip(1))
                {
                    builder.LineTo(point);
                }

                if (c == 0)
                {
                    builder.Close();
                }
            }

            Matrix3x2 transform = trial % 2 == 0 ? new(1, 0.125f, -0.25f, 1, 0.5f, -0.5f) : new(-1, 0, 0, 1, 8, 0);
            DrawingTargetInfo2D target = Target() with { PixelScale = 1.25f, PixelOrigin = new(1, -1) };
            var command = new FillPathCommand2D(builder.Build(), Paint(), rule, State() with { Transform = transform });
            SparseStripPlan2D plan = SparseStripBuilder2D.Create(command, target);
            ushort[,] actual = Expand(plan, 0);
            Matrix3x2 device = transform * Matrix3x2.CreateScale(target.PixelScale) * Matrix3x2.CreateTranslation(-target.PixelOrigin);
            for (int y = 0; y < 12; y++)
            {
                for (int x = 0; x < 12; x++)
                {
                    ushort expected = 0;
                    for (int bit = 0; bit < 16; bit++)
                    {
                        double sx = x + (((bit % 4) + 0.5) / 4), sy = y + (((bit / 4) + 0.5) / 4);
                        int sum = 0;
                        foreach (Vector2[] contour in contours)
                        {
                            for (int i = 0; i < contour.Length; i++)
                            {
                                (double ax, double ay) = Transform(contour[i], device);
                                (double bx, double by) = Transform(contour[(i + 1) % contour.Length], device);
                                double side = Math.FusedMultiplyAdd(bx - ax, sy - ay, -Math.FusedMultiplyAdd(by - ay, sx - ax, 0));
                                sum += ay <= sy && by > sy && side > 0 ? 1 : by <= sy && ay > sy && side < 0 ? -1 : 0;
                            }
                        }

                        if (rule == FillRule2D.NonZero ? sum != 0 : (sum & 1) != 0)
                        {
                            expected |= (ushort)(1 << bit);
                        }
                    }

                    Assert.Equal(expected, actual[x, y]);
                }
            }
        }
    }

    /// <summary>Stroke body triangles are unioned, not blended twice along their shared diagonal.</summary>
    /// <param name="cap">The open-end style.</param>
    /// <param name="left">The inclusive left edge.</param>
    /// <param name="right">The exclusive right edge.</param>
    [Theory]
    [InlineData(LineCap2D.Butt, 2, 6)]
    [InlineData(LineCap2D.Square, 1, 7)]
    public void LineStrokeMatchesAnalyticRectangle(LineCap2D cap, int left, int right)
    {
        var command = new DrawLineCommand2D(new(2, 4), new(6, 4), Paint(), new() { Width = 2, Cap = cap }, State());
        SparseStripPlan2D plan = SparseStripBuilder2D.Create(command, Target());
        ushort[,] actual = Expand(plan, 0);
        for (int y = 0; y < 12; y++)
        {
            for (int x = 0; x < 12; x++)
            {
                Assert.Equal(x >= left && x < right && y >= 3 && y < 5 ? ushort.MaxValue : (ushort)0, actual[x, y]);
            }
        }

        Assert.Empty(plan.BoundaryMasks);
    }

    /// <summary>Analytic simple shapes retain every bit of boundary coverage.</summary>
    [Fact]
    public void CircleMatchesIndependentSquaredDistanceOracle()
    {
        var command = new FillCircleCommand2D(new(5.25f, 4.5f), 2.75f, Paint(), State());
        SparseStripPlan2D plan = SparseStripBuilder2D.Create(command, Target());
        ushort[,] actual = Expand(plan, 0);
        for (int y = 0; y < 12; y++)
        {
            for (int x = 0; x < 12; x++)
            {
                ushort expected = 0;
                for (int bit = 0; bit < 16; bit++)
                {
                    double dx = x + (((bit % 4) + 0.5) / 4) - 5.25;
                    double dy = y + (((bit / 4) + 0.5) / 4) - 4.5;
                    if ((dx * dx) + (dy * dy) <= 2.75 * 2.75)
                    {
                        expected |= (ushort)(1 << bit);
                    }
                }

                Assert.Equal(expected, actual[x, y]);
            }
        }

        Assert.NotEmpty(plan.BoundaryMasks);
        Assert.Equal(plan.Strips, SparseStripBuilder2D.Create(command, Target(), plan.WorkingBytes).Strips);
        Assert.Throws<NotSupportedException>(() => SparseStripBuilder2D.Create(command, Target(), plan.WorkingBytes - 1));
    }

    /// <summary>Mesh interpolation is explicitly deferred, never reduced to sixteen binary shape samples.</summary>
    [Fact]
    public void MeshRetainsCenterCoverageDescriptor()
    {
        MeshVertex2D[] vertices = [new(new(1, 1), 0), new(new(5, 1), 1), new(new(1, 5), 0.5f)];
        var command = new DrawMeshCommand2D(new(vertices, [0, 1, 2]), Paint(), State());
        SparseStripPlan2D plan = SparseStripBuilder2D.Create(command, Target());
        Assert.All(plan.Strips, strip => Assert.Equal(SparseStripEvaluation2D.MeshCenter, strip.Evaluation));
        Assert.Empty(plan.BoundaryMasks);
        Assert.Same(command, Assert.Single(plan.Commands));
    }

    /// <summary>Budgets, options, custom commands and cancellation fail explicitly without another backend.</summary>
    [Fact]
    public void InvalidUnsupportedAndBudgetFailuresAreTruthful()
    {
        var command = new FillRectCommand2D(new(0, 0, 8, 8), Paint(), State());
        Assert.Throws<ArgumentOutOfRangeException>(() => SparseStripBuilder2D.Create(command, Target(), 0));
        Assert.Throws<NotSupportedException>(() => SparseStripBuilder2D.Create(command, Target(), 1024));
        Assert.Throws<ArgumentException>(() => SparseStripBuilder2D.Create(command, Target() with { PixelOrigin = new(0.5f, 0) }));
        Assert.Throws<NotSupportedException>(() => SparseStripBuilder2D.Create(new CustomCommand(State()), Target()));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => SparseStripBuilder2D.Create(command, Target(), cancellationToken: cancellation.Token));
        Assert.Empty(SparseStripBuilder2D.Create(command with { State = State() with { Transform = new(1, 0, 0, 0, 0, 0) } }, Target()).Strips);
    }

    private static ushort[,] Expand(SparseStripPlan2D plan, int ordinal)
    {
        ushort[,] result = new ushort[12, 12];
        foreach (SparseStrip2D strip in plan.Strips.Where(strip => strip.CommandOrdinal == ordinal))
        {
            for (int offset = 0; offset < strip.Length; offset++)
            {
                Assert.Equal((ushort)0, result[strip.X + offset, strip.Y]);
                ushort bits = strip.MaskOffset < 0 ? ushort.MaxValue : plan.BoundaryMasks[strip.MaskOffset + offset];
                Assert.NotEqual((ushort)0, bits);
                if (strip.MaskOffset >= 0)
                {
                    Assert.NotEqual(ushort.MaxValue, bits);
                }

                result[strip.X + offset, strip.Y] = bits;
            }
        }

        return result;
    }

    private static (double X, double Y) Transform(Vector2 p, Matrix3x2 m) =>
        (Math.FusedMultiplyAdd((double)p.X, m.M11, Math.FusedMultiplyAdd((double)p.Y, m.M21, m.M31)),
        Math.FusedMultiplyAdd((double)p.X, m.M12, Math.FusedMultiplyAdd((double)p.Y, m.M22, m.M32)));

    private static PaintGraph2D Paint() => PaintGraph2D.Solid(SRgba<float>.Red);

    private static DrawingState2D State() => new(Matrix3x2.Identity, ClipStack2D.Empty);

    private static DrawingTargetInfo2D Target() => new()
    {
        Description = new() { Width = 12, Height = 12, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment },
    };

    private sealed record CustomCommand(DrawingState2D State) : DrawingCommand2D(State);
}
