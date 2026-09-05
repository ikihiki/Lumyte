using System.Collections.Concurrent;
using System.Numerics;
using Lumyte.Graphics.TwoD;

namespace Lumyte.Graphics.Text.Tests;

public sealed class FontFaceTests
{
    [Fact]
    public void ShapeReturnsGlyphsAdvancesAndUtf16Clusters()
    {
        using var font = new FontFace(TestFontData.Create());

        ShapedText shaped = font.Shape("A\U0001F600B");

        Assert.Equal(TestFontData.UnitsPerEm, font.UnitsPerEm);
        Assert.Collection(
            shaped.Glyphs.ToArray(),
            glyph => Assert.Equal(new ShapedGlyph(1, 0, 600, 0, 0, 0), glyph),
            glyph => Assert.Equal(new ShapedGlyph(3, 1, 700, 0, 0, 0), glyph),
            glyph => Assert.Equal(new ShapedGlyph(2, 3, 650, 0, 0, 0), glyph));
        Assert.Equal(new Vector2(1_950, 0), shaped.Advance);
    }

    [Fact]
    public void MeasureScalesWithFontSize()
    {
        using var font = new FontFace(TestFontData.Create());

        Vector2 small = font.Measure("AB", 10);
        Vector2 large = font.Measure("AB", 25);

        Assert.Equal(new Vector2(12.5f, 10), small);
        Assert.Equal(small * 2.5f, large);
    }

    [Fact]
    public void TryGetGlyphPathConvertsTrueTypeOutline()
    {
        using var font = new FontFace(TestFontData.Create());

        bool found = font.TryGetGlyphPath(1, out PathGeometry? path);

        Assert.True(found);
        Assert.NotNull(path);
        Assert.False(path.IsEmpty);
        Assert.Equal(new Rect(50, -700, 500, 700), path.Bounds);
    }

    [Fact]
    public void ColorFaceReportsItsPaletteCapabilities()
    {
        using var font = new FontFace(TestFontData.CreateColor());

        Assert.True(font.HasColorGlyphs);
        Assert.Equal(2u, font.ColorPaletteCount);
    }

    [Fact]
    public void ColorPaintFaceExtractsAColrVersionOneProgram()
    {
        using var font = new FontFace(TestFontData.CreateColorV1());

        bool found = font.TryGetColorPaintGlyph(3, 0, out ColorPaintGlyph? glyph);

        Assert.True(font.HasColorGlyphs);
        Assert.True(font.HasColorPaintGlyphs);
        Assert.False(font.HasColorLayerGlyphs);
        Assert.True(found);
        Assert.NotNull(glyph);
        Assert.Collection(
            glyph.Operations,
            operation =>
            {
                ColorPaintPushClipGlyph clip = Assert.IsType<ColorPaintPushClipGlyph>(operation);
                Assert.Equal(4u, clip.GlyphId);
                Assert.Equal(new Rect(50, -700, 600, 700), Assert.IsType<PathGeometry>(clip.Path).Bounds);
            },
            operation =>
            {
                ColorPaintSolid solid = Assert.IsType<ColorPaintSolid>(operation);
                Assert.False(solid.Color.IsForeground);
                Assert.Equal(Color.FromSrgb(0, 0, 1), solid.Color.Color);
            },
            operation => Assert.IsType<ColorPaintPopClip>(operation));
    }

    [Fact]
    public void ColorBitmapFaceReportsItsPngCapability()
    {
        using var font = new FontFace(TestFontData.CreateColorBitmap());

        Assert.True(font.HasColorGlyphs);
        Assert.True(font.HasColorBitmapGlyphs);
        Assert.False(font.HasColorLayerGlyphs);
        Assert.Equal(0u, font.ColorPaletteCount);
    }

    [Fact]
    public void TryGetColorBitmapGlyphDecodesPngAndStrikeMetrics()
    {
        using var font = new FontFace(TestFontData.CreateColorBitmap());

        bool found = font.TryGetColorBitmapGlyph(
            TestFontData.ColorBitmapGlyphId,
            TestFontData.ColorBitmapPixelsPerEm,
            out ColorBitmapGlyph? glyph);

        Assert.True(found);
        Assert.NotNull(glyph);
        Assert.Equal(TestFontData.ColorBitmapWidth, glyph.Width);
        Assert.Equal(TestFontData.ColorBitmapHeight, glyph.Height);
        Assert.Equal(TestFontData.ExpectedColorBitmapPixels(), glyph.Pixels.ToArray());
        Assert.Equal(new Rect(100, -200, 200, 200), glyph.Bounds);
    }

    [Fact]
    public void GetColorPaletteReturnsEachPalette()
    {
        using var font = new FontFace(TestFontData.CreateColor());

        ReadOnlyMemory<Color> blue = font.GetColorPalette(0);
        ReadOnlyMemory<Color> green = font.GetColorPalette(1);

        Assert.Collection(
            blue.ToArray(),
            color => Assert.Equal(Color.FromSrgb(0, 0, 1), color),
            color => Assert.Equal(Color.FromSrgb(192 / 255f, 128 / 255f, 64 / 255f, 128 / 255f), color));
        Assert.Collection(
            green.ToArray(),
            color => Assert.Equal(Color.FromSrgb(0, 1, 0), color),
            color => Assert.Equal(Color.FromSrgb(128 / 255f, 64 / 255f, 192 / 255f), color));
    }

    [Fact]
    public void GetColorPaletteRejectsUnknownPalette()
    {
        using var font = new FontFace(TestFontData.CreateColor());

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => font.GetColorPalette(font.ColorPaletteCount));

        Assert.Equal("paletteIndex", exception.ParamName);
    }

    [Fact]
    public void MonochromeFaceReportsNoColorGlyphs()
    {
        using var font = new FontFace(TestFontData.Create());

        Assert.False(font.HasColorGlyphs);
        Assert.Equal(0u, font.ColorPaletteCount);
    }

    [Fact]
    public void ShapeSupportsConcurrentCalls()
    {
        using var font = new FontFace(TestFontData.Create());
        ShapedGlyph[] expected = font.Shape("A\U0001F600B").Glyphs.ToArray();
        var results = new ConcurrentBag<ShapedGlyph[]>();

        Parallel.For(
            0,
            128,
            _ => results.Add(font.Shape("A\U0001F600B").Glyphs.ToArray()));

        Assert.Equal(128, results.Count);
        Assert.All(results, result => Assert.Equal(expected, result));
    }
}
