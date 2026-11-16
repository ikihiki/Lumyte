using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;

namespace Lumyte.Maps.Mvt;

/// <summary>Strict MVT 2 decoder for the explicit roads/buildings/water schema.</summary>
public static class MvtDecoder
{
    /// <summary>The supported application schema, not a general OSM convention.</summary>
    public const string SchemaId = "lumyte.roads-buildings-water.v1";

    private static readonly UTF8Encoding _utf8 = new(false, true);

    /// <summary>Decodes uncompressed bounded input, retaining no input references.</summary>
    /// <param name="input">An uncompressed MVT tile.</param>
    /// <param name="profile">Immutable registered limits.</param>
    /// <param name="sourceRevision">Observed snapshot revision.</param>
    /// <returns>Immutable CPU content.</returns>
    public static MapTileContent Decode(ReadOnlySpan<byte> input, MapTileLoadProfile profile, string sourceRevision = "")
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(sourceRevision);
        var budget = new DecodeBudget(profile, input.Length);
        budget.Charge(256, false);
        budget.Charge(StringBytes(sourceRevision.Length));
        int count = Count(input, 26);
        MapTileLayer[] layers = budget.Array<MapTileLayer>(count, 8);
        var reader = new ProtoReader(input);
        int index = 0;
        while (!reader.End)
        {
            int tag = reader.Tag();
            if (tag == 26)
            {
                MapTileLayer layer = Layer(reader.Bytes(), budget);
                for (int i = 0; i < index; i++)
                {
                    Require(layers[i].Name != layer.Name, "Duplicate layer name.");
                }

                layers[index++] = layer;
            }
            else
            {
                reader.Skip(tag & 7);
            }
        }

