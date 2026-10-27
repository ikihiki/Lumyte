using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Xunit;

namespace Lumyte.Graphics.Text.Parley.Tests;

/// <summary>Requires real COLR fonts to reach the existing vector paint graph without raster conversion.</summary>
public sealed class ParleyColorPaintTests
{
    /// <summary>Gets every paint record format represented by the bundled compact COLR fixture.</summary>
    public static IEnumerable<object[]> PaintFormats => Enumerable.Range(1, 32).Select(format => new object[] { format });

    /// <summary>Gets all composite operations whose source and backdrop must remain independent.</summary>
    public static IEnumerable<object[]> CompositeModes => Enumerable.Range(0, 28).Select(mode => new object[] { mode });

    /// <summary>All COLR v1 paint formats, including their default variable variants, yield finite vector graphs.</summary>
    /// <param name="format">The paint format mapped to the corresponding private-use character.</param>
    [Theory]
    [MemberData(nameof(PaintFormats))]
    public void EveryPaintFormatReachesTheVectorBackend(int format)
    {
        using var engine = new ParleyTextEngine2D();
        string family = TestFonts.Add(engine, "ColrVectorFormats.ttf");
        ParleyTextLayout2D result = engine.CreateDetailedLayout(char.ConvertFromUtf32(0xe000 + format), Options(family));
        Assert.True(Assert.Single(result.Glyphs).IsColor);
        TextPaintGeometry2D drawing = Assert.IsType<TextPaintGeometry2D>(Assert.Single(result.Layout.Drawings));
        Assert.True(drawing.Bounds.Width > 0);
        Assert.True(drawing.Bounds.Height > 0);
        Assert.True(float.IsFinite(drawing.Bounds.X));
        Assert.True(float.IsFinite(drawing.Bounds.Y));
        Assert.Contains(PaintAssertions.Nodes(drawing.Paint.Root), node => node is ClipPaintNode2D);
        Assert.DoesNotContain(PaintAssertions.Nodes(drawing.Paint.Root), node => node is ImagePaintNode2D or DistanceFieldPaintNode2D);
    }

    /// <summary>The glyph's three gradient types remain analytic gradients rather than image patterns.</summary>
    /// <param name="codePoint">The original Google test font's private-use character.</param>
    /// <param name="gradientType">The expected backend gradient node.</param>
    [Theory]
    [InlineData(0xf0500, typeof(LinearPaintNode2D))]
    [InlineData(0xf0503, typeof(RadialPaintNode2D))]
    [InlineData(0xf0200, typeof(SweepPaintNode2D))]
    public void AnalyticGradientsSurviveColorGlyphImport(int codePoint, Type gradientType)
    {
        using var engine = new ParleyTextEngine2D();
        string family = TestFonts.Add(engine, "ColorFontTest.ttf");
        TextLayout2D layout = engine.CreateLayout(char.ConvertFromUtf32(codePoint), Options(family));
        TextPaintGeometry2D drawing = Assert.IsType<TextPaintGeometry2D>(Assert.Single(layout.Drawings));
        Assert.Contains(PaintAssertions.Nodes(drawing.Paint.Root), node => node.GetType() == gradientType);
        Assert.DoesNotContain(PaintAssertions.Nodes(drawing.Paint.Root), node => node is ImagePaintNode2D);
    }

    /// <summary>OpenType composite identifiers preserve their mode at the 2D graph boundary.</summary>
    /// <param name="mode">The OpenType composite mode whose ordinal matches the backend contract.</param>
    [Theory]
    [MemberData(nameof(CompositeModes))]
    public void CompositeModesPreserveTheirBackendOperation(int mode)
    {
        using var engine = new ParleyTextEngine2D();
        string family = TestFonts.Add(engine, "ColorFontTest.ttf");
        TextLayout2D layout = engine.CreateLayout(char.ConvertFromUtf32(0xf0a00 + mode), Options(family));
        TextPaintGeometry2D drawing = Assert.IsType<TextPaintGeometry2D>(Assert.Single(layout.Drawings));
        Assert.Contains(PaintAssertions.Nodes(drawing.Paint.Root).OfType<CompositePaintNode2D>(), node => node.Mode == (CompositeMode2D)mode);
    }

    /// <summary>Choosing a CPAL palette changes resolved colors and preserves the same glyph placement.</summary>
    [Fact]
    public void PaletteSelectionChangesOnlyColor()
    {
        using var engine = new ParleyTextEngine2D();
        string family = TestFonts.Add(engine, "ColrVectorFormats.ttf");
        ParleyTextOptions2D options = Options(family);
        TextPaintGeometry2D first = Assert.IsType<TextPaintGeometry2D>(Assert.Single(engine.CreateLayout("\ue00a", options).Drawings));
        TextPaintGeometry2D second = Assert.IsType<TextPaintGeometry2D>(Assert.Single(engine.CreateLayout("\ue00a", options with { ColorPaletteIndex = 1 }).Drawings));
        Assert.Equal(first.Bounds, second.Bounds);
        Assert.Contains(PaintAssertions.Nodes(first.Paint.Root).OfType<SolidPaintNode2D>(), node => node.Color == new Color2D(1, 0, 0, 1));
        Assert.Contains(PaintAssertions.Nodes(second.Paint.Root).OfType<SolidPaintNode2D>(), node => node.Color == new Color2D(42 / 255f, 41 / 255f, 74 / 255f, 1));
    }

