using System.Numerics;

using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.TwoD;

namespace Lumyte.Graphics.Text.Tests;

public sealed class TextDrawDataTests
{
    [Fact]
    public void TextOwnsPositionedGlyphsAndPreservesPreparedMetrics()
    {
        var outline = new PathBuilder().MoveTo(Vector2.Zero).LineTo(Vector2.UnitX).LineTo(Vector2.One).Close().Build();
        var glyph = new GlyphUploadData(new("glyph", 1), new(0, 0, 1, 1), new(outline));
        PositionedGlyphData[] glyphs = [new(glyph, Matrix3x2.CreateTranslation(2, 3), new(4, 0), 7)];
        var line = new TextLineMetricsData(8, 6, 2, new(0, 0, 10, 8), new(2, 3, 1, 1));
        var text = new TextDrawData(new("text", 1), new(10, 8), [line], [new(glyphs)]);

        glyphs[0] = new(glyph, Matrix3x2.Identity, Vector2.Zero, 0);

        Assert.Equal(new PositionedGlyphData(glyph, Matrix3x2.CreateTranslation(2, 3), new(4, 0), 7), Assert.Single(Assert.Single(text.GlyphRuns).Glyphs));
        Assert.Same(line, Assert.Single(text.Lines));
    }

    [Fact]
    public void ColorGlyphRetainsDecodedBitmapThroughNestedPaint()
    {
        var image = Image();
        var bitmap = new GlyphPaintNode.Bitmap(image, new(0, 0, 1, 1), new(0, 0, 1, 1));
        var layers = new GlyphPaintNode.Layers([new GlyphPaintNode.Transform(Matrix3x2.Identity, bitmap), bitmap]);
        var glyph = new GlyphUploadData(new("color", 1), new(0, 0, 1, 1), colorPaint: layers);

        var text = new TextDrawData(new("text", 2), Vector2.One, [], [new([new(glyph, Matrix3x2.Identity, Vector2.One, 0)])]);

        Assert.Equal(3, text.Uploads.Count);
        Assert.Contains(image, text.Uploads);
        Assert.Contains(glyph, text.Uploads);
        Assert.Contains(text, text.Uploads);
    }

    [Theory]
    [InlineData(DistanceFieldKind.Coverage, 1)]
    [InlineData(DistanceFieldKind.Sdf, 0)]
    [InlineData(DistanceFieldKind.Msdf, -1)]
    public void DistanceFieldRejectsAnIncompatibleRange(DistanceFieldKind kind, float range)
    {
        Assert.Equal("distanceRange", Assert.Throws<ArgumentOutOfRangeException>(() => new DistanceFieldUploadData(new("distance", 1), Image(), kind, Vector2.One, range)).ParamName);
    }

    [Fact]
    public void DistanceFieldRejectsSrgbEncodedDistances()
    {
        var image = new GpuImageUploadData(new("distance pixels", 1), new(1, 1, GpuFormat.Rgba8Unorm), GpuImageColorEncoding.Srgb, GpuImageAlphaMode.Opaque, []);

        Assert.Equal("image", Assert.Throws<ArgumentException>(() => new DistanceFieldUploadData(new("distance", 1), image, DistanceFieldKind.Sdf, Vector2.One, 4)).ParamName);
    }

    private static GpuImageUploadData Image() => new(new("image", 1), new(1, 1, GpuFormat.Rgba8Unorm), GpuImageColorEncoding.Linear, GpuImageAlphaMode.Straight, [new(0, 0, 4, 4, new byte[] { 10, 20, 30, 40 })]);

    [Fact]
    public void ForegroundGradientOwnsItsStopsAndKeepsTheForegroundDeferred()
    {
        GlyphGradientStop[] stops = [new(0, new Color(1, 0, 0)), GlyphGradientStop.Foreground(1, 0.5f)];
        var paint = GlyphPaintNode.LinearGradient(Vector2.Zero, Vector2.One, stops);

        stops[1] = new(1, Color.White);

        var sum = Assert.IsType<GlyphPaintNode.Composite>(paint);
        var foreground = Assert.IsType<GlyphPaintNode.Composite>(sum.Source);
        Assert.IsType<GlyphPaintNode.Foreground>(foreground.Backdrop);
        var alpha = Assert.IsType<GlyphPaintNode.Gradient>(foreground.Source).Brush.Stops[^1].Color.Alpha;
        Assert.Equal(0.5f, alpha);
    }
}
