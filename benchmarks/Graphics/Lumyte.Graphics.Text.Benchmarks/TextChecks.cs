using System.Text;
using System.Text.Json;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Text.Parley;

namespace Lumyte.Graphics.Text.Benchmarks;

internal static class TextChecks
{
    internal static readonly JsonSerializerOptions Json = new() { IncludeFields = true };

    internal static object Verify(TextCase input)
    {
        using var fixture = new TextFixture(input);
        fixture.VerifyScalarCoverage();
        VerifyDistanceAnchor(fixture.Anchor);
        ParleyTextLayout2D first = fixture.Detailed(0);
        ParleyTextLayout2D second = fixture.Detailed(1);
        VerifyLayout(first);
        VerifyLayout(second);
        Require(Fingerprint(first) == Fingerprint(fixture.Detailed(0)), "Repeat shaping/layout is not deterministic.");
        using var fresh = new TextFixture(input);
        Require(Fingerprint(first) == Fingerprint(fresh.Detailed(0)), "Fresh engine changed glyph layout semantics.");
        if (input.Id == "numeric")
        {
            Require(first.Glyphs.Select(glyph => glyph.GlyphId).ToHashSet().SetEquals(second.Glyphs.Select(glyph => glyph.GlyphId)), "Numeric case changed the glyph set.");
        }

        if (input.Id == "new-glyph")
        {
            Require(second.Glyphs.Any(glyph => !first.Glyphs.Any(prior => prior.GlyphId == glyph.GlyphId && prior.FontId == glyph.FontId)), "New-glyph input did not introduce a glyph ID.");
        }

        if (input.Id == "bidi")
        {
            Require(first.Glyphs.Zip(first.Glyphs.Skip(1)).Any(pair => pair.First.Utf16Start > pair.Second.Utf16Start), "Bidi case lost right-to-left visual cluster order.");
        }

        if (input.Id == "paragraph-width")
        {
            TextLayout2D narrow = fixture.Layout(0);
            TextLayout2D wide = fixture.Layout(1);
            Require(narrow.LineCount > wide.LineCount && wide.LineCount >= 1, "Finite provider width did not change wrapping.");
            Require(narrow.AdvanceBounds.Width <= input.Width(0) + 0.1f && wide.AdvanceBounds.Width <= input.Width(1) + 0.1f, "Paragraph exceeds its actual provider width.");
            Require(narrow.LineCount == first.Layout.LineCount && narrow.Bounds == first.Layout.Bounds && narrow.AdvanceBounds == first.Layout.AdvanceBounds, "Provider is not using the requested finite paragraph width.");
        }

        if (input.Id == "japanese")
        {
            Require(first.Glyphs.All(glyph => glyph.FontFamily == fixture.Fonts[1].Family), "Japanese subset was replaced by a different font.");
        }

        if (input.Id == "colr")
        {
            Require(first.Glyphs.Any(glyph => glyph.IsColor) && first.Layout.Drawings.OfType<TextPaintGeometry2D>().Any(), "COLR asset did not produce real vector color paint.");
        }

        // Public snapshots must remain usable after native engine disposal.
        TextLayout2D snapshot = fixture.Layout(0);
        fixture.Dispose();
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.DrawText(snapshot);
        Require(canvas.End().Count > 0, "Disposed-engine snapshot lost its actual vector drawings.");
        return new
        {
            input.Id,
            fonts = fixture.Fonts,
            states = new[] { Describe(first), Describe(second) },
            visibleChangeExpected = input.Changes,
            introducedGlyphs = second.Glyphs
                .Where(glyph => !first.Glyphs.Any(prior => prior.GlyphId == glyph.GlyphId && prior.FontFamily == glyph.FontFamily))
                .Select(glyph => new { glyph.FontFamily, glyph.GlyphId }).Distinct().ToArray(),
            status = "passed",
        };
    }

