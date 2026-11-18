using Xunit;

namespace Lumyte.Graphics.Text.Parley.Tests;

/// <summary>Host-native ownership of shared shaping algorithms formerly exercised in Chromium.</summary>
public sealed class BrowserShapingCoverageTests
{
    /// <summary>Preserves the exact former browser shaping scenarios on the host native ABI.</summary>
    [Fact]
    public void FormerBrowserShapingScenariosUseNativeAbi()
    {
        using var engine = new ParleyTextEngine2D();
        string family = TestFonts.Add(engine, "DejaVuSans.ttf");
        string japanese = TestFonts.Add(engine, "LumyteSampleJapanese.ttf");
        var options = new ParleyTextOptions2D { FontFamily = family, FontSize = 26 };
        ParleyTextLayout2D ligatures = engine.CreateDetailedLayout("office", options);
        ParleyTextLayout2D unjoined = engine.CreateDetailedLayout("office", options with { FontFeatures = [new("liga", 0)] });
        if (ligatures.Glyphs.Count >= unjoined.Glyphs.Count)
        {
            throw new InvalidOperationException("OpenType ligatures did not affect shaping.");
        }

        ParleyTextLayout2D fallback = engine.CreateDetailedLayout("日本語", options with { FallbackFontFamilies = [japanese] });
        if (fallback.Glyphs.Count != 3 || fallback.Glyphs.Any(glyph => glyph.GlyphId == 0 || glyph.FontFamily != japanese))
        {
            throw new InvalidOperationException("Explicit Japanese fallback was not used.");
        }

        ParleyTextLayout2D bidi = engine.CreateDetailedLayout("ABC אבג", options);
        int[] rtlClusters = bidi.Glyphs.Where(glyph => glyph.Utf16Start >= 4).Select(glyph => glyph.Utf16Start).ToArray();
        if (!rtlClusters.SequenceEqual([6, 5, 4]))
        {
            throw new InvalidOperationException("Bidi source clusters did not follow visual glyph order.");
        }

        TextLayout2D wrapped = engine.CreateLayout("Parley shapes a paragraph and wraps its positioned glyphs.", options with { WrappingLength = 230 });
        if (wrapped.LineCount < 2)
        {
            throw new InvalidOperationException("Paragraph wrapping did not create multiple lines.");
        }
    }
}
