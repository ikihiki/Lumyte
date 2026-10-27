using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using SixLabors.Fonts;
using Xunit;

namespace Lumyte.Graphics.Text.Tests;

/// <summary>Exercises real multilingual fonts and the immutable vector-to-canvas contract.</summary>
public sealed class TextLayoutTests
{
    /// <summary>Changing input fonts, ranges and options after creation cannot change recorded outlines.</summary>
    [Fact]
    public void LayoutSnapshotsOptionsRunsPaintAndGeometry()
    {
        Font font = LoadFont();
        var red = PaintGraph2D.Solid(new Color2D(1, 0, 0, 1));
        var blue = PaintGraph2D.Solid(new Color2D(0, 0, 1, 1));
        var run = new TextRun2D { Start = 0, End = 1, Font = font, Paint = red };
        var options = new TextOptions(font) { TextRuns = [run] };
        TextLayout2D layout = SixLaborsTextLayout2D.Create("AB", options, blue);
        Rect2D bounds = layout.Bounds;
        PathSegment2D[][] segments = layout.Geometry.Select(g => g.Path.Segments.ToArray()).ToArray();
        run.Start = 1;
        run.End = 2;
        run.Font = font.Family.CreateFont(80);
        run.Paint = blue;
        options.Origin = new Vector2(200, 300);
        options.WrappingLength = 10;
        options.Font = run.Font;

        Assert.Equal("AB", layout.Text);
        Assert.Equal(bounds, layout.Bounds);
        Assert.Same(red, layout.Geometry[0].Paint);
        Assert.Same(blue, layout.Geometry[^1].Paint);
        for (int i = 0; i < segments.Length; i++)
        {
            Assert.Equal(segments[i], layout.Geometry[i].Path.Segments);
        }

        Assert.Throws<NotSupportedException>(() => ((IList<TextGeometry2D>)layout.Geometry).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<PathSegment2D>)layout.Geometry[0].Path.Segments).Clear());
    }

    /// <summary>Normalizing overlapping ranges and resolving missing fonts only changes private copies.</summary>
    [Fact]
    public void CreatingLayoutDoesNotMutateCallerRuns()
    {
        var first = new TextRun2D { Start = 0, End = 3 };
        var overlapping = new TextRun2D { Start = 1, End = 2 };
        TextRun[] runs = [first, overlapping];
        var options = new TextOptions(LoadFont()) { TextRuns = runs };
        TextLayout2D layout = SixLaborsTextLayout2D.Create("ABC", options);
        Assert.NotEmpty(layout.Geometry);
        Assert.Same(runs, options.TextRuns);
        Assert.Equal(0, first.Start);
        Assert.Equal(3, first.End);
        Assert.Null(first.Font);
        Assert.Equal(1, overlapping.Start);
        Assert.Equal(2, overlapping.End);
        Assert.Null(overlapping.Font);
    }

    /// <summary>Latin ligatures and Arabic joining produce shaped glyphs rather than per-character pictures.</summary>
    [Fact]
    public void OpenTypeLigaturesAndArabicJoiningReachVectorGeometry()
    {
        var options = new TextOptions(LoadFont());
        TextLayout2D ligature = SixLaborsTextLayout2D.Create("ffi", options);
        TextLayout2D arabic = SixLaborsTextLayout2D.Create("\u0644\u0627", options);
        Assert.Single(ligature.Geometry);
        Assert.Single(arabic.Geometry);
        Assert.True(ligature.Bounds.Width > 0);
        Assert.True(arabic.Bounds.Width > 0);
        Assert.All(arabic.Geometry, g => Assert.Contains(g.Path.Segments, s => s.Verb == PathVerb2D.Quadratic));
    }

    /// <summary>A combining sequence occupies one styled grapheme range even though it contains two UTF-16 units.</summary>
    [Fact]
    public void StyledRangesUseGraphemeIndices()
    {
        var red = PaintGraph2D.Solid(new Color2D(1, 0, 0, 1));
        var blue = PaintGraph2D.Solid(new Color2D(0, 0, 1, 1));
        var options = new TextOptions(LoadFont())
        {
            TextRuns = [new TextRun2D { Start = 1, End = 2, Paint = red }],
        };
        TextLayout2D layout = SixLaborsTextLayout2D.Create("e\u0301A", options, blue);
        Assert.True(layout.Geometry.Count >= 2);
        Assert.Same(red, layout.Geometry[^1].Paint);
        Assert.All(layout.Geometry.Take(layout.Geometry.Count - 1), geometry => Assert.Same(blue, geometry.Paint));
    }

    /// <summary>Mixed Latin and Hebrew text preserves Hebrew visual order inside the surrounding LTR paragraph.</summary>
    [Fact]
    public void BidirectionalRunsUseVisualPositions()
    {
        PaintGraph2D[] paints =
        [
            PaintGraph2D.Solid(new Color2D(1, 0, 0, 1)),
            PaintGraph2D.Solid(new Color2D(0, 1, 0, 1)),
            PaintGraph2D.Solid(new Color2D(0, 0, 1, 1)),
            PaintGraph2D.Solid(new Color2D(1, 1, 0, 1)),
        ];
        var options = new TextOptions(LoadFont())
        {
            TextDirection = TextDirection.Auto,
            TextRuns = paints.Select((paint, index) => new TextRun2D { Start = index, End = index + 1, Paint = paint }).ToArray(),
        };
        TextLayout2D layout = SixLaborsTextLayout2D.Create("A\u05d0\u05d1B", options);
        float[] positions = paints.Select(paint => PathPoints(Assert.Single(layout.Geometry, g => ReferenceEquals(g.Paint, paint)).Path).Min(p => p.X)).ToArray();
        Assert.True(positions[0] < positions[2]);
        Assert.True(positions[2] < positions[1]);
        Assert.True(positions[1] < positions[3]);
    }

    /// <summary>Missing script glyphs resolve through an explicitly supplied font and retain their shaped outlines.</summary>
    /// <param name="fileName">The bundled fallback font fixture.</param>
    /// <param name="text">A character missing from the primary font.</param>
    [Theory]
    [InlineData("NotoSansKhmer-Regular.ttf", "\u1781")]
    [InlineData("LumyteSampleJapanese.ttf", "日")]
    public void FallbackFontProducesTheRequestedScript(string fileName, string text)
    {
        var collection = new FontCollection();
        using FileStream stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fonts", fileName));
        FontFamily fallback = collection.Add(stream);
        var options = new TextOptions(LoadFont()) { FallbackFontFamilies = [fallback] };
        TextLayout2D actual = SixLaborsTextLayout2D.Create("A" + text, options);
        TextLayout2D expected = SixLaborsTextLayout2D.Create(text, new TextOptions(fallback.CreateFont(32)));
        Assert.Equal(2, actual.Geometry.Count);
        Path2D expectedPath = Assert.Single(expected.Geometry).Path;
        Path2D actualPath = actual.Geometry[1].Path;
        Assert.Equal(expectedPath.Segments.Select(s => s.Verb), actualPath.Segments.Select(s => s.Verb));
        Vector2 expectedOrigin = expectedPath.Segments[0].P0;
        Vector2 actualOrigin = actualPath.Segments[0].P0;
        Vector2[] expectedPoints = PathPoints(expectedPath).Select(p => p - expectedOrigin).ToArray();
        Vector2[] actualPoints = PathPoints(actualPath).Select(p => p - actualOrigin).ToArray();
        Assert.Equal(expectedPoints.Length, actualPoints.Length);
        for (int i = 0; i < expectedPoints.Length; i++)
        {
            Assert.Equal(expectedPoints[i].X, actualPoints[i].X, 3);
            Assert.Equal(expectedPoints[i].Y, actualPoints[i].Y, 3);
        }
    }

    /// <summary>Whitespace advances without ink; wrapping creates additional measured lines.</summary>
    [Fact]
    public void MeasuresWhitespaceAndWrapsParagraphs()
    {
        var options = new TextOptions(LoadFont());
        TextLayout2D spaces = SixLaborsTextLayout2D.Create("   ", options);
        Assert.Empty(spaces.Geometry);
        Assert.Equal(default, spaces.Bounds);
        Assert.True(spaces.AdvanceBounds.Width > 0);
        TextLayout2D single = SixLaborsTextLayout2D.Create("alpha beta gamma", options);
        options.WrappingLength = 95;
        TextLayout2D wrapped = SixLaborsTextLayout2D.Create("alpha beta gamma", options);
        Assert.Equal(1, single.LineCount);
        Assert.True(wrapped.LineCount > single.LineCount);
        Assert.True(wrapped.AdvanceBounds.Height > single.AdvanceBounds.Height);
        Assert.Equal(2, SixLaborsTextLayout2D.Create("alpha\nbeta", options).LineCount);
    }

    /// <summary>Underline, strikeout and overline become paint-preserving vector outlines.</summary>
    [Fact]
    public void DecorationsUseRunPaintAndContributeToBounds()
    {
        Font font = LoadFont();
        var green = PaintGraph2D.Solid(new Color2D(0, 1, 0, 1));
        TextLayout2D plain = SixLaborsTextLayout2D.Create("A", new TextOptions(font));
        var options = new TextOptions(font)
        {
            TextRuns =
            [
                new TextRun2D
                {
                    Start = 0,
                    End = 1,
                    Paint = green,
                    TextDecorations = TextDecorations.Underline | TextDecorations.Strikeout | TextDecorations.Overline,
                },
            ],
        };
        TextLayout2D decorated = SixLaborsTextLayout2D.Create("A", options);
        Assert.Equal(plain.Geometry.Count + 3, decorated.Geometry.Count);
        Assert.All(decorated.Geometry, geometry => Assert.Same(green, geometry.Paint));
        Assert.Equal(plain.Geometry[0].Path.Segments, decorated.Geometry[0].Path.Segments);
        Assert.All(decorated.Geometry.Skip(1), geometry => Assert.Equal(5, geometry.Path.Segments.Count));
        Assert.Equal(3, decorated.Geometry.Count(g => g.Path.Segments.Count == 5));
        Assert.True(decorated.Bounds.Height > plain.Bounds.Height);
    }

    /// <summary>Text translation composes locally, keeps fixed clip state, and restores the caller's saved scope.</summary>
    [Fact]
    public void DrawingTextRestoresTransformAndClipAcrossRepeatedRecordings()
    {
        TextLayout2D layout = SixLaborsTextLayout2D.Create("Text", new TextOptions(LoadFont()));
        var canvas = new InspectCanvas();
        Assert.Throws<InvalidOperationException>(() => canvas.DrawText(layout));
        canvas.Begin();
        canvas.Translate(new Vector2(11, 23));
        canvas.Scale(new Vector2(2, 3));
        canvas.ClipRect(new Rect2D(1, 2, 90, 40));
        DrawingState2D state = canvas.CurrentState;
        canvas.Save();
        canvas.DrawText(layout, new Vector2(5, 7));
        Assert.Equal(state.Transform, canvas.CurrentState.Transform);
        Assert.Same(state.Clip, canvas.CurrentState.Clip);
        canvas.Restore();
        canvas.End();
        canvas.Begin();
        canvas.DrawText(layout, new Vector2(-1, 2));
        Assert.Equal(Matrix3x2.Identity, canvas.CurrentState.Transform);
        Assert.Empty(canvas.CurrentState.Clip.Elements);
        canvas.End();
    }

    /// <summary>Null and nonfinite input fail without corrupting a canvas recording, including an empty layout.</summary>
    [Fact]
    public void InvalidInputLeavesCanvasUsable()
    {
        var options = new TextOptions(LoadFont());
        Assert.Throws<ArgumentNullException>(() => SixLaborsTextLayout2D.Create(null!, options));
        Assert.Throws<ArgumentNullException>(() => SixLaborsTextLayout2D.Create("text", null!));
        TextLayout2D layout = SixLaborsTextLayout2D.Create(string.Empty, options);
        Assert.Empty(layout.Geometry);
        Assert.Equal(default, layout.Bounds);
        var canvas = new InspectCanvas();
        Assert.Throws<ArgumentNullException>(() => TextCanvas2DExtensions.DrawText(null!, layout));
        canvas.Begin();
        Assert.Throws<ArgumentNullException>(() => canvas.DrawText(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => canvas.DrawText(layout, new Vector2(float.NaN, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => canvas.DrawText(layout, new Vector2(0, float.PositiveInfinity)));
        canvas.DrawText(layout);
        Assert.Equal(Matrix3x2.Identity, canvas.CurrentState.Transform);
        canvas.End();
    }

    private static Font LoadFont()
    {
        var collection = new FontCollection();
        using FileStream stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf"));
        FontFamily family = collection.Add(stream);
        return family.CreateFont(32);
    }

    private static IEnumerable<Vector2> PathPoints(Path2D path)
    {
        foreach (PathSegment2D segment in path.Segments)
        {
            if (segment.Verb != PathVerb2D.Close)
            {
                yield return segment.P0;
            }

            if (segment.Verb is PathVerb2D.Quadratic or PathVerb2D.Cubic)
            {
                yield return segment.P1;
            }

            if (segment.Verb == PathVerb2D.Cubic)
            {
                yield return segment.P2;
            }
        }
    }

    private sealed class InspectCanvas : DrawingCanvas2D
    {
        public DrawingState2D CurrentState => State;

        public DrawList2D End() => CompleteRecording();
    }
}
