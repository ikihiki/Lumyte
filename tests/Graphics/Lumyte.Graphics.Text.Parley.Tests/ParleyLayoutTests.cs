using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Xunit;

namespace Lumyte.Graphics.Text.Parley.Tests;

/// <summary>Exercises Parley's real font shaping, paragraph layout and immutable 2D output.</summary>
public sealed class ParleyLayoutTests
{
    /// <summary>Latin ligatures and Arabic joining are resolved by the real shaping engine.</summary>
    /// <param name="text">The sequence which must shape to one visible glyph.</param>
    [Theory]
    [InlineData("ffi")]
    [InlineData("\u0644\u0627")]
    public void LigaturesBecomeOnePositionedOutline(string text)
    {
        using var engine = new ParleyTextEngine2D();
        string family = TestFonts.Add(engine, "DejaVuSans.ttf");
        ParleyTextLayout2D result = engine.CreateDetailedLayout(text, Options(family));
        ParleyGlyph2D glyph = Assert.Single(result.Glyphs);
        Assert.NotEqual(0u, glyph.GlyphId);
        Assert.Equal(0, glyph.Utf16Start);
        Assert.Equal(text.Length, glyph.Utf16Length);
        TextGeometry2D geometry = Assert.Single(result.Layout.Geometry);
        Assert.Contains(geometry.Path.Segments, segment => segment.Verb == PathVerb2D.Quadratic);
        Assert.True(result.Layout.Bounds.Width > 0);
    }

    /// <summary>OpenType feature overrides reach shaping instead of merely changing drawing metadata.</summary>
    [Fact]
    public void DisablingLigaturesChangesTheShapedGlyphSequence()
    {
        using var engine = new ParleyTextEngine2D();
        string family = TestFonts.Add(engine, "DejaVuSans.ttf");
        ParleyTextOptions2D options = Options(family);
        Assert.Single(engine.CreateDetailedLayout("ffi", options).Glyphs);
        ParleyTextLayout2D disabled = engine.CreateDetailedLayout("ffi", options with { FontFeatures = [new("liga", 0)] });
        Assert.Equal(3, disabled.Glyphs.Count);
        Assert.Equal(3, disabled.Layout.Geometry.Count);
        Assert.Equal(new[] { 0, 1, 2 }, disabled.Glyphs.Select(glyph => glyph.Utf16Start));
    }

    /// <summary>The same nondefault axis instance controls the shaped advance and the extracted glyph contours.</summary>
    [Fact]
    public void VariableAxisChangesBothAdvanceAndOutline()
    {
        using var engine = new ParleyTextEngine2D();
        string family = TestFonts.Add(engine, "ParleyVariableOutline.ttf");
        ParleyTextOptions2D options = Options(family);
        ParleyTextLayout2D thin = engine.CreateDetailedLayout("A", options with { FontVariations = [new("wght", 100)] });
        ParleyTextLayout2D black = engine.CreateDetailedLayout("A", options with { FontVariations = [new("wght", 900)] });
        Assert.Equal(16, Assert.Single(thin.Glyphs).Advance, 3);
        Assert.Equal(28.8f, Assert.Single(black.Glyphs).Advance, 3);
        Assert.Equal(12.8f, thin.Layout.Bounds.Width, 3);
        Assert.Equal(25.6f, black.Layout.Bounds.Width, 3);
        Assert.Equal(thin.Layout.Bounds.Height, black.Layout.Bounds.Height, 3);
    }

    /// <summary>Styled UTF-16 ranges retain their paint and size after conversion to native UTF-8 ranges.</summary>
    [Fact]
    public void StyledRangesApplyToTheRequestedUtf16Character()
    {
        using var engine = new ParleyTextEngine2D();
        string family = TestFonts.Add(engine, "DejaVuSans.ttf");
        var red = PaintGraph2D.Solid(new Color2D(1, 0, 0, 0.5f));
        var blue = PaintGraph2D.Solid(new Color2D(0, 0, 1, 1));
        ParleyTextOptions2D options = Options(family) with { TextRuns = [new() { Start = 1, End = 2, FontSize = 64, Paint = red }] };
        TextLayout2D layout = engine.CreateLayout("A\u00e9B", options, blue);
        Assert.Equal(3, layout.Geometry.Count);
        Assert.Same(blue, layout.Geometry[0].Paint);
        Assert.Same(red, layout.Geometry[1].Paint);
        Assert.Same(blue, layout.Geometry[2].Paint);
        Assert.True(layout.Bounds.Height > engine.CreateLayout("A\u00e9B", Options(family), blue).Bounds.Height);
    }

