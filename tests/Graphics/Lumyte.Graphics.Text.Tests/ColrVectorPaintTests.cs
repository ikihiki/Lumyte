using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using Lumyte.Graphics.Drawing2D;
using SixLabors.Fonts;
using Xunit;

namespace Lumyte.Graphics.Text.Tests;

/// <summary>Verifies that COLR paints become reusable vector graphs for the ordinary 2D backend.</summary>
public sealed class ColrVectorPaintTests
{
    /// <summary>Gets every COLR v1 paint format represented by the compact synthetic fixture.</summary>
    public static IEnumerable<object[]> PaintFormats => Enumerable.Range(1, 32).Select(format => new object[] { format });

    /// <summary>Gets variable formats and the static formats with identical default values.</summary>
    public static IEnumerable<object[]> VariableFormats => new[] { 3, 5, 7, 9, 13, 15, 17, 19, 21, 23, 25, 27, 29, 31 }.Select(format => new object[] { format });

    /// <summary>All 32 paint records, including variable records at their default instance, produce bounded vector graphs.</summary>
    /// <param name="format">The original OpenType paint format encoded in the fixture.</param>
    [Theory]
    [MemberData(nameof(PaintFormats))]
    public void EveryPaintFormatProducesFiniteVectorGeometry(int format)
    {
        using ColorFont2D font = LoadFont("ColrVectorFormats.ttf");
        ColorGlyphPaint2D glyph = font.RenderPaintGlyph((ushort)(format + 1), 64);
        Assert.True(glyph.Bounds.Width > 0);
        Assert.True(glyph.Bounds.Height > 0);
        Assert.True(float.IsFinite(glyph.Bounds.X));
        Assert.True(float.IsFinite(glyph.Bounds.Y));
        Assert.Contains(Nodes(glyph.Paint.Root), node => node is ClipPaintNode2D);
        Assert.DoesNotContain(Nodes(glyph.Paint.Root), node => node is ImagePaintNode2D or DistanceFieldPaintNode2D);
    }

    /// <summary>Variable records with no variation index preserve the static paint graph rather than dropping their fields.</summary>
    /// <param name="format">The variable paint record paired with its preceding static format.</param>
    [Theory]
    [MemberData(nameof(VariableFormats))]
    public void DefaultVariablePaintMatchesItsStaticCounterpart(int format)
    {
        using ColorFont2D font = LoadFont("ColrVectorFormats.ttf");
        ColorGlyphPaint2D actual = font.RenderPaintGlyph((ushort)(format + 1), 64);
        ColorGlyphPaint2D expected = font.RenderPaintGlyph((ushort)format, 64);
        Assert.Equal(expected.Bounds, actual.Bounds);
        Assert.Equal(Fingerprint(expected.Paint.Root), Fingerprint(actual.Paint.Root));
    }

    /// <summary>Mapped variation indices with active default regions contribute their real deltas.</summary>
    [Fact]
    public void DefaultVariationStoreAndIndexMapAreApplied()
    {
        using ColorFont2D font = LoadFont("ColrVectorDefaultVariation.ttf");
        ColorGlyphPaint2D expected = font.RenderPaintGlyph(15, 64);
        ColorGlyphPaint2D actual = font.RenderPaintGlyph(16, 64);
        Assert.Equal(expected.Bounds, actual.Bounds);
        Assert.Equal(Fingerprint(expected.Paint.Root), Fingerprint(actual.Paint.Root));
        Assert.Equal(-4.48f, actual.Bounds.X, 3);
    }

    /// <summary>A mapped no-variation sentinel preserves base fields even when a variation store is present.</summary>
    [Fact]
    public void MappedNoVariationSentinelPreservesTheBasePaint()
    {
        using ColorFont2D font = LoadFont("ColrVectorNoVariation.ttf");
        ColorGlyphPaint2D expected = font.RenderPaintGlyph(15, 64);
        ColorGlyphPaint2D actual = font.RenderPaintGlyph(16, 64);
        Assert.Equal(expected.Bounds, actual.Bounds);
        Assert.Equal(Fingerprint(expected.Paint.Root), Fingerprint(actual.Paint.Root));
        Assert.Equal(1.92f, actual.Bounds.X, 3);
    }

