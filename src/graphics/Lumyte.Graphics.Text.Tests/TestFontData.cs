using System.Buffers.Binary;
using System.IO.Compression;

namespace Lumyte.Graphics.Text.Tests;

internal static class TestFontData
{
    internal const ushort UnitsPerEm = 1_000;
    internal const uint ColorBitmapGlyphId = 3;
    internal const uint ColorBitmapPixelsPerEm = 10;
    internal const uint ColorBitmapWidth = 2;
    internal const uint ColorBitmapHeight = 2;

    internal static byte[] Create()
        => CreateCore(includeColor: false, invalidColorLayer: false, includeColorBitmap: false, includeColorPaint: false);

    internal static byte[] CreateColor()
        => CreateCore(includeColor: true, invalidColorLayer: false, includeColorBitmap: false, includeColorPaint: false);

    internal static byte[] CreateColorV1()
        => CreateCore(includeColor: true, invalidColorLayer: false, includeColorBitmap: false, includeColorPaint: true);

    internal static byte[] CreateColorV1Features()
        => CreateCore(
            includeColor: true,
            invalidColorLayer: false,
            includeColorBitmap: false,
            includeColorPaint: true,
            colorPaint: CreateColorPaintFeatures());

    internal static byte[] CreateColorV1WithNestedTransforms()
        => CreateCore(
            includeColor: true,
            invalidColorLayer: false,
            includeColorBitmap: false,
            includeColorPaint: true,
            colorPaint: CreateSingleColorPaint(
                3,
                CreateTransformPaint(
                    CreateTransformPaint(
                        CreatePaintGlyph(4, CreateSolidPaint(0)),
                        xx: 0.5f,
                        yy: 1),
                    dx: 400)));

    internal static byte[] CreateColorV1TableCoverage()
        => CreateCore(
            includeColor: true,
            invalidColorLayer: false,
            includeColorBitmap: false,
            includeColorPaint: true,
            colorPaint: CreateColorPaintTableCoverage(),
            includeVariationAxis: true);

    internal static byte[] CreateColorV1WithEmptyColorLine()
        => CreateCore(
            includeColor: true,
            invalidColorLayer: false,
            includeColorBitmap: false,
            includeColorPaint: true,
            colorPaint: CreateSingleColorPaint(
                3,
                CreatePaintGlyph(4, CreateEmptyLinearGradientPaint())));

    internal static byte[] CreateColorV1WithEmptyClipGlyph()
        => CreateCore(
            includeColor: true,
            invalidColorLayer: false,
            includeColorBitmap: false,
            includeColorPaint: true,
            colorPaint: CreateSingleColorPaint(
                3,
                CreatePaintGlyph(0, CreateSolidPaint(0))));

    internal static byte[] CreateColorV1WithUnreadableClipGlyph()
        => CreateCore(
            includeColor: true,
            invalidColorLayer: false,
            includeColorBitmap: false,
            includeColorPaint: true,
            colorPaint: CreateSingleColorPaint(
                3,
                CreatePaintGlyph(99, CreateSolidPaint(0))));

    internal static byte[] CreateColorV1WithUnknownExtendMode()
        => CreateCore(
            includeColor: true,
            invalidColorLayer: false,
            includeColorBitmap: false,
            includeColorPaint: true,
            colorPaint: CreateSingleColorPaint(
                3,
                CreatePaintGlyph(4, CreateLinearGradientPaint(byte.MaxValue))));

    internal static byte[] CreateColorV1WithUnknownCompositeMode()
        => CreateCore(
            includeColor: true,
            invalidColorLayer: false,
            includeColorBitmap: false,
            includeColorPaint: true,
            colorPaint: CreateSingleColorPaint(
                3,
                CreateCompositePaint(
                    CreatePaintGlyph(5, CreateSolidPaint(1)),
                    byte.MaxValue,
                    CreatePaintGlyph(4, CreateSolidPaint(0)))));

    internal static byte[] CreateColorWithInvalidLayer()
        => CreateCore(includeColor: true, invalidColorLayer: true, includeColorBitmap: false, includeColorPaint: false);

    internal static byte[] CreateColorBitmap()
        => CreateCore(includeColor: false, invalidColorLayer: false, includeColorBitmap: true, includeColorPaint: false);

    internal static byte[] ExpectedColorBitmapPixels() =>
    [
        255, 0, 0, 255,
        0, 255, 0, 255,
        0, 0, 255, 255,
        0, 0, 0, 0,
    ];

