using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using Lumyte.Graphics.Drawing2D;
using SixLabors.Fonts;
using Xunit;

namespace Lumyte.Graphics.Text.Tests;

/// <summary>Exercises the published Google COLR v1 test font through the real color-font renderer.</summary>
public sealed class ColorFontRenderingTests
{
    /// <summary>Linear, radial and sweep paints produce spatially varying colors from real font tables.</summary>
    /// <param name="glyphId">The known COLR v1 glyph in the bundled test fixture.</param>
    [Theory]
    [InlineData(90)]
    [InlineData(93)]
    [InlineData(12)]
    public void GradientsProduceSpatiallyVaryingColor(ushort glyphId)
    {
        using ColorFont2D font = LoadFont();
        Assert.True(font.HasColorGlyph(glyphId));
        ColorGlyphBitmap2D bitmap = font.RenderGlyph(glyphId, 64, new Color2D(0, 0, 0, 1));
        AssertBitmap(bitmap);
        var colors = new HashSet<(byte R, byte G, byte B)>();
        ReadOnlySpan<byte> pixels = bitmap.Pixels.Span;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i + 3] > 200)
            {
                colors.Add((pixels[i], pixels[i + 1], pixels[i + 2]));
            }
        }

        Assert.True(colors.Count > 16, "The COLR gradient must retain many different interior colors.");
    }

    /// <summary>The 0xffff palette entry follows the caller's foreground and preserves font paint alpha.</summary>
    [Fact]
    public void ForegroundPaletteEntryUsesRequestedStraightColorAndAlpha()
    {
        using ColorFont2D font = LoadFont();
        ColorGlyphBitmap2D red = font.RenderGlyph(154, 64, new Color2D(1, 0, 0, 1));
        ColorGlyphBitmap2D blue = font.RenderGlyph(154, 64, new Color2D(0, 0, 1, 1));
        ColorGlyphBitmap2D translucent = font.RenderGlyph(155, 64, new Color2D(1, 0, 0, 1));
        Assert.True(HasOpaqueColor(red, red: true));
        Assert.True(HasOpaqueColor(blue, red: false));
        Assert.InRange(MaximumAlpha(translucent), 75, 78);
        Assert.Throws<NotSupportedException>(() => font.RenderGlyph(154, 64, new Color2D(1, 0, 0, 0.5f)));
        ReadOnlySpan<byte> pixels = translucent.Pixels.Span;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i + 3] > 50)
            {
                Assert.True(pixels[i] > 240, "Returned RGBA must be straight alpha, not premultiplied red.");
            }
        }
    }

    /// <summary>Every COLR composite mode renders and visually distinct operators are not flattened to source-over.</summary>
    [Fact]
    public void CompositeModesRenderDistinctResults()
    {
        using ColorFont2D font = LoadFont();
        var hashes = new Dictionary<int, string>();
        for (int mode = 0; mode < 28; mode++)
        {
            ColorGlyphBitmap2D bitmap = font.RenderGlyph((ushort)(120 + mode), 48, new Color2D(0.3f, 0.4f, 0.5f, 1));
            AssertBitmap(bitmap);
            hashes[mode] = Convert.ToHexString(SHA256.HashData(bitmap.Pixels.Span));
        }

        Assert.NotEqual(hashes[1], hashes[2]);
        Assert.NotEqual(hashes[3], hashes[4]);
        Assert.NotEqual(hashes[13], hashes[23]);
        Assert.True(hashes.Values.Distinct().Count() >= 16, "Composite operators must retain their separate blend semantics.");
    }

    /// <summary>Affine transforms and nested clip/reference graphs retain different renderings and remain bounded.</summary>
    [Fact]
    public void TransformsNestedGlyphClipsAndRepeatedReferencesRender()
    {
        using ColorFont2D font = LoadFont();
        ColorGlyphBitmap2D first = font.RenderGlyph(205, 64, new Color2D(0, 0, 0, 1));
        ColorGlyphBitmap2D translated = font.RenderGlyph(206, 64, new Color2D(0, 0, 0, 1));
        ColorGlyphBitmap2D rotated = font.RenderGlyph(208, 64, new Color2D(0, 0, 0, 1));
        foreach (ColorGlyphBitmap2D bitmap in new[] { first, translated, rotated, font.RenderGlyph(109, 64, new Color2D(0, 0, 0, 1)), font.RenderGlyph(180, 64, new Color2D(0, 0, 0, 1)) })
        {
            AssertBitmap(bitmap);
            Assert.True(MaximumAlpha(bitmap) > 0);
        }

        Assert.NotEqual(Convert.ToHexString(SHA256.HashData(first.Pixels.Span)), Convert.ToHexString(SHA256.HashData(translated.Pixels.Span)));
        Assert.NotEqual(Convert.ToHexString(SHA256.HashData(first.Pixels.Span)), Convert.ToHexString(SHA256.HashData(rotated.Pixels.Span)));
    }

    /// <summary>A font containing a cyclic PaintColrGlyph graph cannot recurse indefinitely or allocate an unbounded bitmap.</summary>
    [Fact]
    public void CyclicGlyphReferencesRemainBounded()
    {
        using ColorFont2D font = LoadFont();
        foreach (ushort glyphId in new ushort[] { 178, 179 })
        {
            ColorGlyphBitmap2D bitmap = font.RenderGlyph(glyphId, 32, new Color2D(0, 0, 0, 1));
            AssertBitmap(bitmap);
        }
    }

    /// <summary>A shaped mixed paragraph retains vector glyph placement after the native font is disposed.</summary>
    [Fact]
    public void LayoutResolvesRegisteredColorFallbackAndSnapshotsPixels()
    {
        using ColorFont2D colorFont = LoadFont();
        var collection = new FontCollection();
        using FileStream stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf"));
        FontFamily primary = collection.Add(stream);
        var options = new TextOptions(primary.CreateFont(32)) { FallbackFontFamilies = [colorFont.Family] };
        TextLayout2D layout = SixLaborsTextLayout2D.Create("A" + char.ConvertFromUtf32(0xf0500) + "B", options, colorFonts: [colorFont]);
        Assert.Equal(3, layout.Drawings.Count);
        Assert.IsType<TextGeometry2D>(layout.Drawings[0]);
        TextPaintGeometry2D drawing = Assert.IsType<TextPaintGeometry2D>(layout.Drawings[1]);
        Assert.IsType<TextGeometry2D>(layout.Drawings[2]);
        Assert.Equal(2, layout.Geometry.Count);
        Assert.True(drawing.Bounds.X > 0);
        string snapshot = ColrVectorPaintTests.Fingerprint(drawing.Paint.Root);
        colorFont.Dispose();
        Assert.Equal(snapshot, ColrVectorPaintTests.Fingerprint(drawing.Paint.Root));
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.DrawText(layout);
        canvas.End();
    }

    /// <summary>Registered color fonts preserve one v0 base-glyph graph and its translated logical origin.</summary>
    [Fact]
    public void RegisteredV0GlyphIsRepresentedOnceAndOriginTranslatesVectorBounds()
    {
        using ColorFont2D font = LoadFont();
        Assert.True(font.HasColorGlyph(168));
        var options = new TextOptions(font.Family.CreateFont(32));
        string text = char.ConvertFromUtf32(0xf0e00);
        TextLayout2D first = SixLaborsTextLayout2D.Create(text, options, colorFonts: [font]);
        TextPaintGeometry2D firstGlyph = Assert.IsType<TextPaintGeometry2D>(Assert.Single(first.Drawings));
        options.Origin = new Vector2(100, 200);
        TextLayout2D shifted = SixLaborsTextLayout2D.Create(text, options, colorFonts: [font]);
        TextPaintGeometry2D shiftedGlyph = Assert.IsType<TextPaintGeometry2D>(Assert.Single(shifted.Drawings));
        Assert.Equal(ColrVectorPaintTests.GeometryFingerprint(firstGlyph.Paint.Root), ColrVectorPaintTests.GeometryFingerprint(shiftedGlyph.Paint.Root));
        Assert.Equal(firstGlyph.Bounds.X + 100, shiftedGlyph.Bounds.X, 3);
        Assert.Equal(firstGlyph.Bounds.Y + 200, shiftedGlyph.Bounds.Y, 3);
        Assert.InRange(MathF.Abs(firstGlyph.Bounds.Width - shiftedGlyph.Bounds.Width), 0, 0.0001f);
        Assert.InRange(MathF.Abs(firstGlyph.Bounds.Height - shiftedGlyph.Bounds.Height), 0, 0.0001f);
    }

    /// <summary>Fallback color glyph scripts use the actual color face's metrics rather than the primary text font.</summary>
    /// <param name="attributes">The script sizing mode applied to the shaped glyph.</param>
    [Theory]
    [InlineData(TextAttributes.Subscript)]
    [InlineData(TextAttributes.Superscript)]
    public void ColorFallbackScriptSizingMatchesDirectFace(TextAttributes attributes)
    {
        using FileStream colorStream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fonts", "ColorFontScriptMetrics.ttf"));
        using var colorFont = ColorFont2D.Load(colorStream);
        var collection = new FontCollection();
        using FileStream primaryStream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf"));
        FontFamily primary = collection.Add(primaryStream);
        var fallbackOptions = new TextOptions(primary.CreateFont(32))
        {
            FallbackFontFamilies = [colorFont.Family],
            TextRuns = [new TextRun2D { Start = 0, End = 1, TextAttributes = attributes }],
        };
        var directOptions = new TextOptions(colorFont.Family.CreateFont(32))
        {
            TextRuns = [new TextRun2D { Start = 0, End = 1, TextAttributes = attributes }],
        };
        string text = char.ConvertFromUtf32(0xf0500);
        TextLayout2D fallback = SixLaborsTextLayout2D.Create(text, fallbackOptions, colorFonts: [colorFont]);
        TextLayout2D direct = SixLaborsTextLayout2D.Create(text, directOptions, colorFonts: [colorFont]);
        TextPaintGeometry2D actual = Assert.IsType<TextPaintGeometry2D>(Assert.Single(fallback.Drawings));
        TextPaintGeometry2D expected = Assert.IsType<TextPaintGeometry2D>(Assert.Single(direct.Drawings));
        Assert.InRange(MathF.Abs(expected.Bounds.Width - actual.Bounds.Width), 0, 0.0001f);
        Assert.InRange(MathF.Abs(expected.Bounds.Height - actual.Bounds.Height), 0, 0.0001f);
        Assert.Equal(ColrVectorPaintTests.GeometryFingerprint(expected.Paint.Root), ColrVectorPaintTests.GeometryFingerprint(actual.Paint.Root));
        Assert.True(actual.Bounds.Width > 0);
    }

    /// <summary>A color face with zero script size collapses even when its primary font has nonzero metrics.</summary>
    /// <param name="attributes">The zero-sized script mode in the original fixture.</param>
    [Theory]
    [InlineData(TextAttributes.Subscript)]
    [InlineData(TextAttributes.Superscript)]
    public void ZeroColorFaceScriptMetricsProduceTransparentFallback(TextAttributes attributes)
    {
        using ColorFont2D colorFont = LoadFont();
        var collection = new FontCollection();
        using FileStream stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf"));
        FontFamily primary = collection.Add(stream);
        var options = new TextOptions(primary.CreateFont(32))
        {
            FallbackFontFamilies = [colorFont.Family],
            TextRuns = [new TextRun2D { Start = 0, End = 1, TextAttributes = attributes }],
        };
        TextLayout2D layout = SixLaborsTextLayout2D.Create(char.ConvertFromUtf32(0xf0500), options, colorFonts: [colorFont]);
        TextPaintGeometry2D drawing = Assert.IsType<TextPaintGeometry2D>(Assert.Single(layout.Drawings));
        Assert.Equal(0, drawing.Bounds.Width);
        Assert.Equal(0, drawing.Bounds.Height);
    }

    /// <summary>Invalid fonts and raster settings fail predictably without retaining native font resources.</summary>
    [Fact]
    public void InvalidInputAndDisposedOwnersAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => ColorFont2D.Load(null!));
        using var invalid = new MemoryStream([0, 1, 0, 0]);
        Assert.Throws<InvalidDataException>(() => ColorFont2D.Load(invalid));
        using ColorFont2D font = LoadFont();
        Assert.Throws<ArgumentOutOfRangeException>(() => font.RenderGlyph(90, 0, new Color2D(0, 0, 0, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => font.RenderGlyph(90, 32, new Color2D(0, 0, 0, 1), float.NaN));
        Assert.Throws<ArgumentException>(() => font.RenderGlyph(0, 32, new Color2D(0, 0, 0, 1)));
        font.Dispose();
        Assert.Throws<ObjectDisposedException>(() => font.HasColorGlyph(90));
        Assert.Throws<ObjectDisposedException>(() => font.RenderGlyph(90, 32, new Color2D(0, 0, 0, 1)));
    }

    /// <summary>Malformed COLR metadata cannot address outside the supplied table.</summary>
    /// <param name="truncate">Whether to shorten the table header instead of corrupting its base-glyph offset.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsTruncatedOrOutOfBoundsColrMetadata(bool truncate)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fonts", "ColorFontTest.ttf"));
        int tableCount = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4));
        bool found = false;
        for (int i = 0; i < tableCount; i++)
        {
            int record = 12 + (i * 16);
            if (BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(record)) != 0x434f4c52)
            {
                continue;
            }

            found = true;
            if (truncate)
            {
                BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(record + 12), 20);
            }
            else
            {
                int offset = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(record + 8)));
                BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset + 14), uint.MaxValue);
            }

            break;
        }

        Assert.True(found);
        using var stream = new MemoryStream(bytes);
        Assert.Throws<InvalidDataException>(() => ColorFont2D.Load(stream));
    }

    private static ColorFont2D LoadFont()
    {
        using FileStream stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fonts", "ColorFontTest.ttf"));
        return ColorFont2D.Load(stream);
    }

    private static void AssertBitmap(ColorGlyphBitmap2D bitmap)
    {
        Assert.InRange(bitmap.Width, 1, 1024);
        Assert.InRange(bitmap.Height, 1, 1024);
        Assert.Equal(bitmap.Width * bitmap.Height * 4, bitmap.Pixels.Length);
        Assert.True(float.IsFinite(bitmap.Bounds.X));
        Assert.True(float.IsFinite(bitmap.Bounds.Y));
        Assert.True(float.IsFinite(bitmap.Bounds.Width));
        Assert.True(float.IsFinite(bitmap.Bounds.Height));
    }

    private static int MaximumAlpha(ColorGlyphBitmap2D bitmap)
    {
        int maximum = 0;
        ReadOnlySpan<byte> pixels = bitmap.Pixels.Span;
        for (int i = 3; i < pixels.Length; i += 4)
        {
            maximum = Math.Max(maximum, pixels[i]);
        }

        return maximum;
    }

    private static bool HasOpaqueColor(ColorGlyphBitmap2D bitmap, bool red)
    {
        ReadOnlySpan<byte> pixels = bitmap.Pixels.Span;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i + 3] > 200 && (red ? pixels[i] > 240 && pixels[i + 2] < 20 : pixels[i + 2] > 240 && pixels[i] < 20))
            {
                return true;
            }
        }

        return false;
    }
}
