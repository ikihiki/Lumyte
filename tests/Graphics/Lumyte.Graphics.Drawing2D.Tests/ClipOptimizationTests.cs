using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Bit-exact differential tests against the preserved pre-optimization rasterizer.</summary>
public sealed class ClipOptimizationTests
{
    /// <summary>Random contours include self intersections, degenerate edges, holes and adjacent floats.</summary>
    [Fact]
    public void RandomMasksMatchScalarReference()
    {
        var random = new Random(731429);
        for (int trial = 0; trial < 320; trial++)
        {
            var builder = new PathBuilder2D();
            for (int contour = 0; contour < 2; contour++)
            {
                builder.MoveTo(new(0.125f, 0.125f));
                for (int i = 0; i < 9; i++)
                {
                    float x = random.Next(-8, 65) / 8f, y = random.Next(-8, 65) / 8f;
                    builder.LineTo(new(trial % 3 == 0 ? MathF.BitIncrement(x) : x, y));
                }

                builder.Close();
            }

            Matrix3x2 transform = (trial % 4) switch
            {
                0 => Matrix3x2.Identity,
                1 => Matrix3x2.CreateRotation(0.31f),
                2 => new(-1, 0, 0, 1, 8, 0),
                _ => new(1, 0.125f, -0.25f, 1, 0.125f, -0.375f),
            };
            DrawingTargetInfo2D target = Target() with { PixelScale = trial % 2 == 0 ? 1 : 1.25f, PixelOrigin = new(0.125f, -0.375f) };
            if (trial % 17 == 0)
            {
                transform = new(1, 0, 0, 0, 0, 0);
            }

            ClipElement2D[] elements = trial % 3 == 0
                ? [new(builder.Build(), transform, (FillRule2D)(trial % 2)), new(builder.Build(), Matrix3x2.CreateTranslation(0.125f, 0.375f), FillRule2D.EvenOdd)]
                : [new(builder.Build(), transform, (FillRule2D)(trial % 2))];
            var expected = ScalarClipReference2D.Create(elements, target);
            var actual = NormalizedClip2D.Create(elements, target);
            Assert.Equal(expected.Kind, actual.Kind);
            Assert.Equal(expected.Bounds, actual.Bounds);
            Assert.Equal(expected.Samples, actual.Samples);
            Assert.Equal(expected.Mask, actual.Mask);
        }
    }

    /// <summary>Separate immutable stacks and paths share only exactly equivalent captured geometry.</summary>
    [Fact]
    public void StructuralReuseIsPreparationLocalAndTransformSensitive()
    {
        static Path2D Path()
        {
            var b = new PathBuilder2D();
            b.MoveTo(new(0.125f, 0));
            b.LineTo(new(7, 0));
            b.LineTo(new(0, 7));
            b.Close();
            return b.Build();
        }

        var cache = new ClipNormalizationCache2D(Target());
        ClipElement2D[] a = [new(Path(), Matrix3x2.Identity, FillRule2D.NonZero)];
        ClipElement2D[] b = [new(Path(), Matrix3x2.Identity, FillRule2D.NonZero)];
        NormalizedClip2D first = cache.Get(new object(), Matrix3x2.Identity, a);
        Assert.Same(first, cache.Get(new object(), Matrix3x2.Identity, b));
        Assert.NotSame(first, cache.Get(new object(), Matrix3x2.Identity, [b[0] with { Transform = Matrix3x2.CreateTranslation(0.25f, 0) }]));
        Assert.NotSame(first, cache.Get(new object(), Matrix3x2.Identity, [b[0] with { Rule = FillRule2D.EvenOdd }]));
        Assert.NotSame(first, cache.Get(new object(), Matrix3x2.Identity, b, pixelSpace: true));
        cache.Clear();
        foreach (string name in new[] { "_clips", "_equivalent" })
        {
            var dictionary = (System.Collections.IDictionary)typeof(ClipNormalizationCache2D).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(cache)!;
            Assert.Empty(dictionary);
        }

        Assert.NotSame(first, cache.Get(new object(), Matrix3x2.Identity, a));
    }