    private static byte[] CreateCore(
        bool includeColor,
        bool invalidColorLayer,
        bool includeColorBitmap,
        bool includeColorPaint,
        byte[]? colorPaint = null,
        bool includeVariationAxis = false)
    {
        byte[] glyphData = CreateGlyphData(
            includeColor,
            invalidColorLayer,
            includeColorBitmap,
            out ushort[] glyphOffsets);
        var tables = new List<TableRecord>
        {
            new TableRecord("cmap", CreateCharacterMap(includeColorPaint && colorPaint is not null)),
            new TableRecord("glyf", glyphData),
            new TableRecord("head", CreateFontHeader()),
            new TableRecord("hhea", CreateHorizontalHeader(checked((ushort)(includeColor ? 6 : 4)))),
            new TableRecord("hmtx", CreateHorizontalMetrics(includeColor, invalidColorLayer)),
            new TableRecord("loca", CreateGlyphLocations(glyphOffsets)),
            new TableRecord("maxp", CreateMaximumProfile(checked((ushort)(includeColor ? 6 : 4)))),
        };

        if (includeColor)
        {
            tables.Add(new TableRecord(
                "COLR",
                includeColorPaint
                    ? colorPaint ?? CreateColorPaint()
                    : CreateColorLayers(invalidColorLayer)));
            tables.Add(new TableRecord("CPAL", CreateColorPalettes()));
        }
        if (includeColorBitmap)
        {
            byte[] png = CreateColorBitmapPng();
            tables.Add(new TableRecord("CBDT", CreateColorBitmapData(png)));
            tables.Add(new TableRecord("CBLC", CreateColorBitmapLocations(png.Length)));
        }
        if (includeVariationAxis)
        {
            tables.Add(new TableRecord("fvar", CreateFontVariations()));
        }

        return BuildFont([.. tables]);
    }

    private static byte[] CreateCharacterMap(bool includeColorPaintFeatures)
    {
        var writer = new BigEndianWriter();

        writer.WriteUInt16(0); // cmap version
        writer.WriteUInt16(1); // encoding record count
        writer.WriteUInt16(3); // Windows
        writer.WriteUInt16(10); // full Unicode repertoire
        writer.WriteUInt32(12); // subtable offset

        writer.WriteUInt16(12); // format
        writer.WriteUInt16(0); // reserved
        writer.WriteUInt32(40); // subtable length
        writer.WriteUInt32(0); // language
        writer.WriteUInt32(2); // character groups

        writer.WriteUInt32('A');
        writer.WriteUInt32(includeColorPaintFeatures ? 'E' : 'B');
        writer.WriteUInt32(1); // A onward map to consecutive glyphs

        writer.WriteUInt32(0x1F600); // GRINNING FACE, represented by a UTF-16 surrogate pair
        writer.WriteUInt32(0x1F600);
        writer.WriteUInt32(3);

        return writer.ToArray();
    }

    private static byte[] CreateGlyphData(
        bool includeColor,
        bool invalidColorLayer,
        bool includeColorBitmap,
        out ushort[] offsets)
    {
        byte[] colorBaseGlyph = includeColorBitmap
            ? []
            : invalidColorLayer
                ? CreateSimpleGlyph([(200, 150), (200, 550), (500, 550), (500, 150)])
                : CreateSimpleGlyph([(50, 0), (50, 700), (650, 700), (650, 0)]);
        List<byte[]> glyphs =
        [
            [],
            CreateSimpleGlyph([(50, 0), (300, 700), (550, 0)]),
            CreateSimpleGlyph([(50, 0), (50, 700), (550, 700), (550, 0)]),
            colorBaseGlyph,
        ];

        if (includeColor)
        {
            // The color base glyph (3) is painted as a palette-colored background (4)
            // followed by a smaller foreground-colored inset (5).
            glyphs.Add(CreateSimpleGlyph([(50, 0), (50, 700), (650, 700), (650, 0)]));
            glyphs.Add(CreateSimpleGlyph([(200, 150), (200, 550), (500, 550), (500, 150)]));
        }

        var writer = new BigEndianWriter();
        offsets = new ushort[glyphs.Count + 1];
        for (int index = 0; index < glyphs.Count; index++)
        {
            offsets[index] = checked((ushort)(writer.Length / 2));
            writer.WriteBytes(glyphs[index]);
            writer.PadToMultiple(2);
        }
        offsets[^1] = checked((ushort)(writer.Length / 2));

        return writer.ToArray();
    }

    private static byte[] CreateSimpleGlyph((short X, short Y)[] points)
    {
        short minimumX = points.Min(static point => point.X);
        short minimumY = points.Min(static point => point.Y);
        short maximumX = points.Max(static point => point.X);
        short maximumY = points.Max(static point => point.Y);

        var writer = new BigEndianWriter();
        writer.WriteInt16(1); // contour count
        writer.WriteInt16(minimumX);
        writer.WriteInt16(minimumY);
        writer.WriteInt16(maximumX);
        writer.WriteInt16(maximumY);
        writer.WriteUInt16(checked((ushort)(points.Length - 1)));
        writer.WriteUInt16(0); // instruction byte count

        foreach ((short _, short _) in points)
        {
            writer.WriteByte(0x01); // on-curve; coordinates are signed 16-bit deltas
        }

        short previousX = 0;
        foreach ((short x, short _) in points)
        {
            writer.WriteInt16(checked((short)(x - previousX)));
            previousX = x;
        }

        short previousY = 0;
        foreach ((short _, short y) in points)
        {
            writer.WriteInt16(checked((short)(y - previousY)));
            previousY = y;
        }

        return writer.ToArray();
    }

    private static byte[] CreateFontHeader()
    {
        var writer = new BigEndianWriter();
        writer.WriteUInt32(0x00010000); // table version
        writer.WriteUInt32(0x00010000); // font revision
        writer.WriteUInt32(0); // checksum adjustment, patched after assembly
        writer.WriteUInt32(0x5F0F3CF5); // TrueType magic number
        writer.WriteUInt16(3); // baseline at y=0 and left sidebearing at x=0
        writer.WriteUInt16(UnitsPerEm);
        writer.WriteUInt64(0); // created
        writer.WriteUInt64(0); // modified
        writer.WriteInt16(50);
        writer.WriteInt16(0);
        writer.WriteInt16(650);
        writer.WriteInt16(700);
        writer.WriteUInt16(0); // macStyle
        writer.WriteUInt16(8); // lowest readable pixels per em
        writer.WriteInt16(2); // left-to-right glyph data
        writer.WriteInt16(0); // short loca offsets
        writer.WriteInt16(0); // glyph data format

        return writer.ToArray();
    }