    /// <summary>Native byte-range conversion cannot accept a style range which splits a UTF-16 surrogate pair.</summary>
    [Fact]
    public void StyledRangesCannotSplitSurrogatePairs()
    {
        using var engine = new ParleyTextEngine2D();
        string family = TestFonts.Add(engine, "DejaVuSans.ttf");
        ParleyTextOptions2D options = Options(family) with { TextRuns = [new() { Start = 2, End = 3 }] };
        Assert.Throws<ArgumentException>(() => engine.CreateLayout("A\U0001f600B", options));
        options = options with { TextRuns = [new() { Start = 1, End = 2 }] };
        Assert.Throws<ArgumentException>(() => engine.CreateLayout("A\U0001f600B", options));
    }

    /// <summary>All line decorations use font metrics and preserve the selected paint as ordinary vector geometry.</summary>
    [Fact]
    public void DecorationsBecomePaintPreservingVectorContours()
    {
        using var engine = new ParleyTextEngine2D();
        string family = TestFonts.Add(engine, "DejaVuSans.ttf");
        var foreground = PaintGraph2D.Solid(new Color2D(0, 0.5f, 0, 0.5f));
        ParleyTextOptions2D plainOptions = Options(family);
        TextLayout2D plain = engine.CreateLayout("A", plainOptions, foreground);
        ParleyTextOptions2D options = plainOptions with { Decorations = ParleyTextDecorations2D.Underline | ParleyTextDecorations2D.Strikethrough | ParleyTextDecorations2D.Overline };
        TextLayout2D decorated = engine.CreateLayout("A", options, foreground);
        Assert.Equal(plain.Geometry.Count + 3, decorated.Geometry.Count);
        Assert.All(decorated.Geometry, geometry => Assert.Same(foreground, geometry.Paint));
        Assert.Equal(plain.Geometry[0].Path.Segments, decorated.Geometry[0].Path.Segments);
        Assert.All(decorated.Geometry.Skip(1), geometry => Assert.Equal(5, geometry.Path.Segments.Count));
        Assert.True(decorated.Bounds.Height > plain.Bounds.Height);
    }

    /// <summary>An overline-only range must split an otherwise identical glyph run without changing its inherited paint.</summary>
    [Fact]
    public void OverlineOnlyRangeDecoratesTheSelectedCharacter()
    {
        using var engine = new ParleyTextEngine2D();
        string family = TestFonts.Add(engine, "DejaVuSans.ttf");
        var foreground = PaintGraph2D.Solid(new Color2D(0.2f, 0.4f, 0.8f, 0.5f));
        ParleyTextOptions2D options = Options(family);
        ParleyTextLayout2D plain = engine.CreateDetailedLayout("ABC", options, foreground);
        TextLayout2D full = engine.CreateLayout("ABC", options with { Decorations = ParleyTextDecorations2D.Overline }, foreground);
        Rect2D reference = RectangleBounds(Assert.Single(full.Geometry.Skip(plain.Layout.Geometry.Count)).Path);
        TextLayout2D selected = engine.CreateLayout("ABC", options with { TextRuns = [new() { Start = 1, End = 2, Decorations = ParleyTextDecorations2D.Overline }] }, foreground);
        TextGeometry2D decoration = Assert.Single(selected.Geometry.Skip(plain.Layout.Geometry.Count));
        ParleyGlyph2D middle = Assert.Single(plain.Glyphs, glyph => glyph.Utf16Start == 1);
        AssertDecorationRectangle(decoration, middle, reference, foreground);
        Assert.All(selected.Geometry, geometry => Assert.Same(foreground, geometry.Paint));
    }