    internal static void VerifyLayout(ParleyTextLayout2D detailed)
    {
        Require(detailed.Glyphs.Count > 0 && detailed.Layout.Drawings.Count > 0 && detailed.Layout.LineCount > 0, "Missing real glyphs/outlines.");
        Rect2D ink = detailed.Layout.Bounds;
        Rect2D advance = detailed.Layout.AdvanceBounds;
        Require(Finite(ink) && Finite(advance) && ink.Width > 0 && ink.Height > 0 && advance.Width > 0, "Non-finite/empty layout bounds.");
        foreach (ParleyGlyph2D glyph in detailed.Glyphs)
        {
            Require(glyph.GlyphId != 0 && float.IsFinite(glyph.Advance) && float.IsFinite(glyph.Origin.X) && float.IsFinite(glyph.Origin.Y), "Missing/non-finite glyph metadata.");
            Require(glyph.Utf16Start >= 0 && glyph.Utf16Length > 0 && glyph.Utf16Start + glyph.Utf16Length <= detailed.Layout.Text.Length, "Invalid UTF-16 cluster span.");
            Require(glyph.Utf8Start >= 0 && glyph.Utf8Length > 0 && glyph.Utf8Start + glyph.Utf8Length <= Encoding.UTF8.GetByteCount(detailed.Layout.Text), "Invalid UTF-8 cluster span.");
        }
    }

    internal static object Describe(ParleyTextLayout2D detailed) => new
    {
        detailed.Layout.Text,
        detailed.Layout.Bounds,
        detailed.Layout.AdvanceBounds,
        detailed.Layout.LineCount,
        glyphs = detailed.Glyphs,
        outlines = detailed.Layout.Geometry.Count,
        paintGraphs = detailed.Layout.Drawings.OfType<TextPaintGeometry2D>().Count(),
    };

    internal static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void VerifyDistanceAnchor(TextLayout2D anchor)
    {
        foreach (DistanceFieldMode2D mode in new[] { DistanceFieldMode2D.Msdf, DistanceFieldMode2D.Mtsdf })
        {
            DistanceFieldImage2D first = DistanceFieldGenerator2D.Generate(anchor.Geometry[0].Path, pixelsPerUnit: 1, distanceRange: 8, mode: mode);
            DistanceFieldImage2D second = DistanceFieldGenerator2D.Generate(anchor.Geometry[0].Path, pixelsPerUnit: 1, distanceRange: 8, mode: mode);
            Require(first.Width == second.Width && first.Height == second.Height && first.Pixels.Span.SequenceEqual(second.Pixels.Span), "Distance generator changed identical input pixels.");
            int x = (int)(anchor.Bounds.X + (anchor.Bounds.Width / 2) - first.PlaneBounds.X);
            int y = (int)(anchor.Bounds.Y + (anchor.Bounds.Height / 2) - first.PlaneBounds.Y);
            int offset = ((y * first.Width) + x) * 4;
            ReadOnlySpan<byte> pixels = first.Pixels.Span;
            int median = Math.Max(Math.Min(pixels[offset], pixels[offset + 1]), Math.Min(Math.Max(pixels[offset], pixels[offset + 1]), pixels[offset + 2]));
            Require(median < 128 && (mode != DistanceFieldMode2D.Mtsdf || pixels[offset + 3] < 128), "Encoded O counter is not outside the known outline.");
        }
    }

    private static bool Finite(Rect2D bounds) => float.IsFinite(bounds.X) && float.IsFinite(bounds.Y) && float.IsFinite(bounds.Width) && float.IsFinite(bounds.Height) && bounds.Width >= 0 && bounds.Height >= 0;

    private static string Fingerprint(ParleyTextLayout2D detailed)
    {
        var snapshot = new
        {
            detailed.Layout.Text,
            detailed.Layout.Bounds,
            detailed.Layout.AdvanceBounds,
            detailed.Layout.LineCount,
            glyphs = detailed.Glyphs.Select(glyph => new
            {
                glyph.GlyphId,
                glyph.FontFamily,
                glyph.Utf8Start,
                glyph.Utf8Length,
                glyph.Utf16Start,
                glyph.Utf16Length,
                glyph.Origin,
                glyph.Advance,
                glyph.IsColor,
            }),
            paths = detailed.Layout.Geometry.Select(geometry => geometry.Path.Segments),
        };
        return JsonSerializer.Serialize(snapshot, Json);
    }
}
