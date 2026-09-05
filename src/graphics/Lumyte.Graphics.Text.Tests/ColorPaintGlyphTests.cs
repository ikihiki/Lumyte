using System.Numerics;
using Lumyte.Graphics.TwoD;

namespace Lumyte.Graphics.Text.Tests;

public sealed class ColorPaintGlyphTests
{
    [Fact]
    public void FeatureFontMapsEachPaintProgramToText()
    {
        using var font = new FontFace(TestFontData.CreateColorV1Features());

        ShapedText shaped = font.Shape("ABCDE");

        Assert.Equal([1u, 2u, 3u, 4u, 5u], shaped.Glyphs.ToArray().Select(static glyph => glyph.GlyphId));
    }

    [Fact]
    public void LinearGradientRetainsAnchorsStopsAndRepeatExtension()
    {
        using var font = new FontFace(TestFontData.CreateColorV1Features());

        bool found = font.TryGetColorPaintGlyph(1, 0, out ColorPaintGlyph? glyph);

        Assert.True(found);
        Assert.NotNull(glyph);
        Assert.Collection(
            glyph.Operations,
            operation => Assert.Equal(4u, Assert.IsType<ColorPaintPushClipGlyph>(operation).GlyphId),
            operation =>
            {
                ColorPaintLinearGradient gradient = Assert.IsType<ColorPaintLinearGradient>(operation);
                Assert.Equal(new Vector2(10, -20), gradient.Point0);
                Assert.Equal(new Vector2(310, -420), gradient.Point1);
                Assert.Equal(new Vector2(30, -520), gradient.Point2);
                AssertGradient(gradient.Gradient, ColorPaintExtendMode.Repeat);
            },
            operation => Assert.IsType<ColorPaintPopClip>(operation));
    }

    [Fact]
    public void RadialGradientRetainsBothCirclesAndReflectExtension()
    {
        using var font = new FontFace(TestFontData.CreateColorV1Features());

        bool found = font.TryGetColorPaintGlyph(2, 0, out ColorPaintGlyph? glyph);

        Assert.True(found);
        Assert.NotNull(glyph);
        Assert.Collection(
            glyph.Operations,
            operation => Assert.Equal(5u, Assert.IsType<ColorPaintPushClipGlyph>(operation).GlyphId),
            operation =>
            {
                ColorPaintRadialGradient gradient = Assert.IsType<ColorPaintRadialGradient>(operation);
                Assert.Equal(new Vector2(100, -200), gradient.Center0);
                Assert.Equal(25, gradient.Radius0);
                Assert.Equal(new Vector2(350, -450), gradient.Center1);
                Assert.Equal(275, gradient.Radius1);
                AssertGradient(gradient.Gradient, ColorPaintExtendMode.Reflect);
            },
            operation => Assert.IsType<ColorPaintPopClip>(operation));
    }

    [Fact]
    public void SweepGradientConvertsFontCoordinatesAndAngles()
    {
        using var font = new FontFace(TestFontData.CreateColorV1Features());

        bool found = font.TryGetColorPaintGlyph(3, 0, out ColorPaintGlyph? glyph);

        Assert.True(found);
        Assert.NotNull(glyph);
        Assert.Collection(
            glyph.Operations,
            operation => Assert.Equal(4u, Assert.IsType<ColorPaintPushClipGlyph>(operation).GlyphId),
            operation =>
            {
                ColorPaintSweepGradient gradient = Assert.IsType<ColorPaintSweepGradient>(operation);
                Assert.Equal(new Vector2(325, -350), gradient.Center);
                Assert.Equal(0, gradient.StartAngle, 5);
                Assert.Equal(-MathF.PI, gradient.EndAngle, 5);
                AssertGradient(gradient.Gradient, ColorPaintExtendMode.Pad);
            },
            operation => Assert.IsType<ColorPaintPopClip>(operation));
    }

    [Fact]
    public void TransformAndNestedGlyphClipsRemainBalanced()
    {
        using var font = new FontFace(TestFontData.CreateColorV1Features());

        bool found = font.TryGetColorPaintGlyph(4, 0, out ColorPaintGlyph? glyph);

        Assert.True(found);
        Assert.NotNull(glyph);
        Assert.Collection(
            glyph.Operations,
            operation => Assert.Equal(
                new Matrix3x2(1, -0.25f, 0.5f, 1.5f, 25, 40),
                Assert.IsType<ColorPaintPushTransform>(operation).Transform),
            operation => Assert.Equal(4u, Assert.IsType<ColorPaintPushClipGlyph>(operation).GlyphId),
            operation => Assert.Equal(5u, Assert.IsType<ColorPaintPushClipGlyph>(operation).GlyphId),
            operation => Assert.Equal(
                Color.FromSrgb(0, 0, 1),
                Assert.IsType<ColorPaintSolid>(operation).Color.Color),
            operation => Assert.IsType<ColorPaintPopClip>(operation),
            operation => Assert.IsType<ColorPaintPopClip>(operation),
            operation => Assert.IsType<ColorPaintPopTransform>(operation));
    }