    private static byte[] CreateHorizontalHeader(ushort horizontalMetricCount)
    {
        var writer = new BigEndianWriter();
        writer.WriteUInt32(0x00010000);
        writer.WriteInt16(800);
        writer.WriteInt16(-200);
        writer.WriteInt16(0);
        writer.WriteUInt16(700);
        writer.WriteInt16(0);
        writer.WriteInt16(50);
        writer.WriteInt16(650);
        writer.WriteInt16(1);
        writer.WriteInt16(0);
        writer.WriteInt16(0);
        writer.WriteInt16(0);
        writer.WriteInt16(0);
        writer.WriteInt16(0);
        writer.WriteInt16(0);
        writer.WriteInt16(0); // metric data format
        writer.WriteUInt16(horizontalMetricCount); // every glyph has a complete horizontal metric

        return writer.ToArray();
    }

    private static byte[] CreateHorizontalMetrics(bool includeColor, bool invalidColorLayer)
    {
        var writer = new BigEndianWriter();
        writer.WriteUInt16(600);
        writer.WriteInt16(0);
        writer.WriteUInt16(600);
        writer.WriteInt16(50);
        writer.WriteUInt16(650);
        writer.WriteInt16(50);
        writer.WriteUInt16(700);
        writer.WriteInt16(invalidColorLayer ? (short)200 : (short)50);
        if (includeColor)
        {
            // COLR v0 requires every layer glyph to use the base glyph's advance.
            writer.WriteUInt16(700);
            writer.WriteInt16(50);
            writer.WriteUInt16(700);
            writer.WriteInt16(200);
        }
        return writer.ToArray();
    }

    private static byte[] CreateGlyphLocations(ushort[] offsets)
    {
        var writer = new BigEndianWriter();
        foreach (ushort offset in offsets)
        {
            writer.WriteUInt16(offset);
        }
        return writer.ToArray();
    }

    private static byte[] CreateMaximumProfile(ushort glyphCount)
    {
        var writer = new BigEndianWriter();
        writer.WriteUInt32(0x00010000);
        writer.WriteUInt16(glyphCount);
        writer.WriteUInt16(4); // points in a non-composite glyph
        writer.WriteUInt16(1); // contours in a non-composite glyph
        writer.WriteUInt16(0); // composite points
        writer.WriteUInt16(0); // composite contours
        writer.WriteUInt16(1); // zones
        writer.WriteUInt16(0); // twilight points
        writer.WriteUInt16(0); // storage locations
        writer.WriteUInt16(0); // function definitions
        writer.WriteUInt16(0); // instruction definitions
        writer.WriteUInt16(0); // stack elements
        writer.WriteUInt16(0); // instruction bytes
        writer.WriteUInt16(0); // composite elements
        writer.WriteUInt16(0); // composite depth
        return writer.ToArray();
    }

    private static byte[] CreateColorLayers(bool invalidColorLayer)
    {
        var writer = new BigEndianWriter();
        writer.WriteUInt16(0); // COLR version 0
        writer.WriteUInt16(1); // base glyph records
        writer.WriteUInt32(14); // base glyph records offset
        writer.WriteUInt32(20); // layer records offset
        writer.WriteUInt16(2); // layer records

        writer.WriteUInt16(3); // color base glyph
        writer.WriteUInt16(0); // first layer index
        writer.WriteUInt16(2); // layer count

        writer.WriteUInt16(4); // bottom layer glyph
        writer.WriteUInt16(0); // selected palette's first entry
        writer.WriteUInt16(5); // top layer glyph
        writer.WriteUInt16(invalidColorLayer ? (ushort)99 : (ushort)0xFFFF);
        // 0xFFFF selects the application foreground; 99 deliberately exceeds the test palette.
        return writer.ToArray();
    }

    private static byte[] CreateColorPaint()
        => CreateSingleColorPaint(3, CreatePaintGlyph(4, CreateSolidPaint(0)));

    private static byte[] CreateSingleColorPaint(ushort glyphId, byte[] paint)
    {
        var writer = new BigEndianWriter();
        writer.WriteUInt16(1); // COLR version 1
        writer.WriteUInt16(0); // no version-zero base glyph records
        writer.WriteUInt32(0); // base glyph records offset
        writer.WriteUInt32(0); // layer records offset
        writer.WriteUInt16(0); // no version-zero layer records
        writer.WriteUInt32(34); // BaseGlyphList immediately follows the version-one header
        writer.WriteUInt32(0); // no LayerList
        writer.WriteUInt32(0); // no ClipList
        writer.WriteUInt32(0); // no DeltaSetIndexMap
        writer.WriteUInt32(0); // no ItemVariationStore

        writer.WriteUInt32(1); // one BaseGlyphPaintRecord
        writer.WriteUInt16(glyphId);
        writer.WriteUInt32(10); // paint follows the BaseGlyphPaintRecord
        writer.WriteBytes(paint);
        return writer.ToArray();
    }

