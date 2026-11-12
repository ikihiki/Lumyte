using System.Numerics;
using System.Runtime.CompilerServices;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Documents why general CPU clip predicates need a shared arithmetic contract.</summary>
public sealed class ClipArithmeticBoundaryTests
{
    /// <summary>A permitted contraction changes winding at an actual correlated AA sample.</summary>
    [Fact]
    public void CrossProductContractionChangesSampleMembership()
    {
        float a = MathF.BitIncrement(1f);
        float c = MathF.BitIncrement(a);
        Vector2 pixel = new(0.125f);
        var inverse = new Matrix3x2(8, 0, 0, 1, 0, a - pixel.Y);
        Assert.True(Matrix3x2.Invert(inverse, out _));
        var point = Vector2.Transform(pixel, inverse);
        Assert.Equal(new Vector2(1, a), point);
        Vector2[] triangle = [Vector2.Zero, new(a, c), new(0, 2)];

        Assert.Equal(0f, SeparateProduct(a, a) - SeparateProduct(c, 1));
        Assert.Equal(MathF.ScaleB(1f, -46), MathF.FusedMultiplyAdd(a, a, -c));
        Assert.Equal(0, Winding(triangle, point, fused: false));
        Assert.Equal(1, Winding(triangle, point, fused: true));
    }

    private static int Winding(Vector2[] points, Vector2 point, bool fused)
    {
        int winding = 0;
        for (int i = 0; i < points.Length; i++)
        {
            Vector2 start = points[i];
            Vector2 end = points[(i + 1) % points.Length];
            Vector2 edge = end - start;
            Vector2 delta = point - start;
            float second = SeparateProduct(edge.Y, delta.X);
            float side = fused ? MathF.FusedMultiplyAdd(edge.X, delta.Y, -second) : SeparateProduct(edge.X, delta.Y) - second;
            if (start.Y <= point.Y && end.Y > point.Y && side > 0)
            {
                winding++;
            }

            if (end.Y <= point.Y && start.Y > point.Y && side < 0)
            {
                winding--;
            }
        }

        return winding;
    }

    // A call boundary forces a rounded multiply, independent of the JIT's FMA choices.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static float SeparateProduct(float a, float b) => a * b;
}