    /// <summary>The selected CPAL palette is resolved while preserving glyph geometry.</summary>
    [Fact]
    public void PaletteSelectionChangesColorsWithoutRasterizing()
    {
        using ColorFont2D font = LoadFont("ColrVectorFormats.ttf");
        ColorGlyphPaint2D first = font.RenderPaintGlyph(11, 64, paletteIndex: 0);
        ColorGlyphPaint2D second = font.RenderPaintGlyph(11, 64, paletteIndex: 1);
        Assert.Equal(first.Bounds, second.Bounds);
        Assert.Contains(Nodes(first.Paint.Root).OfType<SolidPaintNode2D>(), node => node.Color == new Color2D(1, 0, 0, 1));
        Assert.Contains(Nodes(second.Paint.Root).OfType<SolidPaintNode2D>(), node => node.Color == new Color2D(42 / 255f, 41 / 255f, 74 / 255f, 1));
        Assert.NotEqual(Fingerprint(first.Paint.Root), Fingerprint(second.Paint.Root));
        Assert.Throws<ArgumentOutOfRangeException>(() => font.RenderPaintGlyph(11, 64, paletteIndex: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => font.RenderPaintGlyph(11, 64, paletteIndex: 3));
    }

    /// <summary>CurrentColor remains an arbitrary paint with its own alpha instead of an opaque native foreground color.</summary>
    [Fact]
    public void CurrentColorPreservesGradientPaintAndTransparency()
    {
        using ColorFont2D font = LoadFont("ColorFontTest.ttf");
        var foreground = PaintGraph2D.LinearGradient(Vector2.Zero, new Vector2(64, 0), [new(0, new Color2D(1, 0, 0, 0.25f)), new(1, new Color2D(0, 0, 1, 0.5f))]);
        ColorGlyphPaint2D glyph = font.RenderPaintGlyph(154, 64, foreground);
        Assert.Contains(Nodes(glyph.Paint.Root), node => ReferenceEquals(node, foreground.Root));
        Assert.DoesNotContain(Nodes(glyph.Paint.Root), node => node is ImagePaintNode2D);
        var translucent = PaintGraph2D.Solid(new Color2D(1, 0, 0, 0.5f));
        ColorGlyphPaint2D alpha = font.RenderPaintGlyph(155, 64, translucent);
        Assert.Contains(Nodes(alpha.Paint.Root), node => ReferenceEquals(node, translucent.Root));
        Assert.Contains(Nodes(alpha.Paint.Root).OfType<OpacityPaintNode2D>(), node => MathF.Abs(node.Opacity - 0.3f) < 0.001f);
    }

    /// <summary>A PaintGlyph referring to its own color base retains the actual monochrome outline and alpha.</summary>
    [Fact]
    public void SelfReferencingColorGlyphRetainsVisibleVectorOutline()
    {
        using ColorFont2D font = LoadFont("ColorFontTest.ttf");
        var foreground = PaintGraph2D.Solid(new Color2D(1, 0, 0, 0.5f));
        ColorGlyphPaint2D glyph = font.RenderPaintGlyph(154, 64, foreground);
        Assert.Equal(51.2f, glyph.Bounds.Width, 3);
        Assert.Equal(44.8f, glyph.Bounds.Height, 3);
        ClipPaintNode2D[] clips = Nodes(glyph.Paint.Root).OfType<ClipPaintNode2D>().ToArray();
        Assert.True(clips.Length >= 2);
        Assert.All(clips, clip => Assert.True(clip.Path.Segments.Count >= 4, "Both ClipList and the self-referenced glyf outline must contain real edges."));
        var center = new Vector2(glyph.Bounds.X + (glyph.Bounds.Width / 2), glyph.Bounds.Y + (glyph.Bounds.Height / 2));
        Assert.Equal(new Vector4(0.5f, 0, 0, 0.5f), EvaluateSolidGraph(glyph.Paint.Root, center));
        Assert.Equal(Vector4.Zero, EvaluateSolidGraph(glyph.Paint.Root, new Vector2(glyph.Bounds.X - 1, center.Y)));

        string text = char.ConvertFromUtf32(0xf0b06);
        var options = new TextOptions(font.Family.CreateFont(30)) { ColorFontSupport = ColorFontSupport.None };
        TextLayout2D ordinary = SixLaborsTextLayout2D.Create(text, options, foreground);
        TextLayout2D layout = SixLaborsTextLayout2D.Create(text, options, foreground, colorFonts: [font]);
        TextPaintGeometry2D drawing = Assert.IsType<TextPaintGeometry2D>(Assert.Single(layout.Drawings));
        Assert.Equal(ordinary.Bounds.X + 3, drawing.Bounds.X, 3);
        Assert.Equal(ordinary.Bounds.Y + 1.5f, drawing.Bounds.Y, 3);
        Assert.Equal(24, drawing.Bounds.Width, 3);
        Assert.Equal(21, drawing.Bounds.Height, 3);
        center = new Vector2(drawing.Bounds.X + (drawing.Bounds.Width / 2), drawing.Bounds.Y + (drawing.Bounds.Height / 2));
        Assert.Equal(new Vector4(0.5f, 0, 0, 0.5f), EvaluateSolidGraph(drawing.Paint.Root, center));
    }

    /// <summary>A TrueType composite using its component metrics retains the component's design-space translation.</summary>
    [Fact]
    public void CompositeOutlineWithUseMyMetricsPreservesItsTranslation()
    {
        using ColorFont2D font = LoadFont("ColrVectorCompositeMetrics.ttf");
        var foreground = PaintGraph2D.Solid(new Color2D(1, 0, 0, 0.5f));
        ColorGlyphPaint2D glyph = font.RenderPaintGlyph(1, 30, foreground);
        Assert.InRange(MathF.Abs(glyph.Bounds.X - 6), 0, 0.005f);
        Assert.InRange(MathF.Abs(glyph.Bounds.Width - 30), 0, 0.005f);
        Assert.InRange(MathF.Abs(glyph.Bounds.Height - 30), 0, 0.005f);
        var center = new Vector2(glyph.Bounds.X + (glyph.Bounds.Width / 2), glyph.Bounds.Y + (glyph.Bounds.Height / 2));
        Assert.Equal(new Vector4(0.5f, 0, 0, 0.5f), EvaluateSolidGraph(glyph.Paint.Root, center));
    }

    /// <summary>Font translations and nonuniform scales transform glyph clips while currentColor stays in paragraph coordinates.</summary>
    [Fact]
    public void NestedFontTransformsDoNotTransformExternalForeground()
    {
        using ColorFont2D font = LoadFont("ColrVectorForegroundTransforms.ttf");
        var foreground = PaintGraph2D.LinearGradient(Vector2.Zero, new Vector2(64, 0), [new(0, new Color2D(1, 0, 0, 1)), new(1, new Color2D(0, 0, 1, 0.5f))]);
        ColorGlyphPaint2D glyph = font.RenderPaintGlyph(11, 64, foreground);
        Matrix3x2 transform = TransformAtNode(glyph.Paint.Root, foreground.Root);
        var point = Vector2.Transform(new Vector2(13, 7), transform);
        Assert.Equal(13, point.X, 4);
        Assert.Equal(7, point.Y, 4);
        Assert.Equal(5.12f, glyph.Bounds.X, 3);
        Assert.Equal(48, glyph.Bounds.Width, 3);
        Assert.Equal(80, glyph.Bounds.Height, 3);
    }

    /// <summary>Real font composite operators retain their separate graph modes for the 2D backend.</summary>
    [Fact]
    public void AllCompositeModesReachTheBackendGraph()
    {
        using ColorFont2D font = LoadFont("ColorFontTest.ttf");
        for (int mode = 0; mode < 28; mode++)
        {
            ColorGlyphPaint2D glyph = font.RenderPaintGlyph((ushort)(120 + mode), 48);
            Assert.Contains(Nodes(glyph.Paint.Root).OfType<CompositePaintNode2D>(), node => (int)node.Mode == mode);
        }
    }

    /// <summary>A font clip box limits both the immutable graph and the reported logical drawing bounds.</summary>
    [Fact]
    public void ClipListLimitsTheReportedBounds()
    {
        using ColorFont2D font = LoadFont("ColrVectorFormats.ttf");
        ColorGlyphPaint2D glyph = font.RenderPaintGlyph(33, 64);
        Assert.Equal(51.2f, glyph.Bounds.Width, 3);
        Assert.Equal(51.2f, glyph.Bounds.Height, 3);
        Assert.True(Nodes(glyph.Paint.Root).OfType<ClipPaintNode2D>().Count() >= 3);
    }

    /// <summary>Graph snapshots are independent of native fonts and can be recorded directly without texture upload.</summary>
    [Fact]
    public void LayoutRecordsColorGlyphsAsVectorPaintAndSurvivesFontDisposal()
    {
        using ColorFont2D font = LoadFont("ColorFontTest.ttf");
        var foreground = PaintGraph2D.Solid(new Color2D(0.2f, 0.3f, 0.7f, 0.4f));
        TextLayout2D layout = SixLaborsTextLayout2D.Create(char.ConvertFromUtf32(0xf0b06), new TextOptions(font.Family.CreateFont(40)), foreground, colorFonts: [font]);
        TextPaintGeometry2D drawing = Assert.IsType<TextPaintGeometry2D>(Assert.Single(layout.Drawings));
        string before = Fingerprint(drawing.Paint.Root);
        font.Dispose();
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.DrawText(layout, new Vector2(10, 20));
        canvas.End();
        Assert.Equal(before, Fingerprint(drawing.Paint.Root));
        Assert.DoesNotContain(Nodes(drawing.Paint.Root), node => node is ImagePaintNode2D or DistanceFieldPaintNode2D);
    }

    /// <summary>Cyclic references and unknown paint formats fail without unbounded graph expansion.</summary>
    [Fact]
    public void CyclesAndInvalidFormatsAreRejected()
    {
        using ColorFont2D font = LoadFont("ColorFontTest.ttf");
        Assert.Throws<InvalidDataException>(() => font.RenderPaintGlyph(178, 32));
        Assert.Throws<InvalidDataException>(() => font.RenderPaintGlyph(179, 32));
        byte[] data = File.ReadAllBytes(FontPath("ColrVectorFormats.ttf"));
        int colr = TableOffset(data, 0x434f4c52);
        int list = checked(colr + (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(colr + 14)));
        int firstPaint = checked(list + (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(list + 6)));
        data[firstPaint] = 255;
        using var stream = new MemoryStream(data);
        using var invalid = ColorFont2D.Load(stream);
        Assert.Throws<InvalidDataException>(() => invalid.RenderPaintGlyph(2, 32));
    }

    /// <summary>PaintColrGlyph references require a v1 base record rather than a legacy-only v0 layer list.</summary>
    [Fact]
    public void NestedColrReferenceCannotTargetALegacyOnlyGlyph()
    {
        byte[] data = File.ReadAllBytes(FontPath("ColorFontTest.ttf"));
        int colr = TableOffset(data, 0x434f4c52);
        int list = checked(colr + (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(colr + 14)));
        int count = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(list)));
        int paint = -1;
        for (int i = 0; i < count; i++)
        {
            int record = list + 4 + (i * 6);
            if (BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(record)) == 166)
            {
                paint = checked(list + (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(record + 2)));
                break;
            }
        }

        Assert.True(paint > 0);
        Assert.Equal(11, data[paint]);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(paint + 1), 168);
        using var stream = new MemoryStream(data);
        using var font = ColorFont2D.Load(stream);
        Assert.True(font.HasColorGlyph(168));
        Assert.Throws<InvalidDataException>(() => font.RenderPaintGlyph(166, 32));
    }

    /// <summary>Describes geometric shape and scale while ignoring paragraph translation.</summary>
    /// <param name="root">The root paint.</param>
    /// <returns>The ordered contour and scale values.</returns>
    internal static string GeometryFingerprint(PaintNode2D root)
    {
        var result = new StringBuilder();
        foreach (PaintNode2D node in Nodes(root))
        {
            if (node is ClipPaintNode2D clip)
            {
                foreach (PathSegment2D segment in clip.Path.Segments)
                {
                    result.Append(segment);
                }
            }
            else if (node is TransformPaintNode2D transform)
            {
                Matrix3x2 matrix = transform.Transform;
                result.Append(matrix.M11).Append(',').Append(matrix.M12).Append(',').Append(matrix.M21).Append(',').Append(matrix.M22).Append(';');
            }
        }

        return result.ToString();
    }

    /// <summary>Describes a graph by its ordered vector and paint values.</summary>
    /// <param name="root">The root paint.</param>
    /// <returns>A stable comparison value within a test run.</returns>
    internal static string Fingerprint(PaintNode2D root)
    {
        var result = new StringBuilder();
        foreach (PaintNode2D node in Nodes(root))
        {
            result.Append(node.GetType().Name).Append(':');
            switch (node)
            {
                case ClipPaintNode2D clip:
                    result.Append(clip.Rule);
                    foreach (PathSegment2D segment in clip.Path.Segments)
                    {
                        result.Append(segment);
                    }

                    break;
                case TransformPaintNode2D transform:
                    result.Append(transform.Transform);
                    break;
                case SolidPaintNode2D solid:
                    result.Append(solid.Color);
                    break;
                case OpacityPaintNode2D opacity:
                    result.Append(opacity.Opacity);
                    break;
                case CompositePaintNode2D composite:
                    result.Append(composite.Mode);
                    break;
                case LinearPaintNode2D linear:
                    result.Append(linear.P0).Append(linear.P1).Append(linear.P2).Append(linear.Extend);
                    AppendStops(result, linear.Stops);
                    break;
                case RadialPaintNode2D radial:
                    result.Append(radial.C0).Append(radial.R0).Append(radial.C1).Append(radial.R1).Append(radial.Extend);
                    AppendStops(result, radial.Stops);
                    break;
                case SweepPaintNode2D sweep:
                    result.Append(sweep.Center).Append(sweep.StartAngle).Append(sweep.EndAngle).Append(sweep.Extend);
                    AppendStops(result, sweep.Stops);
                    break;
                default:
                    result.Append(node);
                    break;
            }

            result.Append(';');
        }

        return result.ToString();
    }

    private static ColorFont2D LoadFont(string name)
    {
        using FileStream stream = File.OpenRead(FontPath(name));
        return ColorFont2D.Load(stream);
    }

    private static string FontPath(string name) => Path.Combine(AppContext.BaseDirectory, "Fonts", name);

    private static void AppendStops(StringBuilder result, IReadOnlyList<ColorStop2D> stops)
    {
        foreach (ColorStop2D stop in stops)
        {
            result.Append(stop.Offset).Append(stop.Color).Append(';');
        }
    }

    private static Vector4 EvaluateSolidGraph(PaintNode2D node, Vector2 point)
    {
        return node switch
        {
            SolidPaintNode2D solid => new Vector4(solid.Color.R * solid.Color.A, solid.Color.G * solid.Color.A, solid.Color.B * solid.Color.A, solid.Color.A),
            OpacityPaintNode2D opacity => EvaluateSolidGraph(opacity.Child, point) * opacity.Opacity,
            TransformPaintNode2D transform => Matrix3x2.Invert(transform.Transform, out Matrix3x2 inverse) ? EvaluateSolidGraph(transform.Child, Vector2.Transform(point, inverse)) : Vector4.Zero,
            ClipPaintNode2D clip => ContainsLinePath(clip.Path, point, clip.Rule) ? EvaluateSolidGraph(clip.Child, point) : Vector4.Zero,
            _ => throw new InvalidOperationException("This reference test evaluates only solid paint, opacity, transforms and line-path clips."),
        };
    }

    private static bool ContainsLinePath(Path2D path, Vector2 point, FillRule2D rule)
    {
        Vector2 current = default;
        Vector2 start = default;
        int winding = 0;
        foreach (PathSegment2D segment in path.Segments)
        {
            switch (segment.Verb)
            {
                case PathVerb2D.Move:
                    start = current = segment.P0;
                    break;
                case PathVerb2D.Line:
                    Edge(segment.P0);
                    break;
                case PathVerb2D.Close:
                    Edge(start);
                    break;
                default:
                    throw new InvalidOperationException("The self-referenced test glyph must consist of line contours.");
            }
        }

        return rule == FillRule2D.NonZero ? winding != 0 : Math.Abs(winding) % 2 != 0;

        void Edge(Vector2 end)
        {
            float side = ((end.X - current.X) * (point.Y - current.Y)) - ((end.Y - current.Y) * (point.X - current.X));
            if (current.Y <= point.Y && end.Y > point.Y && side > 0)
            {
                winding++;
            }
            else if (current.Y > point.Y && end.Y <= point.Y && side < 0)
            {
                winding--;
            }

            current = end;
        }
    }

    private static Matrix3x2 TransformAtNode(PaintNode2D root, PaintNode2D target)
    {
        var pending = new Stack<(PaintNode2D Node, Matrix3x2 Transform)>();
        pending.Push((root, Matrix3x2.Identity));
        while (pending.TryPop(out (PaintNode2D Node, Matrix3x2 Transform) entry))
        {
            if (ReferenceEquals(entry.Node, target))
            {
                return entry.Transform;
            }

            switch (entry.Node)
            {
                case ClipPaintNode2D clip:
                    pending.Push((clip.Child, entry.Transform));
                    break;
                case TransformPaintNode2D transform:
                    pending.Push((transform.Child, transform.Transform * entry.Transform));
                    break;
                case OpacityPaintNode2D opacity:
                    pending.Push((opacity.Child, entry.Transform));
                    break;
                case CompositePaintNode2D composite:
                    pending.Push((composite.Backdrop, entry.Transform));
                    pending.Push((composite.Source, entry.Transform));
                    break;
            }
        }

        throw new InvalidOperationException("The expected foreground node was not preserved in the graph.");
    }

    private static int TableOffset(byte[] font, uint tag)
    {
        int count = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
        for (int i = 0; i < count; i++)
        {
            int entry = 12 + (i * 16);
            if (BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(entry)) == tag)
            {
                return checked((int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(entry + 8)));
            }
        }

        throw new InvalidDataException("The test font does not contain the expected table.");
    }

    private static IEnumerable<PaintNode2D> Nodes(PaintNode2D root)
    {
        var pending = new Stack<PaintNode2D>();
        pending.Push(root);
        while (pending.TryPop(out PaintNode2D? node))
        {
            yield return node;
            switch (node)
            {
                case ClipPaintNode2D clip:
                    pending.Push(clip.Child);
                    break;
                case TransformPaintNode2D transform:
                    pending.Push(transform.Child);
                    break;
                case OpacityPaintNode2D opacity:
                    pending.Push(opacity.Child);
                    break;
                case CompositePaintNode2D composite:
                    pending.Push(composite.Backdrop);
                    pending.Push(composite.Source);
                    break;
            }
        }
    }
}
