using System.Numerics;
using HarfBuzzSharp;

namespace Lumyte.Graphics.Text;

/// <summary>Reads and resolves outlines from an OpenType glyf/loca table pair.</summary>
internal sealed class TrueTypeOutlineReader
{
    private const int MaximumCompositeDepth = 32;

    private readonly byte[] glyphTable;
    private readonly byte[] locationTable;
    private readonly bool usesLongLocations;
    private readonly int glyphCount;

    private TrueTypeOutlineReader(
        byte[] glyphTable,
        byte[] locationTable,
        bool usesLongLocations,
        int glyphCount)
    {
        this.glyphTable = glyphTable;
        this.locationTable = locationTable;
        this.usesLongLocations = usesLongLocations;
        this.glyphCount = glyphCount;
    }

    public static TrueTypeOutlineReader? TryCreate(Face face)
    {
        ArgumentNullException.ThrowIfNull(face);

        byte[]? head = ReadTable(face, "head");
        byte[]? glyf = ReadTable(face, "glyf");
        byte[]? loca = ReadTable(face, "loca");
        byte[]? maxp = ReadTable(face, "maxp");
        if (head is null || glyf is null || loca is null || maxp is null || head.Length < 54 || maxp.Length < 6)
        {
            return null;
        }

        try
        {
            short locationFormat = ReadInt16(head, 50);
            if (locationFormat is not 0 and not 1)
            {
                return null;
            }

            int count = ReadUInt16(maxp, 4);
            int requiredLocationBytes = checked((count + 1) * (locationFormat == 1 ? 4 : 2));
            return count > 0 && loca.Length >= requiredLocationBytes
                ? new(glyf, loca, locationFormat == 1, count)
                : null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    public bool TryRead(uint glyphId, out GlyphOutline? outline)
    {
        outline = null;
        if (glyphId >= glyphCount)
        {
            return false;
        }

        try
        {
            outline = Read(glyphId, 0, []);
            return outline is not null;
        }
        catch (InvalidDataException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private GlyphOutline? Read(uint glyphId, int depth, HashSet<uint> ancestors)
    {
        if (glyphId >= glyphCount || depth >= MaximumCompositeDepth || !ancestors.Add(glyphId))
        {
            throw new InvalidDataException("The TrueType composite glyph graph is invalid.");
        }

        try
        {
            (int start, int end) = GetGlyphRange(glyphId);
            if (end == start)
            {
                return null;
            }
            if (start < 0 || end < start || end > glyphTable.Length || end - start < 10)
            {
                throw new InvalidDataException("The TrueType glyph range is invalid.");
            }

            ReadOnlySpan<byte> data = glyphTable.AsSpan(start, end - start);
            short contourCount = ReadInt16(data, 0);
            return contourCount >= 0
                ? ReadSimple(data, contourCount)
                : ReadComposite(data, depth, ancestors);
        }
        finally
        {
            ancestors.Remove(glyphId);
        }
    }

    private (int Start, int End) GetGlyphRange(uint glyphId)
    {
        int index = checked((int)glyphId);
        int start;
        int end;
        if (usesLongLocations)
        {
            start = checked((int)ReadUInt32(locationTable, checked(index * 4)));
            end = checked((int)ReadUInt32(locationTable, checked((index + 1) * 4)));
        }
        else
        {
            start = checked(ReadUInt16(locationTable, checked(index * 2)) * 2);
            end = checked(ReadUInt16(locationTable, checked((index + 1) * 2)) * 2);
        }

        return (start, end);
    }

    private static GlyphOutline? ReadSimple(ReadOnlySpan<byte> data, int contourCount)
    {
        if (contourCount == 0)
        {
            return null;
        }

        int offset = 10;
        var contourEnds = new int[contourCount];
        int previousEnd = -1;
        for (int index = 0; index < contourEnds.Length; index++)
        {
            int end = ReadUInt16(data, offset);
            offset = checked(offset + 2);
            if (end <= previousEnd)
            {
                throw new InvalidDataException("TrueType contour endpoints must be strictly increasing.");
            }
            contourEnds[index] = end;
            previousEnd = end;
        }

        int pointCount = checked(contourEnds[^1] + 1);
        int instructionLength = ReadUInt16(data, offset);
        offset = checked(offset + 2 + instructionLength);
        EnsureAvailable(data, offset, 0);

        var flags = new byte[pointCount];
        for (int point = 0; point < pointCount;)
        {
            byte flag = ReadByte(data, ref offset);
            flags[point++] = flag;
            if ((flag & 0x08) == 0)
            {
                continue;
            }

            int repetitions = ReadByte(data, ref offset);
            if (repetitions > pointCount - point)
            {
                throw new InvalidDataException("A TrueType point flag run exceeds the glyph point count.");
            }
            for (int repeat = 0; repeat < repetitions; repeat++)
            {
                flags[point++] = flag;
            }
        }

        var points = new Vector2[pointCount];
        var onCurve = new bool[pointCount];
        int x = 0;
        for (int point = 0; point < pointCount; point++)
        {
            byte flag = flags[point];
            if ((flag & 0x02) != 0)
            {
                int delta = ReadByte(data, ref offset);
                x = checked(x + (((flag & 0x10) != 0) ? delta : -delta));
            }
            else if ((flag & 0x10) == 0)
            {
                x = checked(x + ReadInt16(data, offset));
                offset = checked(offset + 2);
            }

            points[point].X = x;
            onCurve[point] = (flag & 0x01) != 0;
        }

        int y = 0;
        for (int point = 0; point < pointCount; point++)
        {
            byte flag = flags[point];
            if ((flag & 0x04) != 0)
            {
                int delta = ReadByte(data, ref offset);
                y = checked(y + (((flag & 0x20) != 0) ? delta : -delta));
            }
            else if ((flag & 0x20) == 0)
            {
                y = checked(y + ReadInt16(data, offset));
                offset = checked(offset + 2);
            }

            points[point].Y = y;
        }

        return new(points, onCurve, contourEnds);
    }

    private GlyphOutline? ReadComposite(ReadOnlySpan<byte> data, int depth, HashSet<uint> ancestors)
    {
        const int ArgumentsAreWords = 0x0001;
        const int ArgumentsAreCoordinates = 0x0002;
        const int RoundCoordinates = 0x0004;
        const int HasUniformScale = 0x0008;
        const int HasMoreComponents = 0x0020;
        const int HasSeparateScales = 0x0040;
        const int HasMatrix = 0x0080;
        const int HasScaledOffset = 0x0800;

        var points = new List<Vector2>();
        var onCurve = new List<bool>();
        var contourEnds = new List<int>();
        int offset = 10;

        while (true)
        {
            int flags = ReadUInt16(data, offset);
            uint componentGlyph = ReadUInt16(data, checked(offset + 2));
            offset = checked(offset + 4);

            bool coordinates = (flags & ArgumentsAreCoordinates) != 0;
            int argument1;
            int argument2;
            if ((flags & ArgumentsAreWords) != 0)
            {
                argument1 = coordinates ? ReadInt16(data, offset) : ReadUInt16(data, offset);
                argument2 = coordinates
                    ? ReadInt16(data, checked(offset + 2))
                    : ReadUInt16(data, checked(offset + 2));
                offset = checked(offset + 4);
            }
            else
            {
                argument1 = coordinates ? unchecked((sbyte)ReadByte(data, ref offset)) : ReadByte(data, ref offset);
                argument2 = coordinates ? unchecked((sbyte)ReadByte(data, ref offset)) : ReadByte(data, ref offset);
            }

            float m11 = 1;
            float m12 = 0;
            float m21 = 0;
            float m22 = 1;
            if ((flags & HasUniformScale) != 0)
            {
                m11 = m22 = ReadF2Dot14(data, ref offset);
            }
            else if ((flags & HasSeparateScales) != 0)
            {
                m11 = ReadF2Dot14(data, ref offset);
                m22 = ReadF2Dot14(data, ref offset);
            }
            else if ((flags & HasMatrix) != 0)
            {
                m11 = ReadF2Dot14(data, ref offset);
                m12 = ReadF2Dot14(data, ref offset);
                m21 = ReadF2Dot14(data, ref offset);
                m22 = ReadF2Dot14(data, ref offset);
            }

            GlyphOutline? component = Read(componentGlyph, depth + 1, ancestors);
            if (component is not null)
            {
                ReadOnlySpan<Vector2> componentPoints = component.Points;
                var transformed = new Vector2[componentPoints.Length];
                for (int index = 0; index < transformed.Length; index++)
                {
                    Vector2 point = componentPoints[index];
                    transformed[index] = new(
                        m11 * point.X + m21 * point.Y,
                        m12 * point.X + m22 * point.Y);
                }

                Vector2 translation;
                if (coordinates)
                {
                    translation = new(argument1, argument2);
                    if ((flags & HasScaledOffset) != 0)
                    {
                        translation = new(
                            m11 * translation.X + m21 * translation.Y,
                            m12 * translation.X + m22 * translation.Y);
                    }
                    if ((flags & RoundCoordinates) != 0)
                    {
                        translation = new(MathF.Round(translation.X), MathF.Round(translation.Y));
                    }
                }
                else
                {
                    if ((uint)argument1 >= points.Count || (uint)argument2 >= transformed.Length)
                    {
                        throw new InvalidDataException("A TrueType composite glyph references an invalid attachment point.");
                    }
                    translation = points[argument1] - transformed[argument2];
                }

                int pointBase = points.Count;
                foreach (Vector2 point in transformed)
                {
                    points.Add(point + translation);
                }
                foreach (bool value in component.OnCurve)
                {
                    onCurve.Add(value);
                }
                foreach (int end in component.ContourEnds)
                {
                    contourEnds.Add(checked(pointBase + end));
                }
            }

            if ((flags & HasMoreComponents) == 0)
            {
                break;
            }
        }

        return points.Count == 0
            ? null
            : new(points.ToArray(), onCurve.ToArray(), contourEnds.ToArray());
    }

    private static float ReadF2Dot14(ReadOnlySpan<byte> data, ref int offset)
    {
        float result = ReadInt16(data, offset) / 16384f;
        offset = checked(offset + 2);
        return result;
    }

    private static byte[]? ReadTable(Face face, string name)
    {
        using Blob table = face.ReferenceTable(Tag.Parse(name));
        return table.Length == 0 ? null : table.AsSpan().ToArray();
    }

    private static byte ReadByte(ReadOnlySpan<byte> data, ref int offset)
    {
        EnsureAvailable(data, offset, 1);
        return data[offset++];
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset)
    {
        EnsureAvailable(data, offset, 2);
        return (ushort)((data[offset] << 8) | data[offset + 1]);
    }

    private static short ReadInt16(ReadOnlySpan<byte> data, int offset)
        => unchecked((short)ReadUInt16(data, offset));

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset)
    {
        EnsureAvailable(data, offset, 4);
        return ((uint)data[offset] << 24)
            | ((uint)data[offset + 1] << 16)
            | ((uint)data[offset + 2] << 8)
            | data[offset + 3];
    }

    private static void EnsureAvailable(ReadOnlySpan<byte> data, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > data.Length - length)
        {
            throw new InvalidDataException("The TrueType table is truncated.");
        }
    }
}