    /// <summary>Identity aliases stop at the bounded entry limit and clear all accounting.</summary>
    [Fact]
    public void EquivalentAliasAdmissionIsBounded()
    {
        var b = new PathBuilder2D();
        b.MoveTo(Vector2.Zero);
        b.LineTo(new(1, 0));
        b.LineTo(new(0, 1));
        b.Close();
        ClipElement2D[] elements = [new(b.Build(), Matrix3x2.Identity, FillRule2D.NonZero)];
        var cache = new ClipNormalizationCache2D(Target());
        for (int i = 0; i < 4100; i++)
        {
            cache.Get(new object(), Matrix3x2.Identity, elements);
        }

        System.Reflection.FieldInfo field = typeof(ClipNormalizationCache2D).GetField("_clips", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        Assert.Equal(4096, ((System.Collections.IDictionary)field.GetValue(cache)!).Count);
        cache.Clear();
        foreach (string name in new[] { "_bytes", "_geometryBytes" })
        {
            Assert.Equal(0L, typeof(ClipNormalizationCache2D).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(cache));
        }
    }

    /// <summary>Extreme finite float inputs and curves preserve strict sample transitions.</summary>
    [Fact]
    public void ExtremeAndCurvedMasksMatchScalarReference()
    {
        foreach (float scale in new[] { 1f, 1e30f, 1e-20f })
        {
            foreach (float edge in new[] { 0.125f, MathF.BitIncrement(0.125f), MathF.BitDecrement(0.125f) })
            {
                var b = new PathBuilder2D();
                b.MoveTo(new(edge * scale, 0));
                b.CubicTo(new(6 * scale, -scale), new(7 * scale, 6 * scale), new(edge * scale, 7 * scale));
                b.LineTo(new(edge * scale, 0));
                b.Close();
                b.MoveTo(new(edge * scale, 0));
                b.LineTo(new(edge * scale, 7 * scale));
                b.CubicTo(new(7 * scale, 6 * scale), new(6 * scale, -scale), new(edge * scale, 0));
                b.Close();
                ClipElement2D[] elements = [new(b.Build(), Matrix3x2.CreateScale(1 / scale), FillRule2D.NonZero)];
                var expected = ScalarClipReference2D.Create(elements, Target());
                var actual = NormalizedClip2D.Create(elements, Target());
                Assert.Equal(expected.Kind, actual.Kind);
                Assert.Equal(expected.Bounds, actual.Bounds);
                Assert.Equal(expected.Mask, actual.Mask);
            }
        }
    }

    /// <summary>Faster predicates never relax the original work and area admission policy.</summary>
    [Fact]
    public void WorkBudgetRejectionMatchesScalarReference()
    {
        var b = new PathBuilder2D();
        b.MoveTo(Vector2.Zero);
        b.LineTo(new(2048, 0));
        b.LineTo(new(0, 2048));
        b.Close();
        ClipElement2D[] elements = [new(b.Build(), Matrix3x2.Identity, FillRule2D.NonZero)];
        DrawingTargetInfo2D target = Target() with { Description = Target().Description with { Width = 2048, Height = 2048 } };
        Assert.Throws<NotSupportedException>(() => ScalarClipReference2D.Create(elements, target));
        Assert.Throws<NotSupportedException>(() => NormalizedClip2D.Create(elements, target));
        ClipElement2D[] tooMany = Enumerable.Repeat(elements[0], 4097).ToArray();
        Assert.Throws<NotSupportedException>(() => new ClipNormalizationCache2D(target).Get(new object(), Matrix3x2.Identity, tooMany));
    }

    private static DrawingTargetInfo2D Target() => new()
    {
        Description = new() { Width = 12, Height = 12, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment },
    };
}