    private static byte[] CreateColorPaintFeatures()
    {
        (ushort GlyphId, byte[] Paint)[] paints =
        [
            (1, CreatePaintGlyph(4, CreateLinearGradientPaint(1))),
            (2, CreatePaintGlyph(5, CreateRadialGradientPaint(2))),
            (3, CreatePaintGlyph(4, CreateSweepGradientPaint(0))),
            (4, CreateTransformPaint(
                CreatePaintGlyph(4, CreatePaintGlyph(5, CreateSolidPaint(0))))),
            (5, CreateCompositePaint(
                CreatePaintGlyph(5, CreateSolidPaint(1)),
                compositeMode: 5,
                CreatePaintGlyph(4, CreateSolidPaint(0)))),
        ];

        var baseGlyphList = new BigEndianWriter();
        baseGlyphList.WriteUInt32(checked((uint)paints.Length));
        uint paintOffset = checked((uint)(sizeof(uint) + (paints.Length * 6)));
        foreach ((ushort glyphId, byte[] paint) in paints)
        {
            baseGlyphList.WriteUInt16(glyphId);
            baseGlyphList.WriteUInt32(paintOffset);
            paintOffset = checked(paintOffset + (uint)paint.Length);
        }
        foreach ((ushort _, byte[] paint) in paints)
        {
            baseGlyphList.WriteBytes(paint);
        }

        var writer = new BigEndianWriter();
        writer.WriteUInt16(1); // COLR version 1
        writer.WriteUInt16(0); // no version-zero base glyph records
        writer.WriteUInt32(0); // base glyph records offset
        writer.WriteUInt32(0); // layer records offset
        writer.WriteUInt16(0); // no version-zero layer records
        writer.WriteUInt32(34); // BaseGlyphList immediately follows the version-one header
        writer.WriteUInt32(0); // no LayerList
        writer.WriteUInt32(0); // no ClipList
        writer.WriteUInt32(0); // no DeltaSetIndexMap
        writer.WriteUInt32(0); // no ItemVariationStore
        writer.WriteBytes(baseGlyphList.ToArray());
        return writer.ToArray();
    }

    private static byte[] CreateColorPaintTableCoverage()
    {
        byte[] layerPaint = CreateColorLayersPaint(2, 0);
        byte[] reusedPaint = CreateColorGlyphPaint(1);
        byte[] clippedPaint = CreatePaintGlyph(4, CreateSolidPaint(0));
        byte[] variablePaint = CreateVariableTranslatePaint(
            CreatePaintGlyph(5, CreateSolidPaint(0)),
            variableIndexBase: 0);

        (ushort GlyphId, byte[] Paint)[] paints =
        [
            (1, layerPaint),
            (2, reusedPaint),
            (3, clippedPaint),
            (4, variablePaint),
        ];

        var baseGlyphList = new BigEndianWriter();
        baseGlyphList.WriteUInt32(checked((uint)paints.Length));
        uint paintOffset = checked((uint)(sizeof(uint) + (paints.Length * 6)));
        foreach ((ushort glyphId, byte[] paint) in paints)
        {
            baseGlyphList.WriteUInt16(glyphId);
            baseGlyphList.WriteUInt32(paintOffset);
            paintOffset = checked(paintOffset + (uint)paint.Length);
        }
        foreach ((ushort _, byte[] paint) in paints)
        {
            baseGlyphList.WriteBytes(paint);
        }

        byte[] layerList = CreateLayerList(
            CreatePaintGlyph(4, CreateSolidPaint(0)),
            CreatePaintGlyph(5, CreateSolidPaint(ushort.MaxValue)));
        byte[] clipList = CreateClipList(
            glyphId: 3,
            minimumX: 200,
            minimumY: 100,
            maximumX: 450,
            maximumY: 600);
        byte[] variationStore = CreateItemVariationStore(xDelta: 300, yDelta: 0);

        const uint headerLength = 34;
        uint baseGlyphListOffset = headerLength;
        uint layerListOffset = checked(baseGlyphListOffset + (uint)baseGlyphList.Length);
        uint clipListOffset = checked(layerListOffset + (uint)layerList.Length);
        uint variationStoreOffset = checked(clipListOffset + (uint)clipList.Length);

        var writer = new BigEndianWriter();
        writer.WriteUInt16(1); // COLR version 1
        writer.WriteUInt16(0); // no version-zero base glyph records
        writer.WriteUInt32(0); // base glyph records offset
        writer.WriteUInt32(0); // layer records offset
        writer.WriteUInt16(0); // no version-zero layer records
        writer.WriteUInt32(baseGlyphListOffset);
        writer.WriteUInt32(layerListOffset);
        writer.WriteUInt32(clipListOffset);
        writer.WriteUInt32(0); // use the implicit delta-set index mapping
        writer.WriteUInt32(variationStoreOffset);
        writer.WriteBytes(baseGlyphList.ToArray());
        writer.WriteBytes(layerList);
        writer.WriteBytes(clipList);
        writer.WriteBytes(variationStore);
        return writer.ToArray();
    }

    private static byte[] CreateColorLayersPaint(byte layerCount, uint firstLayerIndex)
    {
        var writer = new BigEndianWriter();
        writer.WriteByte(1); // PaintColrLayers
        writer.WriteByte(layerCount);
        writer.WriteUInt32(firstLayerIndex);
        return writer.ToArray();
    }

