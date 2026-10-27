using System.Buffers.Binary;
using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Xunit;

namespace Lumyte.Graphics.Text.Parley.Tests;

/// <summary>Preserves format, variation and malformed-font regressions through the production text engine.</summary>
public sealed class ParleyColorConformanceTests
{
    /// <summary>Gets variable paint formats paired with static formats carrying identical default values.</summary>
    public static IEnumerable<object[]> VariableFormats => new[] { 3, 5, 7, 9, 13, 15, 17, 19, 21, 23, 25, 27, 29, 31 }.Select(format => new object[] { format });

    /// <summary>Default variable records and no-variation sentinels preserve their static counterparts.</summary>
    /// <param name="format">The variable format following its static counterpart.</param>
    [Theory]
    [MemberData(nameof(VariableFormats))]
    public void DefaultVariablePaintMatchesItsStaticCounterpart(int format)
    {
        using var engine = new ParleyTextEngine2D();
        string family = TestFonts.Add(engine, "ColrVectorFormats.ttf");
        TextPaintGeometry2D expected = Paint(engine, family, 0xe000 + format - 1);
        TextPaintGeometry2D actual = Paint(engine, family, 0xe000 + format);
        Assert.Equal(expected.Bounds, actual.Bounds);
        Assert.Equal(PaintAssertions.Fingerprint(expected.Paint.Root), PaintAssertions.Fingerprint(actual.Paint.Root));
    }

    /// <summary>Default variation-store deltas and their mapped no-variation sentinel retain the specified translations.</summary>
    /// <param name="name">The font with an active default region or mapped no-variation sentinel.</param>
    /// <param name="expectedX">The expected logical X offset from the shaped glyph origin.</param>
    [Theory]
    [InlineData("ColrVectorDefaultVariation.ttf", -4.48f)]
    [InlineData("ColrVectorNoVariation.ttf", 1.92f)]
    public void DefaultVariationIndexMappingsPreserveTheExpectedTranslation(string name, float expectedX)
    {
        using var engine = new ParleyTextEngine2D();
        string family = TestFonts.Add(engine, name);
        ParleyTextLayout2D result = engine.CreateDetailedLayout("\ue00f", Options(family));
        TextPaintGeometry2D actual = Assert.IsType<TextPaintGeometry2D>(Assert.Single(result.Layout.Drawings));
        Assert.Equal(expectedX, actual.Bounds.X - Assert.Single(result.Glyphs).Origin.X, 3);
        TextPaintGeometry2D expected = Paint(engine, family, 0xe00e);
        Assert.Equal(expected.Bounds, actual.Bounds);
        Assert.Equal(PaintAssertions.Fingerprint(expected.Paint.Root), PaintAssertions.Fingerprint(actual.Paint.Root));
    }

    /// <summary>Composite outlines retain component translations even when USE_MY_METRICS supplies their bearings.</summary>
    [Fact]
    public void CompositeOutlinePreservesComponentTranslation()
    {
        using var engine = new ParleyTextEngine2D();
        string family = TestFonts.Add(engine, "ColrVectorCompositeMetrics.ttf");
        ParleyTextLayout2D result = engine.CreateDetailedLayout(char.ConvertFromUtf32(0xf0b06), Options(family) with { FontSize = 30 });
        TextPaintGeometry2D drawing = Assert.IsType<TextPaintGeometry2D>(Assert.Single(result.Layout.Drawings));
        Assert.Equal(6, drawing.Bounds.X - Assert.Single(result.Glyphs).Origin.X, 3);
        Assert.Equal(30, drawing.Bounds.Width, 3);
        Assert.Equal(30, drawing.Bounds.Height, 3);
    }

    /// <summary>The font's ClipList constrains both vector coverage and reported bounds.</summary>
    [Fact]
    public void ClipListBoundsSurviveImport()
    {
        using var engine = new ParleyTextEngine2D();
        string family = TestFonts.Add(engine, "ColrVectorFormats.ttf");
        TextPaintGeometry2D drawing = Paint(engine, family, 0xe020);
        Assert.Equal(51.2f, drawing.Bounds.Width, 3);
        Assert.Equal(51.2f, drawing.Bounds.Height, 3);
        Assert.True(PaintAssertions.Nodes(drawing.Paint.Root).OfType<ClipPaintNode2D>().Count() >= 3);
    }

    /// <summary>Partial font opacity multiplies an arbitrary translucent foreground gradient exactly once.</summary>
    [Fact]
    public void TranslucentGradientCurrentColorRetainsFontOpacity()
    {
        using var engine = new ParleyTextEngine2D();
        string family = TestFonts.Add(engine, "ColorFontTest.ttf");
        var foreground = PaintGraph2D.LinearGradient(Vector2.Zero, new Vector2(64, 0), [new(0, new Color2D(1, 0, 0, 0.25f)), new(1, new Color2D(0, 0, 1, 0.5f))]);
        TextPaintGeometry2D drawing = Assert.IsType<TextPaintGeometry2D>(Assert.Single(engine.CreateLayout(char.ConvertFromUtf32(0xf0b07), Options(family), foreground).Drawings));
        var center = new Vector2(drawing.Bounds.X + (drawing.Bounds.Width / 2), drawing.Bounds.Y + (drawing.Bounds.Height / 2));
        Vector4 expected = PaintAssertions.Evaluate(foreground.Root, center) * 0.3f;
        Assert.InRange(Vector4.Distance(expected, PaintAssertions.Evaluate(drawing.Paint.Root, center)), 0, 0.002f);
        Assert.Equal(Vector4.Zero, PaintAssertions.Evaluate(drawing.Paint.Root, new Vector2(drawing.Bounds.X - 1, center.Y)));
    }