    /// <summary>Disabling an inherited overline on one range leaves separate lines above its two neighbors.</summary>
    [Fact]
    public void DisablingOverlineSplitsTheInheritedDecoration()
    {
        using var engine = new ParleyTextEngine2D();
        string family = TestFonts.Add(engine, "DejaVuSans.ttf");
        var foreground = PaintGraph2D.Solid(new Color2D(0.2f, 0.4f, 0.8f, 0.5f));
        ParleyTextOptions2D options = Options(family);
        ParleyTextLayout2D plain = engine.CreateDetailedLayout("ABC", options, foreground);
        options = options with { Decorations = ParleyTextDecorations2D.Overline };
        TextLayout2D full = engine.CreateLayout("ABC", options, foreground);
        Rect2D reference = RectangleBounds(Assert.Single(full.Geometry.Skip(plain.Layout.Geometry.Count)).Path);
        TextLayout2D selected = engine.CreateLayout("ABC", options with { TextRuns = [new() { Start = 1, End = 2, Decorations = ParleyTextDecorations2D.None }] }, foreground);
        TextGeometry2D[] decorations = selected.Geometry.Skip(plain.Layout.Geometry.Count).OrderBy(geometry => RectangleBounds(geometry.Path).X).ToArray();
        Assert.Equal(2, decorations.Length);
        AssertDecorationRectangle(decorations[0], Assert.Single(plain.Glyphs, glyph => glyph.Utf16Start == 0), reference, foreground);
        AssertDecorationRectangle(decorations[1], Assert.Single(plain.Glyphs, glyph => glyph.Utf16Start == 2), reference, foreground);
        Assert.All(selected.Geometry, geometry => Assert.Same(foreground, geometry.Paint));
    }

    /// <summary>Mixed-direction runs retain logical clusters while their glyph origins follow visual order.</summary>
    [Fact]
    public void MixedHebrewAndLatinUseVisualPositions()
    {
        using var engine = new ParleyTextEngine2D();
        string family = TestFonts.Add(engine, "DejaVuSans.ttf");
        ParleyTextLayout2D result = engine.CreateDetailedLayout("A\u05d0\u05d1B", Options(family));
        Assert.Equal(4, result.Glyphs.Count);
        float[] origins = Enumerable.Range(0, 4).Select(index => Assert.Single(result.Glyphs, glyph => glyph.Utf16Start == index).Origin.X).ToArray();
        Assert.True(origins[0] < origins[2]);
        Assert.True(origins[2] < origins[1]);
        Assert.True(origins[1] < origins[3]);
    }

    /// <summary>Cluster offsets distinguish UTF-8 bytes, UTF-16 units and a combined grapheme.</summary>
    [Fact]
    public void ClustersMapUtf8RangesBackToUtf16WithoutSplittingSurrogates()
    {
        using var engine = new ParleyTextEngine2D();
        string family = TestFonts.Add(engine, "DejaVuSans.ttf");
        const string Text = "e\u0301\U0001f600A";
        ParleyTextLayout2D result = engine.CreateDetailedLayout(Text, Options(family));
        ParleyGlyph2D accented = Assert.Single(result.Glyphs, glyph => glyph.Utf16Start == 0);
        Assert.Equal(2, accented.Utf16Length);
        Assert.Equal(0, accented.Utf8Start);
        Assert.Equal(3, accented.Utf8Length);
        ParleyGlyph2D supplementary = Assert.Single(result.Glyphs, glyph => glyph.Utf16Start == 2);
        Assert.Equal(2, supplementary.Utf16Length);
        Assert.Equal(3, supplementary.Utf8Start);
        Assert.Equal(4, supplementary.Utf8Length);
        ParleyGlyph2D final = Assert.Single(result.Glyphs, glyph => glyph.Utf16Start == 4);
        Assert.Equal(1, final.Utf16Length);
        Assert.Equal(7, final.Utf8Start);
        Assert.All(result.Glyphs, glyph => Assert.False(char.IsLowSurrogate(Text[glyph.Utf16Start])));
    }