    /// <summary>Self-referenced color base glyphs keep real contours and multiply foreground alpha exactly once.</summary>
    /// <param name="codePoint">A currentColor glyph with full or partial font paint opacity.</param>
    /// <param name="expectedAlpha">The product of the caller's alpha and the font paint's alpha.</param>
    [Theory]
    [InlineData(0xf0b06, 0.5f)]
    [InlineData(0xf0b07, 0.15f)]
    public void CurrentColorPreservesOutlineAndTransparentAlpha(int codePoint, float expectedAlpha)
    {
        using var engine = new ParleyTextEngine2D();
        string family = TestFonts.Add(engine, "ColorFontTest.ttf");
        var foreground = PaintGraph2D.Solid(new Color2D(1, 0, 0, 0.5f));
        TextPaintGeometry2D drawing = Assert.IsType<TextPaintGeometry2D>(Assert.Single(engine.CreateLayout(char.ConvertFromUtf32(codePoint), Options(family), foreground).Drawings));
        Assert.True(drawing.Bounds.Width > 0);
        Assert.True(drawing.Bounds.Height > 0);
        Assert.Contains(PaintAssertions.Nodes(drawing.Paint.Root), node => ReferenceEquals(node, foreground.Root));
        var center = new Vector2(drawing.Bounds.X + (drawing.Bounds.Width / 2), drawing.Bounds.Y + (drawing.Bounds.Height / 2));
        Vector4 actual = PaintAssertions.Evaluate(drawing.Paint.Root, center);
        Assert.InRange(MathF.Abs(actual.X - expectedAlpha), 0, 0.002f);
        Assert.InRange(MathF.Abs(actual.W - expectedAlpha), 0, 0.002f);
        Assert.Equal(0, actual.Y);
        Assert.Equal(0, actual.Z);
        Assert.Equal(Vector4.Zero, PaintAssertions.Evaluate(drawing.Paint.Root, new Vector2(drawing.Bounds.X - 1, center.Y)));
    }

    /// <summary>An arbitrary foreground gradient remains in paragraph coordinates beneath nested COLR transforms.</summary>
    [Fact]
    public void NestedFontTransformsPreserveExternalForegroundCoordinates()
    {
        using var engine = new ParleyTextEngine2D();
        string family = TestFonts.Add(engine, "ColrVectorForegroundTransforms.ttf");
        var foreground = PaintGraph2D.LinearGradient(Vector2.Zero, new Vector2(64, 0), [new(0, new Color2D(1, 0, 0, 0.25f)), new(1, new Color2D(0, 0, 1, 0.5f))]);
        TextPaintGeometry2D drawing = Assert.IsType<TextPaintGeometry2D>(Assert.Single(engine.CreateLayout("\ue00a", Options(family), foreground).Drawings));
        Assert.Contains(PaintAssertions.Nodes(drawing.Paint.Root), node => ReferenceEquals(node, foreground.Root));
        var center = new Vector2(drawing.Bounds.X + (drawing.Bounds.Width / 2), drawing.Bounds.Y + (drawing.Bounds.Height / 2));
        Vector4 expected = PaintAssertions.Evaluate(foreground.Root, center);
        Vector4 actual = PaintAssertions.Evaluate(drawing.Paint.Root, center);
        Assert.InRange(Vector4.Distance(expected, actual), 0, 0.0001f);
    }

    /// <summary>CurrentColor layout snapshots retain their paint and can record after native engine disposal.</summary>
    [Fact]
    public void MixedColorAndOutlineSnapshotSurvivesDisposal()
    {
        var engine = new ParleyTextEngine2D();
        string primary = TestFonts.Add(engine, "DejaVuSans.ttf");
        string fallback = TestFonts.Add(engine, "ColorFontTest.ttf");
        ParleyTextOptions2D options = Options(primary) with { FallbackFontFamilies = [fallback] };
        TextLayout2D layout = engine.CreateLayout("A" + char.ConvertFromUtf32(0xf0500) + "B", options);
        Assert.Equal(3, layout.Drawings.Count);
        Assert.IsType<TextGeometry2D>(layout.Drawings[0]);
        Assert.IsType<TextPaintGeometry2D>(layout.Drawings[1]);
        Assert.IsType<TextGeometry2D>(layout.Drawings[2]);
        engine.Dispose();
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.DrawText(layout);
        canvas.End();
    }

    /// <summary>Nondefault design coordinates change COLR paint translations through the real variation store.</summary>
    [Fact]
    public void VariableAxisChangesColorPaintAtTheSelectedInstance()
    {
        using var engine = new ParleyTextEngine2D();
        string family = TestFonts.Add(engine, "ParleyVariablePaint.ttf");
        ParleyTextOptions2D options = Options(family);
        TextPaintGeometry2D normal = Assert.IsType<TextPaintGeometry2D>(Assert.Single(engine.CreateLayout("\ue00f", options).Drawings));
        TextPaintGeometry2D varied = Assert.IsType<TextPaintGeometry2D>(Assert.Single(engine.CreateLayout("\ue00f", options with { FontVariations = [new("TEST", 1)] }).Drawings));
        Assert.InRange(MathF.Abs((varied.Bounds.X - normal.Bounds.X) + 6.4f), 0, 0.001f);
        Assert.InRange(MathF.Abs((varied.Bounds.Y - normal.Bounds.Y) + 12.8f), 0, 0.001f);
        Assert.Equal(normal.Bounds.Width, varied.Bounds.Width, 3);
        Assert.Equal(normal.Bounds.Height, varied.Bounds.Height, 3);
    }

    private static ParleyTextOptions2D Options(string family) => new() { FontFamily = family, FontSize = 64 };
}