    private static byte[] CreateColorGlyphPaint(ushort glyphId)
    {
        var writer = new BigEndianWriter();
        writer.WriteByte(11); // PaintColrGlyph
        writer.WriteUInt16(glyphId);
        return writer.ToArray();
    }

    private static byte[] CreateVariableTranslatePaint(byte[] paint, uint variableIndexBase)
    {
        const int headerLength = 12;
        var writer = new BigEndianWriter();
        writer.WriteByte(15); // PaintVarTranslate
        writer.WriteUInt24(headerLength);
        writer.WriteInt16(0); // base dx
        writer.WriteInt16(0); // base dy
        writer.WriteUInt32(variableIndexBase);
        writer.WriteBytes(paint);
        return writer.ToArray();
    }

    private static byte[] CreateLayerList(params byte[][] paints)
    {
        var writer = new BigEndianWriter();
        writer.WriteUInt32(checked((uint)paints.Length));
        uint paintOffset = checked((uint)(sizeof(uint) + (paints.Length * sizeof(uint))));
        foreach (byte[] paint in paints)
        {
            writer.WriteUInt32(paintOffset);
            paintOffset = checked(paintOffset + (uint)paint.Length);
        }
        foreach (byte[] paint in paints)
        {
            writer.WriteBytes(paint);
        }
        return writer.ToArray();
    }

    private static byte[] CreateClipList(
        ushort glyphId,
        short minimumX,
        short minimumY,
        short maximumX,
        short maximumY)
    {
        const uint clipBoxOffset = 12;
        var writer = new BigEndianWriter();
        writer.WriteByte(1); // ClipList format 1
        writer.WriteUInt32(1); // one Clip record
        writer.WriteUInt16(glyphId);
        writer.WriteUInt16(glyphId);
        writer.WriteUInt24(clipBoxOffset);
        writer.WriteByte(1); // ClipBox format 1
        writer.WriteInt16(minimumX);
        writer.WriteInt16(minimumY);
        writer.WriteInt16(maximumX);
        writer.WriteInt16(maximumY);
        return writer.ToArray();
    }

    private static byte[] CreateItemVariationStore(short xDelta, short yDelta)
    {
        const uint variationRegionListOffset = 12;
        const uint itemVariationDataOffset = 22;
        var writer = new BigEndianWriter();
        writer.WriteUInt16(1); // ItemVariationStore format 1
        writer.WriteUInt32(variationRegionListOffset);
        writer.WriteUInt16(1); // one ItemVariationData subtable
        writer.WriteUInt32(itemVariationDataOffset);

        writer.WriteUInt16(1); // one design-space axis
        writer.WriteUInt16(1); // one variation region
        writer.WriteUInt16(0); // region starts at the default coordinate
        writer.WriteUInt16(0x4000); // region peaks at the maximum coordinate
        writer.WriteUInt16(0x4000); // region ends at the maximum coordinate

        writer.WriteUInt16(2); // one item for dx and one for dy
        writer.WriteUInt16(1); // one 16-bit delta per item
        writer.WriteUInt16(1); // one region contributes to each delta set
        writer.WriteUInt16(0); // region index 0
        writer.WriteInt16(xDelta);
        writer.WriteInt16(yDelta);
        return writer.ToArray();
    }

    private static byte[] CreatePaintGlyph(ushort glyphId, byte[] paint)
    {
        var writer = new BigEndianWriter();
        writer.WriteByte(10); // PaintGlyph
        writer.WriteUInt24(6); // child follows this table
        writer.WriteUInt16(glyphId);
        writer.WriteBytes(paint);
        return writer.ToArray();
    }

    private static byte[] CreateSolidPaint(ushort paletteIndex)
    {
        var writer = new BigEndianWriter();
        writer.WriteByte(2); // PaintSolid
        writer.WriteUInt16(paletteIndex);
        writer.WriteUInt16(0x4000); // opaque F2DOT14 alpha
        return writer.ToArray();
    }

    private static byte[] CreateLinearGradientPaint(byte extendMode)
    {
        var writer = new BigEndianWriter();
        writer.WriteByte(4); // PaintLinearGradient
        writer.WriteUInt24(16); // ColorLine follows the fixed fields
        writer.WriteInt16(10);
        writer.WriteInt16(20);
        writer.WriteInt16(310);
        writer.WriteInt16(420);
        writer.WriteInt16(30);
        writer.WriteInt16(520);
        WriteColorLine(writer, extendMode);
        return writer.ToArray();
    }

    private static byte[] CreateEmptyLinearGradientPaint()
    {
        var writer = new BigEndianWriter();
        writer.WriteByte(4); // PaintLinearGradient
        writer.WriteUInt24(16); // ColorLine follows the fixed fields
        writer.WriteInt16(10);
        writer.WriteInt16(20);
        writer.WriteInt16(310);
        writer.WriteInt16(420);
        writer.WriteInt16(30);
        writer.WriteInt16(520);
        writer.WriteByte(0); // pad extension
        writer.WriteUInt16(0); // an empty color line paints transparent black
        return writer.ToArray();
    }