    /// <summary>An explicitly registered fallback supplies the script's glyph IDs and real contours.</summary>
    /// <param name="fileName">The bundled font which supplies the missing script.</param>
    /// <param name="text">The script sample missing from the primary font.</param>
    [Theory]
    [InlineData("LumyteSampleJapanese.ttf", "日本語")]
    [InlineData("NotoSansKhmer-Regular.ttf", "\u1781\u17d2\u1798\u17c2\u179a")]
    public void ExplicitFallbackShapesMissingScripts(string fileName, string text)
    {
        using var engine = new ParleyTextEngine2D();
        string primary = TestFonts.Add(engine, "DejaVuSans.ttf");
        string fallback = TestFonts.Add(engine, fileName);
        ParleyTextOptions2D options = Options(primary) with { FallbackFontFamilies = [fallback] };
        ParleyTextLayout2D result = engine.CreateDetailedLayout("A" + text, options);
        Assert.Equal(primary, Assert.Single(result.Glyphs, glyph => glyph.Utf16Start == 0).FontFamily);
        ParleyGlyph2D[] script = result.Glyphs.Where(glyph => glyph.Utf16Start > 0).ToArray();
        Assert.NotEmpty(script);
        Assert.All(script, glyph =>
        {
            Assert.NotEqual(0u, glyph.GlyphId);
            Assert.Equal(fallback, glyph.FontFamily);
        });
        Assert.True(result.Layout.Geometry.Count > 1);
        Assert.True(result.Layout.Bounds.Width > 0);
    }

    /// <summary>Whitespace advances without ink, explicit newlines and wrapping create measured paragraph lines.</summary>
    [Fact]
    public void WhitespaceAndWrappingPreserveParagraphMetrics()
    {
        using var engine = new ParleyTextEngine2D();
        string family = TestFonts.Add(engine, "DejaVuSans.ttf");
        ParleyTextOptions2D options = Options(family);
        TextLayout2D spaces = engine.CreateLayout("   ", options);
        Assert.Empty(spaces.Drawings);
        Assert.True(spaces.AdvanceBounds.Width > 0);
        TextLayout2D single = engine.CreateLayout("alpha beta gamma", options);
        TextLayout2D wrapped = engine.CreateLayout("alpha beta gamma", options with { WrappingLength = 95 });
        Assert.Equal(1, single.LineCount);
        Assert.True(wrapped.LineCount > 1);
        Assert.True(wrapped.AdvanceBounds.Height > single.AdvanceBounds.Height);
        Assert.Equal(2, engine.CreateLayout("alpha\nbeta", options).LineCount);
    }

    /// <summary>A tab keeps its source cluster and layout advance without drawing the font's missing-glyph outline.</summary>
    [Fact]
    public void TabAdvancesWithoutDrawingAMissingGlyphBox()
    {
        using var engine = new ParleyTextEngine2D();
        string family = TestFonts.Add(engine, "DejaVuSans.ttf");
        ParleyTextOptions2D options = Options(family);
        ParleyTextLayout2D result = engine.CreateDetailedLayout("ab\tcd", options);
        Assert.Equal(5, result.Glyphs.Count);
        ParleyGlyph2D tab = Assert.Single(result.Glyphs, glyph => glyph.Utf16Start == 2);
        Assert.Equal(1, tab.Utf16Length);
        Assert.Equal(2, tab.Utf8Start);
        Assert.Equal(1, tab.Utf8Length);
        Assert.True(tab.Advance > 0);
        ParleyGlyph2D before = Assert.Single(result.Glyphs, glyph => glyph.Utf16Start == 1);
        ParleyGlyph2D after = Assert.Single(result.Glyphs, glyph => glyph.Utf16Start == 3);
        Assert.Equal(tab.Origin.X + tab.Advance, after.Origin.X, 3);
        Assert.True(after.Origin.X > before.Origin.X + before.Advance);
        Assert.Equal(4, result.Layout.Geometry.Count);
        TextLayout2D visible = engine.CreateLayout("abcd", options);
        for (int index = 0; index < visible.Geometry.Count; index++)
        {
            Path2D expected = visible.Geometry[index].Path;
            Path2D actual = result.Layout.Geometry[index].Path;
            Assert.Equal(expected.Segments.Select(segment => segment.Verb), actual.Segments.Select(segment => segment.Verb));
            Vector2 expectedOrigin = expected.Segments[0].P0;
            Vector2 actualOrigin = actual.Segments[0].P0;
            for (int segment = 0; segment < expected.Segments.Count; segment++)
            {
                PathSegment2D expectedSegment = expected.Segments[segment];
                PathSegment2D actualSegment = actual.Segments[segment];
                if (expectedSegment.Verb != PathVerb2D.Close)
                {
                    Assert.InRange(Vector2.Distance(expectedSegment.P0 - expectedOrigin, actualSegment.P0 - actualOrigin), 0, 0.0001f);
                }

                if (expectedSegment.Verb is PathVerb2D.Quadratic or PathVerb2D.Cubic)
                {
                    Assert.InRange(Vector2.Distance(expectedSegment.P1 - expectedOrigin, actualSegment.P1 - actualOrigin), 0, 0.0001f);
                }

                if (expectedSegment.Verb == PathVerb2D.Cubic)
                {
                    Assert.InRange(Vector2.Distance(expectedSegment.P2 - expectedOrigin, actualSegment.P2 - actualOrigin), 0, 0.0001f);
                }
            }
        }
    }

