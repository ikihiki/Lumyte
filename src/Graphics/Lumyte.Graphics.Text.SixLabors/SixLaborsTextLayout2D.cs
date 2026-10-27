using Lumyte.Graphics.Drawing2D;
using SixLabors.Fonts;

namespace Lumyte.Graphics.Text;

/// <summary>Provides the optional SixLabors layout backend for the shared text snapshot.</summary>
public static class SixLaborsTextLayout2D
{
    /// <summary>Shapes and lays out a complete paragraph with optional styled grapheme ranges.</summary>
    /// <param name="text">Plain Unicode text; no markup is interpreted.</param>
    /// <param name="options">Font, fallback, bidi, wrapping, alignment, OpenType and run options. Do not mutate during this call.</param>
    /// <param name="paint">The default paint; null selects opaque black.</param>
    /// <param name="colorFonts">Optional registered color font owners, kept alive throughout creation.</param>
    /// <param name="colorPixelsPerUnit">A positive compatibility setting; vector color glyphs are independent of raster resolution.</param>
    /// <param name="colorPaletteIndex">The nonnegative CPAL palette index for registered color fonts.</param>
    /// <returns>A CPU snapshot which no longer reads the supplied options or fonts.</returns>
    public static TextLayout2D Create(string text, TextOptions options, PaintGraph2D? paint = null, IReadOnlyList<ColorFont2D>? colorFonts = null, float colorPixelsPerUnit = 2, int colorPaletteIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(options);

        // The upstream engine fills missing run fonts and trims overlapping runs in place.
        // Work on private copies so layout creation cannot modify the caller's configuration.
        var snapshot = new TextOptions(options)
        {
            TextRuns = options.TextRuns?.Select(run => new TextRun2D
            {
                Start = run.Start,
                End = run.End,
                Font = run.Font,
                TextAttributes = run.TextAttributes,
                TextDecorations = run.TextDecorations,
                Paint = (run as TextRun2D)?.Paint,
            }).ToArray() ?? [],
            FallbackFontFamilies = options.FallbackFontFamilies.ToArray(),
            FeatureTags = options.FeatureTags.ToArray(),
        };
        if (!float.IsFinite(colorPixelsPerUnit) || colorPixelsPerUnit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(colorPixelsPerUnit));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(colorPaletteIndex);
        if (colorFonts is { Count: > 0 })
        {
            // Paint import must see shaped base glyph IDs, not the v0 fallback layers.
            snapshot.ColorFontSupport = ColorFontSupport.None;
        }

        var renderer = new OutlineGlyphRenderer2D(paint ?? PaintGraph2D.Solid(new Color2D(0, 0, 0, 1)), colorFonts?.ToArray() ?? [], colorPaletteIndex);
        TextRenderer.RenderTextTo(renderer, text, snapshot);
        FontRectangle advance = TextMeasurer.MeasureAdvance(text, snapshot);
        return TextLayout2D.FromDrawings(text, renderer.Bounds, new Rect2D(advance.X, advance.Y, advance.Width, advance.Height), TextMeasurer.CountLines(text, snapshot), renderer.Drawings);
    }
}