    private static byte[] CreateRadialGradientPaint(byte extendMode)
    {
        var writer = new BigEndianWriter();
        writer.WriteByte(6); // PaintRadialGradient
        writer.WriteUInt24(16); // ColorLine follows the fixed fields
        writer.WriteInt16(100);
        writer.WriteInt16(200);
        writer.WriteUInt16(25);
        writer.WriteInt16(350);
        writer.WriteInt16(450);
        writer.WriteUInt16(275);
        WriteColorLine(writer, extendMode);
        return writer.ToArray();
    }

    private static byte[] CreateSweepGradientPaint(byte extendMode)
    {
        var writer = new BigEndianWriter();
        writer.WriteByte(8); // PaintSweepGradient
        writer.WriteUInt24(12); // ColorLine follows the fixed fields
        writer.WriteInt16(325);
        writer.WriteInt16(350);
        writer.WriteInt16(unchecked((short)0xC000)); // -1 maps to zero radians
        writer.WriteInt16(0); // 0 maps to pi radians
        WriteColorLine(writer, extendMode);
        return writer.ToArray();
    }

    private static byte[] CreateTransformPaint(byte[] paint)
        => CreateTransformPaint(
            paint,
            xx: 1,
            yx: 0.25f,
            xy: -0.5f,
            yy: 1.5f,
            dx: 25,
            dy: -40);

    private static byte[] CreateTransformPaint(
        byte[] paint,
        float xx = 1,
        float yx = 0,
        float xy = 0,
        float yy = 1,
        float dx = 0,
        float dy = 0)
    {
        const int headerLength = 7;
        var writer = new BigEndianWriter();
        writer.WriteByte(12); // PaintTransform
        writer.WriteUInt24(headerLength);
        writer.WriteUInt24(checked((uint)(headerLength + paint.Length)));
        writer.WriteBytes(paint);
        WriteFixed(writer, xx);
        WriteFixed(writer, yx);
        WriteFixed(writer, xy);
        WriteFixed(writer, yy);
        WriteFixed(writer, dx);
        WriteFixed(writer, dy);
        return writer.ToArray();
    }

    private static byte[] CreateCompositePaint(
        byte[] sourcePaint,
        byte compositeMode,
        byte[] backdropPaint)
    {
        const int headerLength = 8;
        var writer = new BigEndianWriter();
        writer.WriteByte(32); // PaintComposite
        writer.WriteUInt24(headerLength);
        writer.WriteByte(compositeMode);
        writer.WriteUInt24(checked((uint)(headerLength + sourcePaint.Length)));
        writer.WriteBytes(sourcePaint);
        writer.WriteBytes(backdropPaint);
        return writer.ToArray();
    }

    private static void WriteColorLine(BigEndianWriter writer, byte extendMode)
    {
        writer.WriteByte(extendMode);
        writer.WriteUInt16(3);

        // Keep the records deliberately out of offset order. Consumers must retain the
        // original order for equal offsets while sorting the color line by offset.
        WriteColorStop(writer, 0x4000, 0, 0x4000);
        WriteColorStop(writer, 0, 0xFFFF, 0x4000);
        WriteColorStop(writer, 0x2000, 1, 0x4000);
    }

    private static void WriteColorStop(
        BigEndianWriter writer,
        ushort stopOffset,
        ushort paletteIndex,
        ushort alpha)
    {
        writer.WriteUInt16(stopOffset);
        writer.WriteUInt16(paletteIndex);
        writer.WriteUInt16(alpha);
    }

    private static void WriteFixed(BigEndianWriter writer, float value)
    {
        int fixedValue = checked((int)MathF.Round(value * 65_536));
        writer.WriteUInt32(unchecked((uint)fixedValue));
    }

    private static byte[] CreateColorPalettes()
    {
        var writer = new BigEndianWriter();
        writer.WriteUInt16(0); // CPAL version 0
        writer.WriteUInt16(2); // entries in each palette
        writer.WriteUInt16(2); // palettes
        writer.WriteUInt16(4); // color records
        writer.WriteUInt32(16); // color records offset
        writer.WriteUInt16(0); // blue palette starts at color record 0
        writer.WriteUInt16(2); // green palette starts at color record 2

        writer.WriteByte(255); // blue: BGRA
        writer.WriteByte(0);
        writer.WriteByte(0);
        writer.WriteByte(255);
        writer.WriteByte(64); // non-primary, translucent BGRA entry
        writer.WriteByte(128);
        writer.WriteByte(192);
        writer.WriteByte(128);
        writer.WriteByte(0); // green: BGRA
        writer.WriteByte(255);
        writer.WriteByte(0);
        writer.WriteByte(255);
        writer.WriteByte(192); // second palette's unused contrasting entry
        writer.WriteByte(64);
        writer.WriteByte(128);
        writer.WriteByte(255);
        return writer.ToArray();
    }

    private static byte[] CreateFontVariations()
    {
        var writer = new BigEndianWriter();
        writer.WriteUInt16(1); // fvar major version
        writer.WriteUInt16(0); // fvar minor version
        writer.WriteUInt16(16); // axes array immediately follows the header
        writer.WriteUInt16(2); // reserved
        writer.WriteUInt16(1); // one variation axis
        writer.WriteUInt16(20); // VariationAxisRecord size
        writer.WriteUInt16(0); // no named instances
        writer.WriteUInt16(8); // nominal instance record size for one axis

        writer.WriteUInt32(0x77676874); // 'wght'
        writer.WriteUInt32(0); // minimum value 0.0
        writer.WriteUInt32(0); // default value 0.0
        writer.WriteUInt32(0x00010000); // maximum value 1.0
        writer.WriteUInt16(0); // axis flags
        writer.WriteUInt16(256); // axis name ID (not needed by the renderer)
        return writer.ToArray();
    }

