using Lumyte.Graphics.TwoD;

namespace Lumyte.Graphics.Text.Tests;

public sealed class ColorPaintResilienceTests
{
    [Fact]
    public void EmptyColorLineRemainsAValidTransparentPaint()
    {
        using var font = new FontFace(TestFontData.CreateColorV1WithEmptyColorLine());

        bool found = font.TryGetColorPaintGlyph(3, 0, out ColorPaintGlyph? glyph);

        Assert.True(found);
        Assert.NotNull(glyph);
        ColorPaintLinearGradient gradient = Assert.IsType<ColorPaintLinearGradient>(glyph.Operations[1]);
        Assert.Empty(gradient.Gradient.Stops);
        Assert.True(TextRenderer.ValidateColorPaintGlyph(glyph, Brush.Solid(Color.White)));
    }

    [Fact]
    public void EmptyGlyphOutlineRemainsAValidTransparentClip()
    {
        using var font = new FontFace(TestFontData.CreateColorV1WithEmptyClipGlyph());

        bool found = font.TryGetColorPaintGlyph(3, 0, out ColorPaintGlyph? glyph);

        Assert.True(found);
        Assert.NotNull(glyph);
        ColorPaintPushClipGlyph clip = Assert.IsType<ColorPaintPushClipGlyph>(glyph.Operations[0]);
        Assert.Equal(0u, clip.GlyphId);
        Assert.Null(clip.Path);
        Assert.True(TextRenderer.ValidateColorPaintGlyph(glyph, Brush.Solid(Color.White)));
    }

    [Fact]
    public void UnreadableClipGlyphFallsBackWithoutPoisoningTheCache()
    {
        using var font = new FontFace(TestFontData.CreateColorV1WithUnreadableClipGlyph());

        bool first = font.TryGetColorPaintGlyph(3, 0, out ColorPaintGlyph? firstGlyph);
        bool second = font.TryGetColorPaintGlyph(3, 0, out ColorPaintGlyph? secondGlyph);
        bool hasMonochromeFallback = font.TryGetGlyphPath(3, out PathGeometry? fallback);

        Assert.False(first);
        Assert.Null(firstGlyph);
        Assert.False(second);
        Assert.Null(secondGlyph);
        Assert.True(hasMonochromeFallback);
        Assert.NotNull(fallback);
    }

    [Fact]
    public void DegenerateRectangleRemainsAValidTransparentClip()
    {
        var glyph = new ColorPaintGlyph(
        [
            new ColorPaintPushClipRectangle(new(10, 20, 0, 30)),
            new ColorPaintSolid(new(Color.White, false)),
            new ColorPaintPopClip(),
        ]);

        bool supported = TextRenderer.ValidateColorPaintGlyph(glyph, Brush.Solid(Color.White));

        Assert.True(supported);
    }

    [Fact]
    public void UnknownExtendModeUsesTheCompatiblePadRule()
    {
        using var font = new FontFace(TestFontData.CreateColorV1WithUnknownExtendMode());

        bool found = font.TryGetColorPaintGlyph(3, 0, out ColorPaintGlyph? glyph);

        Assert.True(found);
        Assert.NotNull(glyph);
        ColorPaintLinearGradient gradient = Assert.IsType<ColorPaintLinearGradient>(glyph.Operations[1]);
        Assert.Equal(ColorPaintExtendMode.Pad, gradient.Gradient.ExtendMode);
    }

    [Fact]
    public void UnknownCompositeModeUsesTheCompatibleClearRule()
    {
        using var font = new FontFace(TestFontData.CreateColorV1WithUnknownCompositeMode());

        bool first = font.TryGetColorPaintGlyph(3, 0, out ColorPaintGlyph? firstGlyph);
        bool second = font.TryGetColorPaintGlyph(3, 0, out ColorPaintGlyph? secondGlyph);

        Assert.True(first);
        Assert.True(second);
        Assert.NotNull(firstGlyph);
        Assert.Same(firstGlyph, secondGlyph);
        Assert.Contains(
            firstGlyph.Operations,
            operation => operation is ColorPaintPushGroup
            {
                CompositeMode: ColorPaintCompositeMode.Clear,
            });
        Assert.Contains(
            firstGlyph.Operations,
            operation => operation is ColorPaintPopGroup
            {
                CompositeMode: ColorPaintCompositeMode.Clear,
            });
        Assert.True(TextRenderer.ValidateColorPaintGlyph(firstGlyph, Brush.Solid(Color.White)));
    }

}
