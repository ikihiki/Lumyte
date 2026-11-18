using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Public shape intent preserves strict circle and inclusive rounded-corner boundaries.</summary>
public sealed class SparseCircleBoundaryTests
{
    /// <summary>Every lowered sample agrees with an independent exact dyadic circle equation.</summary>
    /// <param name="rounded">Whether the identical geometric bounds belong to a rounded rectangle.</param>
    /// <param name="scenario">Identity, translation, reflection or uniform scaling.</param>
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    public void PublicShapeIntentKeepsDistinctBoundaryMasks(bool rounded, int scenario)
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
        if (rounded)
        {
            canvas.FillRoundedRect(new(0.125f, 0.125f, 1, 1), 0.5f, SRgba<float>.Red);
        }
        else
        {
            canvas.FillCircle(new(0.625f, 0.625f), 0.5f, SRgba<float>.Red);
        }

        DrawList2D list = canvas.End();
        var target = new DrawingTargetInfo2D
        {
            Description = new() { Width = 12, Height = 12, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment },
        };
        SparseStripPlan2D plan = SparseStripBuilder2D.Create(list, target);
        ushort[,] actual = new ushort[12, 12];
        foreach (SparseStrip2D strip in plan.Strips)
        {
            Assert.Equal(0, strip.CommandOrdinal);
            for (int offset = 0; offset < strip.Length; offset++)
            {
                Assert.Equal((ushort)0, actual[strip.X + offset, strip.Y]);
                actual[strip.X + offset, strip.Y] = strip.MaskOffset < 0 ? ushort.MaxValue : plan.BoundaryMasks[strip.MaskOffset + offset];
            }
        }

        for (int y = 0; y < 12; y++)
        {
            for (int x = 0; x < 12; x++)
            {
                ushort expected = 0;
                for (int bit = 0; bit < 16; bit++)
                {
                    // All values are dyadic and exact. In eighth-unit local coordinates the
                    // independent circle equation is (u-5)^2+(v-5)^2 < 16, not Shape.Contains.
                    double u = ((8 * x) + (2 * (bit % 4)) + 1 - (8 * transform.M31)) / transform.M11;
                    double v = ((8 * y) + (2 * (bit / 4)) + 1 - (8 * transform.M32)) / transform.M22;
                    double squared = ((u - 5) * (u - 5)) + ((v - 5) * (v - 5));
                    bool inside = rounded ? squared <= 16 && u >= 1 && u < 9 && v >= 1 && v < 9 : squared < 16;
                    if (inside)
                    {
                        expected |= (ushort)(1 << bit);
                    }
                }

                Assert.Equal(expected, actual[x, y]);
            }
        }

        if (scenario == 0)
        {
            Assert.Equal(rounded ? 11 : 9, BitOperations.PopCount(actual[0, 0]));
            Assert.Equal(rounded ? (ushort)0xefe4 : (ushort)0xeee0, actual[0, 0]);
        }
    }
}