    /// <summary>Paragraph alignment changes glyph placement within the requested finite width.</summary>
    [Fact]
    public void AlignmentUsesTheParagraphWidth()
    {
        using var engine = new ParleyTextEngine2D();
        string family = TestFonts.Add(engine, "DejaVuSans.ttf");
        ParleyTextOptions2D options = Options(family) with { WrappingLength = 200 };
        TextLayout2D start = engine.CreateLayout("AB", options);
        TextLayout2D center = engine.CreateLayout("AB", options with { Alignment = ParleyTextAlignment2D.Center });
        TextLayout2D end = engine.CreateLayout("AB", options with { Alignment = ParleyTextAlignment2D.End });
        Assert.True(start.Bounds.X < center.Bounds.X);
        Assert.True(center.Bounds.X < end.Bounds.X);
        Assert.Equal(start.Bounds.Width, center.Bounds.Width, 3);
        Assert.Equal(start.Bounds.Width, end.Bounds.Width, 3);
    }

    /// <summary>Font source streams and native engine lifetime do not affect an already created layout.</summary>
    [Fact]
    public void LayoutSnapshotSurvivesFontAndEngineDisposal()
    {
        var engine = new ParleyTextEngine2D();
        string family = TestFonts.Add(engine, "DejaVuSans.ttf");
        var foreground = PaintGraph2D.Solid(new Color2D(0.2f, 0.4f, 0.8f, 0.5f));
        ParleyTextLayout2D result = engine.CreateDetailedLayout("Snapshot", Options(family), foreground);
        PathSegment2D[][] segments = result.Layout.Geometry.Select(geometry => geometry.Path.Segments.ToArray()).ToArray();
        engine.Dispose();
        engine.Dispose();
        Assert.Throws<ObjectDisposedException>(() => engine.CreateLayout("A", Options(family)));
        for (int index = 0; index < segments.Length; index++)
        {
            Assert.Equal(segments[index], result.Layout.Geometry[index].Path.Segments);
            Assert.Same(foreground, result.Layout.Geometry[index].Paint);
        }

        Assert.Throws<NotSupportedException>(() => ((IList<ParleyGlyph2D>)result.Glyphs).Clear());
        var canvas = new InspectCanvas();
        canvas.Begin();
        canvas.DrawText(result.Layout);
        canvas.End();
    }

    /// <summary>The native font entry point reports malformed data without poisoning subsequent valid layouts.</summary>
    [Fact]
    public void MalformedFontDoesNotPoisonTheEngine()
    {
        using var engine = new ParleyTextEngine2D();
        using var invalid = new MemoryStream([0, 1, 0, 0]);
        Assert.Throws<InvalidDataException>(() => engine.AddFont(invalid));
        string family = TestFonts.Add(engine, "DejaVuSans.ttf");
        Assert.NotEmpty(engine.CreateLayout("Valid", Options(family)).Geometry);
    }