    /// <summary>Unsupported paint tags fail through the C ABI instead of producing a monochrome fallback.</summary>
    [Fact]
    public void InvalidPaintFormatIsRejected()
    {
        byte[] data = FontData("ColrVectorFormats.ttf");
        int colr = TableOffset(data, 0x434f4c52);
        int list = checked(colr + (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(colr + 14)));
        int firstPaint = checked(list + (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(list + 6)));
        data[firstPaint] = 255;
        using var engine = new ParleyTextEngine2D();
        string family = engine.AddFont(data);
        Assert.Throws<InvalidDataException>(() => engine.CreateLayout("\ue001", Options(family)));
    }

    /// <summary>Cyclic PaintColrGlyph references are bounded and reported as invalid input.</summary>
    [Fact]
    public void CyclicPaintReferencesAreRejected()
    {
        byte[] data = FontData("ColorFontTest.ttf");
        using var engine = new ParleyTextEngine2D();
        string family = engine.AddFont(data);
        foreach (ushort glyphId in new ushort[] { 178, 179 })
        {
            string text = char.ConvertFromUtf32(CodePointForGlyph(data, glyphId));
            Assert.Throws<InvalidDataException>(() => engine.CreateLayout(text, Options(family)));
        }
    }

    /// <summary>PaintColrGlyph cannot resolve a legacy-only v0 base glyph as a v1 paint graph.</summary>
    [Fact]
    public void NestedColorReferenceCannotTargetLegacyOnlyGlyph()
    {
        byte[] data = FontData("ColorFontTest.ttf");
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
        using var engine = new ParleyTextEngine2D();
        string family = engine.AddFont(data);
        string text = char.ConvertFromUtf32(CodePointForGlyph(data, 166));
        Assert.Throws<InvalidDataException>(() => engine.CreateLayout(text, Options(family)));
    }

    /// <summary>Truncated COLR headers and offsets outside their table cannot silently become monochrome glyphs.</summary>
    /// <param name="truncate">Whether to truncate the table header instead of corrupting the base-list offset.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidColorTableMetadataIsRejected(bool truncate)
    {
        byte[] data = FontData("ColorFontTest.ttf");
        int count = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(4));
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            int record = 12 + (i * 16);
            if (BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(record)) != 0x434f4c52)
            {
                continue;
            }

            found = true;
            if (truncate)
            {
                BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(record + 12), 20);
            }
            else
            {
                int colr = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(record + 8)));
                BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(colr + 14), uint.MaxValue);
            }

            break;
        }

        Assert.True(found);
        Assert.Throws<InvalidDataException>(() =>
        {
            using var engine = new ParleyTextEngine2D();
            string family = engine.AddFont(data);
            engine.CreateLayout(char.ConvertFromUtf32(0xf0b06), Options(family));
        });
    }

    private static TextPaintGeometry2D Paint(ParleyTextEngine2D engine, string family, int codePoint)
        => Assert.IsType<TextPaintGeometry2D>(Assert.Single(engine.CreateLayout(char.ConvertFromUtf32(codePoint), Options(family)).Drawings));

    private static ParleyTextOptions2D Options(string family) => new() { FontFamily = family, FontSize = 64 };

    private static byte[] FontData(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fonts", name));

    private static int TableOffset(byte[] data, uint tag)
    {
        int count = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(4));
        for (int i = 0; i < count; i++)
        {
            int record = 12 + (i * 16);
            if (BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(record)) == tag)
            {
                return checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(record + 8)));
            }
        }

        throw new InvalidDataException("The fixture is missing the requested font table.");
    }

    private static int CodePointForGlyph(byte[] data, ushort glyphId)
    {
        int cmap = TableOffset(data, 0x636d6170);
        int count = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(cmap + 2));
        for (int i = 0; i < count; i++)
        {
            int subtable = checked(cmap + (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(cmap + 8 + (i * 8))));
            if (BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(subtable)) != 12)
            {
                continue;
            }

            int groups = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(subtable + 12)));
            for (int group = 0; group < groups; group++)
            {
                int record = subtable + 16 + (group * 12);
                uint first = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(record));
                uint last = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(record + 4));
                uint firstGlyph = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(record + 8));
                if (glyphId >= firstGlyph && glyphId - firstGlyph <= last - first)
                {
                    return checked((int)(first + glyphId - firstGlyph));
                }
            }
        }

        throw new InvalidDataException("The fixture glyph has no supplementary Unicode mapping.");
    }
}
