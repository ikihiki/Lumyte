namespace Lumyte.Graphics.Text;

/// <summary>Evaluates COLR ItemVariationStore deltas at the default normalized coordinates.</summary>
internal sealed class ColrDefaultVariations2D(ColrTable2D table, int map, int store)
{
    private readonly Dictionary<uint, float> _cache = [];

    public float Delta(uint index)
    {
        if (index == uint.MaxValue || store == 0)
        {
            return 0;
        }

        if (_cache.TryGetValue(index, out float cached))
        {
            return cached;
        }

        uint mapped = Map(index);
        if (mapped == uint.MaxValue)
        {
            _cache.Add(index, 0);
            return 0;
        }

        int outer = checked((int)(mapped >> 16));
        int inner = (int)(mapped & 0xffff);
        if (table.U16(store) != 1 || outer >= table.U16(store + 6))
        {
            throw new InvalidDataException("The COLR variation store or outer index is invalid.");
        }

        int data = table.Offset32(store, store + 8 + (outer * 4));
        int itemCount = table.U16(data);
        int wordCountValue = table.U16(data + 2);
        int regionCount = table.U16(data + 4);
        bool longWords = (wordCountValue & 0x8000) != 0;
        int wordCount = wordCountValue & 0x7fff;
        if (inner >= itemCount || wordCount > regionCount)
        {
            throw new InvalidDataException("The COLR variation item is invalid.");
        }

        int rowLength = checked((wordCount * (longWords ? 4 : 2)) + ((regionCount - wordCount) * (longWords ? 2 : 1)));
        int values = checked(data + 6 + (regionCount * 2) + (inner * rowLength));
        table.Check(values, rowLength);
        int regions = table.Offset32(store, store + 2);
        int axes = table.U16(regions);
        int totalRegions = table.U16(regions + 2);
        double result = 0;
        for (int i = 0; i < regionCount; i++)
        {
            int regionIndex = table.U16(data + 6 + (i * 2));
            if (regionIndex >= totalRegions)
            {
                throw new InvalidDataException("The COLR variation region index is invalid.");
            }

            int value;
            if (i < wordCount)
            {
                value = longWords ? table.I32(values) : table.I16(values);
                values += longWords ? 4 : 2;
            }
            else
            {
                value = longWords ? table.I16(values) : unchecked((sbyte)table.U8(values));
                values += longWords ? 2 : 1;
            }

            double scalar = 1;
            for (int axis = 0; axis < axes; axis++)
            {
                int offset = checked(regions + 4 + (((regionIndex * axes) + axis) * 6));
                float start = table.F2(offset);
                float peak = table.F2(offset + 2);
                float end = table.F2(offset + 4);
                if (peak == 0 || start > peak || peak > end || (start < 0 && end > 0))
                {
                    continue;
                }

                if (start >= 0 || end <= 0)
                {
                    scalar = 0;
                    break;
                }

                scalar *= peak > 0 ? -start / (peak - start) : end / (end - peak);
            }

            result += value * scalar;
        }

        float delta = (float)result;
        if (!float.IsFinite(delta))
        {
            throw new InvalidDataException("The COLR variation delta is nonfinite.");
        }

        _cache.Add(index, delta);
        return delta;
    }

    private uint Map(uint index)
    {
        if (map == 0)
        {
            return index;
        }

        int format = table.U8(map);
        int entryFormat = table.U8(map + 1);
        if (format > 1 || (entryFormat & 0xc0) != 0)
        {
            throw new InvalidDataException("The COLR variation-index mapping format is invalid.");
        }

        uint count = format == 0 ? table.U16(map + 2) : table.U32(map + 2);
        if (count == 0)
        {
            throw new InvalidDataException("The COLR variation-index mapping is empty.");
        }

        uint item = Math.Min(index, count - 1);
        int entrySize = ((entryFormat >> 4) & 3) + 1;
        int innerBits = (entryFormat & 15) + 1;
        int offset = checked(map + (format == 0 ? 4 : 6) + checked((int)item * entrySize));
        uint value = 0;
        for (int i = 0; i < entrySize; i++)
        {
            value = (value << 8) | table.U8(offset + i);
        }

        uint innerMask = (1u << innerBits) - 1;
        uint outer = value >> innerBits;
        if (outer > ushort.MaxValue)
        {
            throw new InvalidDataException("The COLR variation outer index is too large.");
        }

        return (outer << 16) | (value & innerMask);
    }
}