    private static byte[] CreateColorBitmapData(byte[] png)
    {
        var writer = new BigEndianWriter();
        writer.WriteUInt16(3); // CBDT major version
        writer.WriteUInt16(0); // CBDT minor version

        writer.WriteByte(checked((byte)ColorBitmapHeight)); // small glyph metrics: height
        writer.WriteByte(checked((byte)ColorBitmapWidth)); // width
        writer.WriteSignedByte(1); // horizontal bearing X
        writer.WriteSignedByte(2); // horizontal bearing Y
        writer.WriteByte(7); // horizontal advance
        writer.WriteUInt32(checked((uint)png.Length));
        writer.WriteBytes(png);
        return writer.ToArray();
    }

    private static byte[] CreateColorBitmapLocations(int pngLength)
    {
        const uint indexSubtableArrayOffset = 56;
        const uint indexSubtableArrayLength = 8;
        const uint indexSubtableLength = 16;
        const uint imageDataOffset = 4;
        uint imageRecordLength = checked(5u + sizeof(uint) + (uint)pngLength);

        var writer = new BigEndianWriter();
        writer.WriteUInt16(3); // CBLC major version
        writer.WriteUInt16(0); // CBLC minor version
        writer.WriteUInt32(1); // bitmap strikes

        writer.WriteUInt32(indexSubtableArrayOffset);
        writer.WriteUInt32(indexSubtableArrayLength + indexSubtableLength);
        writer.WriteUInt32(1); // index subtables
        writer.WriteUInt32(0); // reserved color reference
        WriteHorizontalBitmapLineMetrics(writer);
        WriteEmptyBitmapLineMetrics(writer);
        writer.WriteUInt16(checked((ushort)ColorBitmapGlyphId));
        writer.WriteUInt16(checked((ushort)ColorBitmapGlyphId));
        writer.WriteByte(checked((byte)ColorBitmapPixelsPerEm));
        writer.WriteByte(checked((byte)ColorBitmapPixelsPerEm));
        writer.WriteByte(32); // 8-bit BGRA color channels
        writer.WriteSignedByte(1); // horizontal small-glyph metrics

        writer.WriteUInt16(checked((ushort)ColorBitmapGlyphId));
        writer.WriteUInt16(checked((ushort)ColorBitmapGlyphId));
        writer.WriteUInt32(indexSubtableArrayLength);

        writer.WriteUInt16(1); // index format 1: variable metrics and uint32 offsets
        writer.WriteUInt16(17); // CBDT format 17: small metrics followed by PNG
        writer.WriteUInt32(imageDataOffset);
        writer.WriteUInt32(0); // glyph data begins at imageDataOffset
        writer.WriteUInt32(imageRecordLength); // sentinel just past the glyph data
        return writer.ToArray();
    }

    private static void WriteHorizontalBitmapLineMetrics(BigEndianWriter writer)
    {
        writer.WriteSignedByte(8); // ascender
        writer.WriteSignedByte(-2); // descender
        writer.WriteByte(checked((byte)ColorBitmapWidth));
        writer.WriteSignedByte(1); // caret slope numerator
        writer.WriteSignedByte(0); // caret slope denominator
        writer.WriteSignedByte(0); // caret offset
        writer.WriteSignedByte(1); // minimum origin side bearing
        writer.WriteSignedByte(4); // minimum advance side bearing
        writer.WriteSignedByte(2); // maximum extent before the baseline
        writer.WriteSignedByte(0); // minimum extent after the baseline
        writer.WriteSignedByte(0);
        writer.WriteSignedByte(0);
    }

    private static void WriteEmptyBitmapLineMetrics(BigEndianWriter writer)
    {
        for (int index = 0; index < 12; index++)
        {
            writer.WriteSignedByte(0);
        }
    }

    private static byte[] CreateColorBitmapPng()
    {
        var header = new BigEndianWriter();
        header.WriteUInt32(ColorBitmapWidth);
        header.WriteUInt32(ColorBitmapHeight);
        header.WriteByte(4); // four-bit palette indices, as used by compact CBDT strikes
        header.WriteByte(3); // indexed color type
        header.WriteByte(0); // deflate compression
        header.WriteByte(0); // adaptive filtering
        header.WriteByte(0); // no interlace

        byte[] scanlines =
        [
            0, 0x01, // red, green
            0, 0x23, // blue, transparent
        ];
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(scanlines);
        }

        var png = new BigEndianWriter();
        png.WriteBytes([137, 80, 78, 71, 13, 10, 26, 10]);
        WritePngChunk(png, "IHDR", header.ToArray());
        WritePngChunk(
            png,
            "PLTE",
            [
                255, 0, 0,
                0, 255, 0,
                0, 0, 255,
                0, 0, 0,
            ]);
        WritePngChunk(png, "tRNS", [255, 255, 255, 0]);
        WritePngChunk(png, "IDAT", compressed.ToArray());
        WritePngChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WritePngChunk(BigEndianWriter writer, string type, byte[] data)
    {
        byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        writer.WriteUInt32(checked((uint)data.Length));
        writer.WriteBytes(typeBytes);
        writer.WriteBytes(data);
        writer.WriteUInt32(CalculatePngCrc(typeBytes, data));
    }