    [Fact]
    public void SourceInCompositeRetainsBackdropAndSourceGroups()
    {
        using var font = new FontFace(TestFontData.CreateColorV1Features());

        bool found = font.TryGetColorPaintGlyph(5, 0, out ColorPaintGlyph? glyph);

        Assert.True(found);
        Assert.NotNull(glyph);
        Assert.Contains(
            glyph.Operations,
            operation => operation is ColorPaintPushGroup { CompositeMode: ColorPaintCompositeMode.SourceIn });
        Assert.Contains(
            glyph.Operations,
            operation => operation is ColorPaintPopGroup { CompositeMode: ColorPaintCompositeMode.SourceIn });
        Assert.Equal(2, glyph.Operations.Count(static operation => operation is ColorPaintPushClipGlyph));
        Assert.Equal(2, glyph.Operations.Count(static operation => operation is ColorPaintSolid));
    }

    [Fact]
    public void ColorLayersAndReusedColorGlyphRetainBottomToTopPaintOrder()
    {
        using var font = new FontFace(TestFontData.CreateColorV1TableCoverage());

        bool foundLayers = font.TryGetColorPaintGlyph(1, 0, out ColorPaintGlyph? layers);
        bool foundReuse = font.TryGetColorPaintGlyph(2, 0, out ColorPaintGlyph? reuse);

        Assert.True(foundLayers);
        Assert.True(foundReuse);
        Assert.NotNull(layers);
        Assert.NotNull(reuse);
        AssertLayeredBlueAndForeground(layers);
        AssertLayeredBlueAndForeground(reuse);
    }

    [Fact]
    public void ClipListAddsTheBaseGlyphClipRectangle()
    {
        using var font = new FontFace(TestFontData.CreateColorV1TableCoverage());

        bool found = font.TryGetColorPaintGlyph(3, 0, out ColorPaintGlyph? glyph);

        Assert.True(found);
        Assert.NotNull(glyph);
        Assert.Collection(
            glyph.Operations,
            operation => Assert.Equal(
                new Rect(200, -600, 250, 500),
                Assert.IsType<ColorPaintPushClipRectangle>(operation).Rectangle),
            operation => Assert.Equal(4u, Assert.IsType<ColorPaintPushClipGlyph>(operation).GlyphId),
            operation => Assert.Equal(
                Color.FromSrgb(0, 0, 1),
                Assert.IsType<ColorPaintSolid>(operation).Color.Color),
            operation => Assert.IsType<ColorPaintPopClip>(operation),
            operation => Assert.IsType<ColorPaintPopClip>(operation));
    }

    [Fact]
    public void VariableTranslateUsesTheSelectedDesignSpaceCoordinate()
    {
        byte[] data = TestFontData.CreateColorV1TableCoverage();
        using var defaultFont = new FontFace(data);
        using var maximumWeightFont = new FontFace(
            data,
            variations: [new FontVariation("wght", 1)]);

        bool foundDefault = defaultFont.TryGetColorPaintGlyph(4, 0, out ColorPaintGlyph? defaultGlyph);
        bool foundMaximum = maximumWeightFont.TryGetColorPaintGlyph(4, 0, out ColorPaintGlyph? maximumGlyph);

        Assert.True(foundDefault);
        Assert.True(foundMaximum);
        Assert.NotNull(defaultGlyph);
        Assert.NotNull(maximumGlyph);
        Assert.DoesNotContain(defaultGlyph.Operations, static operation => operation is ColorPaintPushTransform);
        ColorPaintPushTransform transform = Assert.Single(
            maximumGlyph.Operations.OfType<ColorPaintPushTransform>());
        Assert.Equal(Matrix3x2.CreateTranslation(300, 0), transform.Transform);
    }

    private static void AssertLayeredBlueAndForeground(ColorPaintGlyph glyph)
    {
        Assert.Equal(
            [4u, 5u],
            glyph.Operations
                .OfType<ColorPaintPushClipGlyph>()
                .Select(static operation => operation.GlyphId));
        Assert.Collection(
            glyph.Operations.OfType<ColorPaintSolid>(),
            operation =>
            {
                Assert.False(operation.Color.IsForeground);
                Assert.Equal(Color.FromSrgb(0, 0, 1), operation.Color.Color);
            },
            operation =>
            {
                Assert.True(operation.Color.IsForeground);
                Assert.Equal(Color.White, operation.Color.Color);
            });
    }

    private static void AssertGradient(
        ColorPaintGradient gradient,
        ColorPaintExtendMode expectedExtendMode)
    {
        Assert.Equal(expectedExtendMode, gradient.ExtendMode);
        Assert.Collection(
            gradient.Stops,
            stop =>
            {
                Assert.Equal(0, stop.Offset);
                Assert.True(stop.Color.IsForeground);
                Assert.Equal(Color.White, stop.Color.Color);
            },
            stop =>
            {
                Assert.Equal(0.5f, stop.Offset);
                Assert.False(stop.Color.IsForeground);
                Assert.Equal(
                    Color.FromSrgb(192 / 255f, 128 / 255f, 64 / 255f, 128 / 255f),
                    stop.Color.Color);
            },
            stop =>
            {
                Assert.Equal(1, stop.Offset);
                Assert.False(stop.Color.IsForeground);
                Assert.Equal(Color.FromSrgb(0, 0, 1), stop.Color.Color);
            });
    }
}
