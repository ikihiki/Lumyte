using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks inclusive rounded-rectangle samples independently of production containment.</summary>
public sealed class SparseRoundedRectBoundaryTests
{
    /// <summary>Checks zero, intermediate, maximal and clamped radii on exact dyadic sample boundaries.</summary>
    /// <param name="radius">The public corner radius.</param>
    /// <param name="scenario">Identity, translation, reflection or scale.</param>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.0625f, 0)]
    [InlineData(0.125f, 0)]
    [InlineData(0.25f, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(0.0625f, 1)]
    [InlineData(0.125f, 1)]
    [InlineData(0.25f, 1)]
    [InlineData(1, 1)]
    [InlineData(0, 2)]
    [InlineData(0.0625f, 2)]
    [InlineData(0.125f, 2)]
    [InlineData(0.25f, 2)]
    [InlineData(1, 2)]
    [InlineData(0, 3)]
    [InlineData(0.0625f, 3)]
    [InlineData(0.125f, 3)]
    [InlineData(0.25f, 3)]
    [InlineData(1, 3)]
    public void SamplesMatchInclusiveSignedDistance(float radius, int scenario)
    {
        Matrix3x2 transform = scenario switch
        {
            0 => Matrix3x2.Identity,
            1 => Matrix3x2.CreateTranslation(2, 3),
            2 => new(-1, 0, 0, 1, 3, 1),
            _ => Matrix3x2.CreateScale(2),
        };
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.ConcatTransform(transform);
        canvas.FillRoundedRect(new(0.125f, 0.125f, 0.5f, 0.5f), radius, SRgba<float>.Red);
        SparseStripPlan2D plan = SparseStripBuilder2D.Create(canvas.End(), new DrawingTargetInfo2D
        {
            Description = new() { Width = 8, Height = 8, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment },
        });
        ushort[,] actual = new ushort[8, 8];
        foreach (SparseStrip2D strip in plan.Strips)
        {
            for (int offset = 0; offset < strip.Length; offset++)
            {
                Assert.Equal((ushort)0, actual[strip.X + offset, strip.Y]);
                actual[strip.X + offset, strip.Y] = strip.MaskOffset < 0 ? ushort.MaxValue : plan.BoundaryMasks[strip.MaskOffset + offset];
            }
        }

        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                ushort expected = 0;
                for (int bit = 0; bit < 16; bit++)
                {
                    double u = (x + (((bit % 4) + 0.5) / 4) - transform.M31) / transform.M11;
                    double v = (y + (((bit / 4) + 0.5) / 4) - transform.M32) / transform.M22;

                    // Independent baseline drawing-fragment.slang Kind 2 signed-distance
                    // equation. In particular it has no half-open rectangular prefilter.
                    double r = Math.Min(radius, 0.25);
                    double qx = Math.Abs(u - 0.375) - 0.25 + r;
                    double qy = Math.Abs(v - 0.375) - 0.25 + r;
                    double outsideX = Math.Max(qx, 0), outsideY = Math.Max(qy, 0);
                    double distance = Math.Sqrt((outsideX * outsideX) + (outsideY * outsideY)) + Math.Min(Math.Max(qx, qy), 0);
                    if (distance <= r)
                    {
                        expected |= (ushort)(1 << bit);
                    }
                }

                Assert.Equal(expected, actual[x, y]);
            }
        }

        if (scenario == 0)
        {
            Assert.Equal(radius == 0 ? (ushort)0x0777 : (ushort)0x0272, actual[0, 0]);
            Assert.Equal(radius == 0 ? 9 : 5, BitOperations.PopCount(actual[0, 0]));
        }
    }
}