    private static uint CalculatePngCrc(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        UpdatePngCrc(ref crc, type);
        UpdatePngCrc(ref crc, data);
        return ~crc;
    }

    private static void UpdatePngCrc(ref uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0
                    ? 0xEDB88320u ^ (crc >> 1)
                    : crc >> 1;
            }
        }
    }

    private static byte[] BuildFont(TableRecord[] unsortedTables)
    {
        TableRecord[] tables = unsortedTables
            .OrderBy(static table => table.Tag, StringComparer.Ordinal)
            .ToArray();

        ushort maximumPowerOfTwo = 1;
        ushort entrySelector = 0;
        while (maximumPowerOfTwo * 2 <= tables.Length)
        {
            maximumPowerOfTwo *= 2;
            entrySelector++;
        }

        int fontLength = Align4(12 + (tables.Length * 16));
        foreach (TableRecord table in tables)
        {
            table.Offset = checked((uint)fontLength);
            table.Checksum = CalculateChecksum(table.Data);
            fontLength += Align4(table.Data.Length);
        }

        var font = new byte[fontLength];
        WriteUInt32(font, 0, 0x00010000);
        WriteUInt16(font, 4, checked((ushort)tables.Length));
        WriteUInt16(font, 6, checked((ushort)(maximumPowerOfTwo * 16)));
        WriteUInt16(font, 8, entrySelector);
        WriteUInt16(font, 10, checked((ushort)((tables.Length * 16) - (maximumPowerOfTwo * 16))));

        int directoryOffset = 12;
        foreach (TableRecord table in tables)
        {
            WriteTag(font, directoryOffset, table.Tag);
            WriteUInt32(font, directoryOffset + 4, table.Checksum);
            WriteUInt32(font, directoryOffset + 8, table.Offset);
            WriteUInt32(font, directoryOffset + 12, checked((uint)table.Data.Length));
            table.Data.CopyTo(font, checked((int)table.Offset));
            directoryOffset += 16;
        }

        TableRecord head = tables.Single(static table => table.Tag == "head");
        uint checksumAdjustment = unchecked(0xB1B0AFBAu - CalculateChecksum(font));
        WriteUInt32(font, checked((int)head.Offset) + 8, checksumAdjustment);
        return font;
    }

    private static int Align4(int value) => (value + 3) & ~3;

    private static uint CalculateChecksum(byte[] bytes)
    {
        uint checksum = 0;
        for (int offset = 0; offset < bytes.Length; offset += 4)
        {
            uint value = (uint)bytes[offset] << 24;
            if (offset + 1 < bytes.Length)
            {
                value |= (uint)bytes[offset + 1] << 16;
            }
            if (offset + 2 < bytes.Length)
            {
                value |= (uint)bytes[offset + 2] << 8;
            }
            if (offset + 3 < bytes.Length)
            {
                value |= bytes[offset + 3];
            }
            checksum = unchecked(checksum + value);
        }
        return checksum;
    }

    private static void WriteTag(byte[] destination, int offset, string tag)
    {
        for (int index = 0; index < 4; index++)
        {
            destination[offset + index] = checked((byte)tag[index]);
        }
    }

    private static void WriteUInt16(byte[] destination, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16BigEndian(destination.AsSpan(offset, sizeof(ushort)), value);

    private static void WriteUInt32(byte[] destination, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(destination.AsSpan(offset, sizeof(uint)), value);

    private sealed class TableRecord(string tag, byte[] data)
    {
        internal string Tag { get; } = tag;
        internal byte[] Data { get; } = data;
        internal uint Offset { get; set; }
        internal uint Checksum { get; set; }
    }

    private sealed class BigEndianWriter
    {
        private readonly List<byte> bytes = [];

        internal int Length => bytes.Count;

        internal void WriteByte(byte value) => bytes.Add(value);

        internal void WriteSignedByte(sbyte value) => WriteByte(unchecked((byte)value));

        internal void WriteBytes(byte[] value) => bytes.AddRange(value);

        internal void WriteInt16(short value) => WriteUInt16(unchecked((ushort)value));

        internal void WriteUInt16(ushort value)
        {
            bytes.Add((byte)(value >> 8));
            bytes.Add((byte)value);
        }

        internal void WriteUInt32(uint value)
        {
            bytes.Add((byte)(value >> 24));
            bytes.Add((byte)(value >> 16));
            bytes.Add((byte)(value >> 8));
            bytes.Add((byte)value);
        }

        internal void WriteUInt24(uint value)
        {
            if (value > 0xFFFFFF)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            bytes.Add((byte)(value >> 16));
            bytes.Add((byte)(value >> 8));
            bytes.Add((byte)value);
        }

        internal void WriteUInt64(ulong value)
        {
            WriteUInt32((uint)(value >> 32));
            WriteUInt32((uint)value);
        }

        internal void PadToMultiple(int alignment)
        {
            while (bytes.Count % alignment != 0)
            {
                bytes.Add(0);
            }
        }

        internal byte[] ToArray() => [.. bytes];
    }
}