    /// <summary>Invalid public options are rejected before creating a native layout.</summary>
    [Fact]
    public void InvalidInputIsRejected()
    {
        using var engine = new ParleyTextEngine2D();
        string family = TestFonts.Add(engine, "DejaVuSans.ttf");
        ParleyTextOptions2D options = Options(family);
        Assert.Throws<ArgumentNullException>(() => engine.AddFont((Stream)null!));
        Assert.Throws<ArgumentNullException>(() => engine.CreateLayout(null!, options));
        Assert.NotEmpty(engine.CreateLayout("A").Geometry);
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.CreateLayout("A", options with { FontSize = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.CreateLayout("A", options with { FontSize = float.NaN }));
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.CreateLayout("A", options with { WrappingLength = float.NaN }));
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.CreateLayout("A", options with { ColorPaletteIndex = -1 }));
    }

    /// <summary>Drawing a Parley snapshot obeys the same local translation and clip lifetime as other 2D content.</summary>
    [Fact]
    public void CanvasRecordingRestoresTransformAndClip()
    {
        using var engine = new ParleyTextEngine2D();
        string family = TestFonts.Add(engine, "DejaVuSans.ttf");
        TextLayout2D layout = engine.CreateLayout("Text", Options(family));
        var canvas = new InspectCanvas();
        canvas.Begin();
        canvas.Translate(new Vector2(10, 20));
        canvas.Scale(new Vector2(2, 3));
        canvas.ClipRect(new Rect2D(0, 0, 100, 80));
        DrawingState2D state = canvas.CurrentState;
        canvas.DrawText(layout, new Vector2(3, 7));
        Assert.Equal(state.Transform, canvas.CurrentState.Transform);
        Assert.Same(state.Clip, canvas.CurrentState.Clip);
        canvas.End();
    }

    /// <summary>Unhinted Parley outlines preserve counter orientation through the existing MTSDF generator.</summary>
    [Fact]
    public void MtsdfPreservesTheCounterOfTheLetterO()
    {
        using var engine = new ParleyTextEngine2D();
        string family = TestFonts.Add(engine, "DejaVuSans.ttf");
        TextLayout2D layout = engine.CreateLayout("O", Options(family));
        Path2D path = Assert.Single(layout.Geometry).Path;
        Assert.True(path.Segments.Count(segment => segment.Verb == PathVerb2D.Close) >= 2);
        DistanceFieldImage2D image = DistanceFieldGenerator2D.Generate(path, pixelsPerUnit: 2, distanceRange: 8, maximumDimension: 128);
        var center = new Vector2(layout.Bounds.X + (layout.Bounds.Width / 2), layout.Bounds.Y + (layout.Bounds.Height / 2));
        var stem = new Vector2(layout.Bounds.X + (layout.Bounds.Width * 0.08f), center.Y);
        Assert.True(TestFonts.AlphaAt(image, center) < 128, "The counter must remain outside the filled glyph.");
        Assert.True(TestFonts.AlphaAt(image, stem) > 128, "The left stem must remain inside the filled glyph.");
    }

    private static ParleyTextOptions2D Options(string family) => new() { FontFamily = family, FontSize = 32 };

    private static void AssertDecorationRectangle(TextGeometry2D decoration, ParleyGlyph2D glyph, Rect2D reference, PaintGraph2D paint)
    {
        Assert.Equal(new[] { PathVerb2D.Move, PathVerb2D.Line, PathVerb2D.Line, PathVerb2D.Line, PathVerb2D.Close }, decoration.Path.Segments.Select(segment => segment.Verb));
        Rect2D bounds = RectangleBounds(decoration.Path);
        Assert.Equal(glyph.Origin.X, bounds.X, 3);
        Assert.Equal(glyph.Advance, bounds.Width, 3);
        Assert.Equal(reference.Y, bounds.Y, 3);
        Assert.Equal(reference.Height, bounds.Height, 3);
        Assert.True(bounds.Height > 0);
        Assert.Same(paint, decoration.Paint);
    }

    private static Rect2D RectangleBounds(Path2D path)
    {
        Vector2[] corners = path.Segments.Where(segment => segment.Verb != PathVerb2D.Close).Select(segment => segment.P0).ToArray();
        float left = corners.Min(point => point.X);
        float top = corners.Min(point => point.Y);
        return new Rect2D(left, top, corners.Max(point => point.X) - left, corners.Max(point => point.Y) - top);
    }

    private sealed class InspectCanvas : DrawingCanvas2D
    {
        public DrawingState2D CurrentState => State;

        public DrawList2D End() => CompleteRecording();
    }
}
