using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Compares canonical geometry adoption against the original clip arithmetic.</summary>
public sealed class ClipGeometryIntegrationTests
{
    /// <summary>All mask samples preserve old double arithmetic across transforms and multiple contours.</summary>
    /// <param name="rule">The fill rule.</param>
    /// <param name="reverse">Whether the inner contour is reversed.</param>
    [Theory]
    [InlineData(FillRule2D.NonZero, false)]
    [InlineData(FillRule2D.NonZero, true)]
    [InlineData(FillRule2D.EvenOdd, false)]
    [InlineData(FillRule2D.EvenOdd, true)]
    public void TransformedMasksMatchOriginalOracle(FillRule2D rule, bool reverse)
    {
        Vector2[][] contours = [[new(0.125f, 0.125f), new(5.625f, 0.125f), new(3.125f, 5.625f)], [new(1.625f, 1.125f), new(3.625f, 1.125f), new(2.625f, 3.625f)]];
        if (reverse)
        {
            Array.Reverse(contours[1]);
        }

        var builder = new PathBuilder2D();
        foreach (Vector2[] contour in contours)
        {
            builder.MoveTo(contour[0]);
            foreach (Vector2 point in contour.Skip(1))
            {
                builder.LineTo(point);
            }

            builder.Close();
        }

        Matrix3x2[] transforms = [Matrix3x2.Identity, new(1.125f, 0.25f, -0.125f, 0.875f, 1.125f, 0.625f), new(-1, 0, 0, 1, 7, 0)];
        foreach (Matrix3x2 transform in transforms)
        {
            var target = new DrawingTargetInfo2D { Description = new() { Width = 16, Height = 16, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment } };
            var clip = NormalizedClip2D.Create([new(builder.Build(), transform, rule)], target);
            Assert.Equal(NormalizedClipKind2D.SampleMask, clip.Kind);
            ((double X, double Y) A, (double X, double Y) B)[] edges = contours.SelectMany(contour => contour.Select((a, i) => (A: Transform(a, transform), B: Transform(contour[(i + 1) % contour.Length], transform)))).ToArray();
            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    uint expected = 0;
                    for (int sy = 0; sy < 4; sy++)
                    {
                        for (int sx = 0; sx < 4; sx++)
                        {
                            double px = x + ((sx + 0.5) / 4), py = y + ((sy + 0.5) / 4);
                            int winding = 0;
                            foreach (((double X, double Y) A, (double X, double Y) B) edge in edges)
                            {
                                double side = Math.FusedMultiplyAdd(edge.B.X - edge.A.X, py - edge.A.Y, -Math.FusedMultiplyAdd(edge.B.Y - edge.A.Y, px - edge.A.X, 0));
                                if (edge.A.Y <= py && edge.B.Y > py && side > 0)
                                {
                                    winding++;
                                }

                                if (edge.B.Y <= py && edge.A.Y > py && side < 0)
                                {
                                    winding--;
                                }
                            }

                            if (rule == FillRule2D.NonZero ? winding != 0 : (Math.Abs(winding) % 2) != 0)
                            {
                                expected |= 1u << ((sy * 4) + sx);
                            }
                        }
                    }

                    uint actual = 0;
                    if (x >= clip.Bounds.X && x < clip.Bounds.X + clip.Bounds.Width && y >= clip.Bounds.Y && y < clip.Bounds.Y + clip.Bounds.Height)
                    {
                        int index = ((y - (int)clip.Bounds.Y) * (int)clip.Bounds.Width) + x - (int)clip.Bounds.X;
                        actual = (uint)clip.Mask[index / 4][index % 4];
                    }

                    Assert.Equal(expected, actual);
                }
            }
        }
    }

    /// <summary>Raw edge enclosure must retain horizontal and vertical segments rather than ignore their empty area.</summary>
    [Fact]
    public void AxisAlignedPathEdgesRetainMaskBounds()
    {
        var builder = new PathBuilder2D();
        builder.MoveTo(new(1.125f, 1.125f));
        builder.LineTo(new(5.625f, 1.125f));
        builder.LineTo(new(5.625f, 4.625f));
        builder.LineTo(new(1.125f, 4.625f));
        builder.Close();
        builder.MoveTo(new(2.125f, 2.125f));
        builder.LineTo(new(4.625f, 2.125f));
        builder.LineTo(new(4.625f, 3.625f));
        builder.LineTo(new(2.125f, 3.625f));
        builder.Close();
        var target = new DrawingTargetInfo2D { Description = new() { Width = 8, Height = 8, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment } };
        var clip = NormalizedClip2D.Create([new(builder.Build(), Matrix3x2.Identity, FillRule2D.EvenOdd)], target);
        Assert.Equal(NormalizedClipKind2D.SampleMask, clip.Kind);
        Assert.Equal(new ScissorRect(1, 1, 5, 4), clip.Bounds);
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 5; x++)
            {
                uint expected = 0;
                for (int sy = 0; sy < 4; sy++)
                {
                    for (int sx = 0; sx < 4; sx++)
                    {
                        double px = 1 + x + ((sx + 0.5) / 4), py = 1 + y + ((sy + 0.5) / 4);
                        bool outer = px >= 1.125 && px < 5.625 && py >= 1.125 && py < 4.625;
                        bool inner = px >= 2.125 && px < 4.625 && py >= 2.125 && py < 3.625;
                        if (outer && !inner)
                        {
                            expected |= 1u << ((sy * 4) + sx);
                        }
                    }
                }

                int index = (y * 5) + x;
                Assert.Equal(expected, (uint)clip.Mask[index / 4][index % 4]);
            }
        }
    }

    /// <summary>Invalid coordinates keep renderer rejection semantics rather than leaking point validation errors.</summary>
    [Fact]
    public void NonFinitePathCoordinatesRemainNotSupported()
    {
        System.Reflection.MethodInfo method = typeof(NormalizedClip2D).GetMethod("Transform", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        System.Reflection.TargetInvocationException exception = Assert.Throws<System.Reflection.TargetInvocationException>(() => method.Invoke(null, [new Vector2(float.PositiveInfinity, 1), Matrix3x2.Identity]));
        Assert.IsType<NotSupportedException>(exception.InnerException);
    }

    private static (double X, double Y) Transform(Vector2 p, Matrix3x2 m)
        => (Math.FusedMultiplyAdd(m.M11, p.X, Math.FusedMultiplyAdd(m.M21, p.Y, m.M31)), Math.FusedMultiplyAdd(m.M12, p.X, Math.FusedMultiplyAdd(m.M22, p.Y, m.M32)));
}
