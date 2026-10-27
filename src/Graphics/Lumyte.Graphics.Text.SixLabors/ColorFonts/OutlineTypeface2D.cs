using System.Buffers.Binary;
using SkiaSharp;

namespace Lumyte.Graphics.Text;

/// <summary>Builds an outline-only face with unchanged glyph IDs, bypassing native color-image glyph classification.</summary>
internal static class OutlineTypeface2D
{
    public static SKTypeface Create(SKTypeface source)
    {
        var tables = new List<(uint Tag, byte[] Data)>();
        foreach (uint tag in source.GetTableTags().Order())
        {
            // Color tables can cause GetGlyphPath to hide even a valid glyf/CFF outline.
            // DSIG is invalidated by rebuilding the font directory and must be omitted.
            if (tag is 0x434f4c52 or 0x4350414c or 0x53564720 or 0x73626978 or 0x43424454 or 0x43424c43 or 0x45424454 or 0x45424c43 or 0x45425343 or 0x44534947)
            {
                continue;
            }

            byte[] data = source.GetTableData(tag);
            if (tag == 0x68656164)
            {
                if (data.Length < 12)
                {
                    throw new InvalidDataException("The outline face has a truncated head table.");
                }

                data.AsSpan(8, 4).Clear();
            }

            tables.Add((tag, data));
        }

        if (tables.Count is < 1 or > 4095 || !tables.Any(table => table.Tag == 0x68656164))
        {
            throw new InvalidDataException("The outline face requires a valid OpenType table directory and head table.");
        }

        NormalizeTrueTypeBearings(tables);
        int offset = checked(12 + (tables.Count * 16));
        int length = offset;
        foreach ((_, byte[] data) in tables)
        {
            length = checked(length + Padded(data.Length));
        }

        byte[] font = new byte[length];
        bool cff = tables.Any(table => table.Tag is 0x43464620 or 0x43464632);
        Write32(font, 0, cff ? 0x4f54544fu : 0x00010000u);
        Write16(font, 4, tables.Count);
        int power = 1;
        int selector = 0;
        while (power * 2 <= tables.Count)
        {
            power *= 2;
            selector++;
        }

        Write16(font, 6, power * 16);
        Write16(font, 8, selector);
        Write16(font, 10, (tables.Count - power) * 16);
        int head = 0;
        for (int i = 0; i < tables.Count; i++)
        {
            (uint tag, byte[] data) = tables[i];
            int record = 12 + (i * 16);
            Write32(font, record, tag);
            Write32(font, record + 4, Checksum(data));
            Write32(font, record + 8, (uint)offset);
            Write32(font, record + 12, (uint)data.Length);
            data.CopyTo(font, offset);
            if (tag == 0x68656164)
            {
                head = offset;
            }

            offset += Padded(data.Length);
        }

        Write32(font, head + 8, unchecked(0xb1b0afba - Checksum(font)));
        using var blob = SKData.CreateCopy(font);
        return SKTypeface.FromData(blob) ?? throw new InvalidDataException("Skia rejected the rebuilt outline-only font face.");
    }

    private static int Padded(int length) => checked((length + 3) & ~3);

    private static void NormalizeTrueTypeBearings(List<(uint Tag, byte[] Data)> tables)
    {
        var records = tables.ToDictionary(table => table.Tag, table => table.Data);
        if (!records.TryGetValue(0x676c7966, out byte[]? glyf))
        {
            return;
        }

        byte[] Table(uint tag, int minimum)
        {
            if (!records.TryGetValue(tag, out byte[]? bytes) || bytes.Length < minimum)
            {
                throw new InvalidDataException("The TrueType outline face has a missing or truncated metrics table.");
            }

            return bytes;
        }

        byte[] head = Table(0x68656164, 54);
        byte[] maxp = Table(0x6d617870, 6);
        byte[] hhea = Table(0x68686561, 36);
        int glyphs = BinaryPrimitives.ReadUInt16BigEndian(maxp.AsSpan(4));
        int metrics = BinaryPrimitives.ReadUInt16BigEndian(hhea.AsSpan(34));
        int format = BinaryPrimitives.ReadInt16BigEndian(head.AsSpan(50));
        if (metrics == 0 || metrics > glyphs || format is not 0 and not 1)
        {
            throw new InvalidDataException("The TrueType outline metrics or glyph-location format is invalid.");
        }

        byte[] hmtx = Table(0x686d7478, checked((metrics * 4) + ((glyphs - metrics) * 2)));
        byte[] loca = Table(0x6c6f6361, checked((glyphs + 1) * (format == 0 ? 2 : 4)));
        uint Location(int index) => format == 0
            ? (uint)BinaryPrimitives.ReadUInt16BigEndian(loca.AsSpan(index * 2)) * 2
            : BinaryPrimitives.ReadUInt32BigEndian(loca.AsSpan(index * 4));

        for (int glyph = 0; glyph < glyphs; glyph++)
        {
            uint start = Location(glyph);
            uint end = Location(glyph + 1);
            if (start > end || end > glyf.Length || (start != end && end - start < 10))
            {
                throw new InvalidDataException("The TrueType glyph bounds lie outside the outline table.");
            }

            short minimum = start == end ? (short)0 : BinaryPrimitives.ReadInt16BigEndian(glyf.AsSpan(checked((int)start + 2)));
            int bearing = glyph < metrics ? (glyph * 4) + 2 : (metrics * 4) + ((glyph - metrics) * 2);

            // COLR paths use design coordinates, independent of horizontal layout metrics.
            // Matching LSB to xMin prevents native phantom-point normalization from moving them.
            BinaryPrimitives.WriteInt16BigEndian(hmtx.AsSpan(bearing), minimum);
        }
    }

    private static void Write16(byte[] bytes, int offset, int value)
        => BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offset), checked((ushort)value));

    private static void Write32(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset), value);

    private static uint Checksum(ReadOnlySpan<byte> bytes)
    {
        uint result = 0;
        for (int i = 0; i < bytes.Length; i += 4)
        {
            uint word = 0;
            for (int component = 0; component < 4; component++)
            {
                word = (word << 8) | (i + component < bytes.Length ? bytes[i + component] : 0u);
            }

            result = unchecked(result + word);
        }

        return result;
    }
}