        budget.Charge(48);
        return new MapTileContent(ImmutableCollectionsMarshal.AsImmutableArray(layers), sourceRevision, budget.Retained, budget.Total + input.Length);
    }

    private static MapTileLayer Layer(ReadOnlySpan<byte> bytes, DecodeBudget budget)
    {
        string? name = null;
        uint extent = 4096;
        bool hasExtent = false;
        bool version = false;
        int featureCount = Count(bytes, 18);
        int keyCount = Count(bytes, 26);
        int valueCount = Count(bytes, 34);
        budget.Features(featureCount);
        string[] keys = budget.Array<string>(keyCount, 8, false);
        object[] values = budget.Array<object>(valueCount, 8, false);
        MapTileFeature[] features = budget.Array<MapTileFeature>(featureCount, 8);
        int keyIndex = 0;
        int valueIndex = 0;
        var reader = new ProtoReader(bytes);
        while (!reader.End)
        {
            int tag = reader.Tag();
            switch (tag)
            {
                case 10:
                    Require(name is null, "Duplicate layer name field.");
                    name = String(reader.Bytes(), budget);
                    break;
                case 18:
                    reader.Bytes();
                    break;
                case 26:
                    keys[keyIndex++] = String(reader.Bytes(), budget);
                    break;
                case 34:
                    values[valueIndex++] = Value(reader.Bytes(), budget);
                    break;
                case 40:
                    Require(!hasExtent, "Duplicate extent.");
                    extent = reader.UInt32();
                    hasExtent = true;
                    break;
                case 120:
                    Require(!version && reader.UInt32() == 2, "MVT version must be 2.");
                    version = true;
                    break;
                default:
                    RejectKnownWire(tag, 1, 2, 3, 4, 5, 15);
                    reader.Skip(tag & 7);
                    break;
            }
        }

        Require(version && extent > 0 && name is "roads" or "buildings" or "water", "Unsupported layer schema, extent or version.");
        reader = new ProtoReader(bytes);
        int featureIndex = 0;
        while (!reader.End)
        {
            int tag = reader.Tag();
            if (tag == 18)
            {
                features[featureIndex++] = Feature(reader.Bytes(), keys, values, name!, budget);
            }
            else
            {
                reader.Skip(tag & 7);
            }
        }

        budget.Charge(40);
        return new MapTileLayer(name!, extent, ImmutableCollectionsMarshal.AsImmutableArray(features));
    }

    private static MapTileFeature Feature(ReadOnlySpan<byte> bytes, string[] keys, object[] values, string layer, DecodeBudget budget)
    {
        ulong? id = null;
        uint type = 0;
        bool hasType = false;
        int tagCount = RepeatedCount(bytes, 2);
        int geometryCount = RepeatedCount(bytes, 4);
        Require(tagCount % 2 == 0, "Odd tag index count.");
        uint[] tags = budget.Array<uint>(tagCount, 4, false);
        uint[] geometry = budget.Array<uint>(geometryCount, 4, false);
        int ti = 0;
        int gi = 0;
        var reader = new ProtoReader(bytes);
        while (!reader.End)
        {
            int tag = reader.Tag();
            switch (tag)
            {
                case 8:
                    Require(id is null, "Duplicate feature ID.");
                    id = reader.Varint();
                    break;
                case 24:
                    Require(!hasType, "Duplicate geometry type.");
                    type = reader.UInt32();
                    hasType = true;
                    break;
                case 16:
                case 18:
                    Repeated(ref reader, tag, tags, ref ti);
                    break;
                case 32:
                case 34:
                    Repeated(ref reader, tag, geometry, ref gi);
                    break;
                default:
                    RejectKnownWire(tag, 1, 2, 3, 4);
                    reader.Skip(tag & 7);
                    break;
            }
        }

        Require(hasType && (layer == "roads" ? type == 2 : type == 3), "Layer geometry does not match schema.");
        MapTileAttribute[] attributes = budget.Array<MapTileAttribute>(tagCount / 2, 16);
        string[] attributeNames = budget.Array<string>(attributes.Length, 8, false);
        for (int i = 0; i < attributes.Length; i++)
        {
            uint key = tags[i * 2];
            uint value = tags[(i * 2) + 1];
            Require(key < keys.Length && value < values.Length, "Tag index out of range.");
            attributeNames[i] = keys[key];
            attributes[i] = new MapTileAttribute(keys[key], values[value]);
        }

        Array.Sort(attributeNames, StringComparer.Ordinal);
        for (int i = 1; i < attributeNames.Length; i++)
        {
            Require(attributeNames[i - 1] != attributeNames[i], "Duplicate feature attribute.");
        }

        ImmutableArray<MapTilePath> paths = Geometry(geometry, (MapGeometryType)type, budget);
        budget.Charge(56);
        return new MapTileFeature(id, (MapGeometryType)type, paths, ImmutableCollectionsMarshal.AsImmutableArray(attributes));
    }

    private static ImmutableArray<MapTilePath> Geometry(uint[] commands, MapGeometryType type, DecodeBudget budget)
    {
        int pathCount = Walk(commands, type, null, budget);
        MapTilePath[] paths = budget.Array<MapTilePath>(pathCount, 8);
        Walk(commands, type, paths, budget);
        return ImmutableCollectionsMarshal.AsImmutableArray(paths);
    }

    private static int Walk(uint[] commands, MapGeometryType type, MapTilePath[]? paths, DecodeBudget budget)
    {
        int cursor = 0;
        int x = 0;
        int y = 0;
        int pathIndex = 0;
        while (cursor < commands.Length)
        {
            Require(commands[cursor++] == 9, "Each path starts with one MoveTo.");
            ReadPoint(commands, ref cursor, ref x, ref y);
            MapPoint first = new(x, y);
            Require(cursor < commands.Length, "Missing LineTo.");
            uint line = commands[cursor++];
            uint count = line >> 3;
            Require((line & 7) == 2 && count > 0 && count <= (commands.Length - cursor) / 2, "Invalid LineTo count.");
            int vertices = checked((int)count + 1);
            if (paths is null)
            {
                budget.Vertices(vertices);
            }

            MapPoint[]? points = paths is null ? null : budget.Array<MapPoint>(vertices, 8);
            if (points is not null)
            {
                points[0] = first;
            }

            Int128 area = 0;
            MapPoint previous = first;
            for (int i = 1; i < vertices; i++)
            {
                ReadPoint(commands, ref cursor, ref x, ref y);
                MapPoint point = new(x, y);
                Require(point != previous, "Zero length segment.");
                area += ((Int128)previous.X * point.Y) - ((Int128)point.X * previous.Y);
                previous = point;
                if (points is not null)
                {
                    points[i] = point;
                }
            }

            bool hole = false;
            if (type == MapGeometryType.Polygon)
            {
                Require(vertices >= 3 && previous != first && cursor < commands.Length && commands[cursor++] == 15, "Invalid polygon closure.");
                area += ((Int128)previous.X * first.Y) - ((Int128)first.X * previous.Y);
                Require(area != 0, "Degenerate ring.");
                hole = area < 0;
                Require(!hole || pathIndex > 0, "Interior ring precedes exterior.");
            }

            if (paths is not null)
            {
                budget.Charge(32);
                paths[pathIndex] = new MapTilePath(ImmutableCollectionsMarshal.AsImmutableArray(points!), hole);
            }

            pathIndex++;
        }

        Require(pathIndex > 0, "Empty geometry.");
        return pathIndex;
    }

    private static void ReadPoint(uint[] commands, ref int cursor, ref int x, ref int y)
    {
        Require(cursor <= commands.Length - 2, "Truncated point.");
        uint dx = commands[cursor++];
        uint dy = commands[cursor++];
        long nextX = (long)x + (int)((dx >> 1) ^ (uint)-(int)(dx & 1));
        long nextY = (long)y + (int)((dy >> 1) ^ (uint)-(int)(dy & 1));
        Require(nextX is >= int.MinValue and <= int.MaxValue && nextY is >= int.MinValue and <= int.MaxValue, "Coordinate overflow.");
        x = (int)nextX;
        y = (int)nextY;
    }

    private static object Value(ReadOnlySpan<byte> bytes, DecodeBudget budget)
    {
        var reader = new ProtoReader(bytes);
        object? value = null;
        while (!reader.End)
        {
            int tag = reader.Tag();
            Require(value is null, "MVT value must have exactly one typed field.");
            budget.Charge(24);
            value = tag switch
            {
                10 => String(reader.Bytes(), budget),
                21 => BitConverter.UInt32BitsToSingle((uint)reader.Fixed(4)),
                25 => BitConverter.UInt64BitsToDouble(reader.Fixed(8)),
                32 => unchecked((long)reader.Varint()),
                40 => reader.Varint(),
                48 => ZigZag(reader.Varint()),
                56 => Boolean(reader.Varint()),
                _ => throw new InvalidDataException("Unknown attribute value type."),
            };
        }

        return value ?? throw new InvalidDataException("Empty attribute value.");
    }

    private static long ZigZag(ulong value) => (long)((value >> 1) ^ (ulong)-(long)(value & 1));

    private static bool Boolean(ulong value) => value <= 1 ? value != 0 : throw new InvalidDataException("Invalid bool.");

    private static string String(ReadOnlySpan<byte> bytes, DecodeBudget budget)
    {
        budget.Attributes(bytes.Length);
        int chars;
        try
        {
            chars = _utf8.GetCharCount(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("Invalid UTF8.", exception);
        }

        budget.Charge(StringBytes(chars));
        return _utf8.GetString(bytes);
    }

    private static long StringBytes(int chars) => (26L + (chars * 2L) + 7) & ~7L;

    private static int Count(ReadOnlySpan<byte> bytes, int expected)
    {
        var reader = new ProtoReader(bytes);
        int count = 0;
        while (!reader.End)
        {
            int tag = reader.Tag();
            if (tag == expected)
            {
                count++;
            }

            reader.Skip(tag & 7);
        }

        return count;
    }

    private static int RepeatedCount(ReadOnlySpan<byte> bytes, int field)
    {
        var reader = new ProtoReader(bytes);
        int count = 0;
        while (!reader.End)
        {
            int tag = reader.Tag();
            if (tag == (field << 3))
            {
                reader.UInt32();
                count++;
            }
            else if (tag == ((field << 3) | 2))
            {
                var packed = new ProtoReader(reader.Bytes());
                while (!packed.End)
                {
                    packed.UInt32();
                    count++;
                }
            }
            else
            {
                reader.Skip(tag & 7);
            }
        }

        return count;
    }

    private static void Repeated(ref ProtoReader reader, int tag, uint[] target, ref int index)
    {
        if ((tag & 7) == 0)
        {
            target[index++] = reader.UInt32();
        }
        else
        {
            var packed = new ProtoReader(reader.Bytes());
            while (!packed.End)
            {
                target[index++] = packed.UInt32();
            }
        }
    }

    private static void RejectKnownWire(int tag, params ReadOnlySpan<int> fields)
        => Require(!fields.Contains(tag >> 3), "Wrong wire type for known field.");

    private static void Require(bool valid, string message)
    {
        if (!valid)
        {
            throw new InvalidDataException(message);
        }
    }
}
