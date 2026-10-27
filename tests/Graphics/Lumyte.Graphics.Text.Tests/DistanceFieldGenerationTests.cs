using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Xunit;

namespace Lumyte.Graphics.Text.Tests;

/// <summary>Validates real multichannel distance data independently of GPU reconstruction.</summary>
public sealed class DistanceFieldGenerationTests
{
    /// <summary>RGB contains independent edge distances, while median reconstruction preserves a sharp polygon.</summary>
    [Fact]
    public void MsdfContainsDistinctChannelsAndPreservesCorners()
    {
        Path2D path = BoxWithHole();
        DistanceFieldImage2D image = DistanceFieldGenerator2D.Generate(path, 2, 8, DistanceFieldMode2D.Msdf);
        Assert.Equal(image.Width * image.Height * 4, image.Pixels.Length);
        Assert.Equal(DistanceFieldMode2D.Msdf, image.Mode);
        Assert.Equal(2, image.PixelsPerUnit);
        Assert.Equal(8, image.DistanceRange);
        bool channelsDiffer = false;
        ReadOnlySpan<byte> pixels = image.Pixels.Span;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            channelsDiffer |= pixels[i] != pixels[i + 1] || pixels[i + 1] != pixels[i + 2];
        }

        Assert.True(channelsDiffer, "An MSDF must retain independent colored edge distances.");
        Assert.True(MedianAt(image, new Vector2(2, 2)) > 127);
        Assert.True(MedianAt(image, new Vector2(-1, -1)) < 127);
        Assert.True(MedianAt(image, new Vector2(16, 16)) < 127);
        Assert.True(MedianAt(image, new Vector2(31, 1)) > 127);
        Assert.True(MedianAt(image, new Vector2(33, 1)) < 127);
    }

    /// <summary>The alpha channel stores true signed distance, distinct from corner-preserving RGB pseudo-distance.</summary>
    [Fact]
    public void MtsdfAlphaPreservesHoleAndTrueDistance()
    {
        DistanceFieldImage2D image = DistanceFieldGenerator2D.Generate(BoxWithHole(), 2, 8, DistanceFieldMode2D.Mtsdf);
        Assert.True(AlphaAt(image, new Vector2(2, 16)) > 127);
        Assert.True(AlphaAt(image, new Vector2(16, 16)) < 127);
        Assert.True(AlphaAt(image, new Vector2(-1, -1)) < 127);
        bool alphaDiffers = false;
        ReadOnlySpan<byte> pixels = image.Pixels.Span;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            alphaDiffers |= pixels[i + 3] != Median(pixels[i], pixels[i + 1], pixels[i + 2]);
        }

        Assert.True(alphaDiffers, "MTSDF alpha must carry true distance rather than a copy of the RGB median.");
    }

    /// <summary>Global contour reversal retains nonzero fill coverage and generation is deterministic.</summary>
    [Fact]
    public void ReversingContoursRetainsFillAndGenerationIsDeterministic()
    {
        DistanceFieldImage2D first = DistanceFieldGenerator2D.Generate(BoxWithHole(), 1, 8);
        DistanceFieldImage2D repeated = DistanceFieldGenerator2D.Generate(BoxWithHole(), 1, 8);
        DistanceFieldImage2D reversed = DistanceFieldGenerator2D.Generate(BoxWithHole(reverse: true), 1, 8);
        Assert.Equal(first.Pixels.ToArray(), repeated.Pixels.ToArray());
        foreach (Vector2 point in new Vector2[] { new(3, 3), new(16, 3), new(16, 16), new(-2, -2), new(16, 30) })
        {
            Assert.Equal(MedianAt(first, point) > 127, MedianAt(reversed, point) > 127);
            Assert.Equal(AlphaAt(first, point) > 127, AlphaAt(reversed, point) > 127);
        }
    }

    /// <summary>TrueType quadratic and CFF cubic outlines both reach the generator without flattening away their interior.</summary>
    [Fact]
    public void QuadraticAndCubicCurvesProduceValidFields()
    {
        var quadratic = new PathBuilder2D();
        quadratic.MoveTo(new Vector2(16, 0));
        quadratic.QuadraticTo(new Vector2(32, 0), new Vector2(32, 16));
        quadratic.QuadraticTo(new Vector2(32, 32), new Vector2(16, 32));
        quadratic.QuadraticTo(new Vector2(0, 32), new Vector2(0, 16));
        quadratic.QuadraticTo(Vector2.Zero, new Vector2(16, 0));
        quadratic.Close();
        var cubic = new PathBuilder2D();
        cubic.MoveTo(new Vector2(16, 0));
        cubic.CubicTo(new Vector2(25, 0), new Vector2(32, 7), new Vector2(32, 16));
        cubic.CubicTo(new Vector2(32, 25), new Vector2(25, 32), new Vector2(16, 32));
        cubic.CubicTo(new Vector2(7, 32), new Vector2(0, 25), new Vector2(0, 16));
        cubic.CubicTo(new Vector2(0, 7), new Vector2(7, 0), new Vector2(16, 0));
        cubic.Close();
        foreach (Path2D path in new[] { quadratic.Build(), cubic.Build() })
        {
            DistanceFieldImage2D image = DistanceFieldGenerator2D.Generate(path, 1, 8);
            Assert.True(MedianAt(image, new Vector2(16, 16)) > 127);
            Assert.True(AlphaAt(image, new Vector2(16, 16)) > 127);
            Assert.True(MedianAt(image, new Vector2(0, 0)) < 127);
        }
    }

    /// <summary>Pixel rows follow logical Y-down coordinates and preserve negative fractional placement.</summary>
    [Fact]
    public void AsymmetricOutlineRetainsTopLeftPlacement()
    {
        var builder = new PathBuilder2D();
        Vector2 origin = new(-7.25f, 4.5f);
        builder.MoveTo(origin);
        builder.LineTo(origin + new Vector2(20, 0));
        builder.LineTo(origin + new Vector2(0, 40));
        builder.Close();
        DistanceFieldImage2D image = DistanceFieldGenerator2D.Generate(builder.Build(), 2, 8);
        Assert.True(image.PlaneBounds.X < origin.X);
        Assert.True(image.PlaneBounds.Y < origin.Y);
        Assert.True(MedianAt(image, origin + new Vector2(15, 3)) > 127);
        Assert.True(AlphaAt(image, origin + new Vector2(15, 3)) > 127);
        Assert.True(MedianAt(image, origin + new Vector2(15, 35)) < 127);
        Assert.True(AlphaAt(image, origin + new Vector2(15, 35)) < 127);
    }

    /// <summary>Invalid dimensions and nonfinite generation settings are rejected before allocating large buffers.</summary>
    [Fact]
    public void RejectsInvalidSettingsAndOversizedOutput()
    {
        Path2D path = BoxWithHole();
        Assert.Throws<ArgumentNullException>(() => DistanceFieldGenerator2D.Generate(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => DistanceFieldGenerator2D.Generate(path, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DistanceFieldGenerator2D.Generate(path, float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => DistanceFieldGenerator2D.Generate(path, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DistanceFieldGenerator2D.Generate(path, 1, float.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => DistanceFieldGenerator2D.Generate(path, 1, 8, (DistanceFieldMode2D)99));
        Assert.Throws<NotSupportedException>(() => DistanceFieldGenerator2D.Generate(path, 1, 8, maximumDimension: 4));
        DistanceFieldImage2D empty = DistanceFieldGenerator2D.Generate(new PathBuilder2D().Build());
        Assert.Equal(1, empty.Width);
        Assert.Equal(1, empty.Height);
        Assert.All(empty.Pixels.ToArray(), value => Assert.Equal(0, value));
    }

    private static Path2D BoxWithHole(bool reverse = false)
    {
        var builder = new PathBuilder2D();
        Vector2[][] contours =
        [
            [new(0, 0), new(32, 0), new(32, 32), new(0, 32)],
            [new(10, 10), new(10, 22), new(22, 22), new(22, 10)],
        ];
        foreach (Vector2[] contour in contours)
        {
            IEnumerable<Vector2> points = reverse ? contour.Reverse() : contour;
            Vector2[] ordered = points.ToArray();
            builder.MoveTo(ordered[0]);
            foreach (Vector2 point in ordered.Skip(1))
            {
                builder.LineTo(point);
            }

            builder.Close();
        }

        return builder.Build();
    }

    private static int PixelOffset(DistanceFieldImage2D image, Vector2 point)
    {
        int x = Math.Clamp((int)((point.X - image.PlaneBounds.X) * image.PixelsPerUnit), 0, image.Width - 1);
        int y = Math.Clamp((int)((point.Y - image.PlaneBounds.Y) * image.PixelsPerUnit), 0, image.Height - 1);
        return ((y * image.Width) + x) * 4;
    }

    private static byte MedianAt(DistanceFieldImage2D image, Vector2 point)
    {
        int offset = PixelOffset(image, point);
        ReadOnlySpan<byte> pixel = image.Pixels.Span.Slice(offset, 4);
        return Median(pixel[0], pixel[1], pixel[2]);
    }

    private static byte AlphaAt(DistanceFieldImage2D image, Vector2 point) => image.Pixels.Span[PixelOffset(image, point) + 3];

    private static byte Median(byte a, byte b, byte c) => Math.Max(Math.Min(a, b), Math.Min(Math.Max(a, b), c));
}
