using System.Text;
using Lumyte.Maps.Mvt;
using Xunit;

namespace Lumyte.Maps.Tests;

/// <summary>Hand-authored protobuf fixtures; no network or OSM redistribution.</summary>
public sealed class MvtDecoderTests
{
    /// <summary>Preserves extent, typed tags and line buffer coordinates.</summary>
    /// <param name="packed">Whether repeated fields are packed.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LinesAndTypedAttributes(bool packed)
    {
        byte[] tile = Tile("roads", 8192, [9, 1, 0, 10, 22, 10], packed);
        MapTileContent content = MvtDecoder.Decode(tile, new("test"), "revision");
        MapTileLayer layer = Assert.Single(content.Layers);
        Assert.Equal(8192U, layer.Extent);
        MapTileFeature feature = Assert.Single(layer.Features);
        Assert.Equal(new MapPoint(-1, 0), feature.Paths[0].Points[0]);
        Assert.Equal(new MapPoint(10, 5), feature.Paths[0].Points[1]);
        Assert.Equal("primary", Assert.Single(feature.Attributes).Value);
        Assert.Equal("revision", content.SourceRevision);
        Assert.True(content.PeakBuildBytes > content.RetainedBytes);
        tile.AsSpan().Clear();
        Assert.Equal("primary", feature.Attributes[0].Value);
    }

    /// <summary>Preserves holes and multiple exterior rings in screen winding order.</summary>
    [Fact]
    public void PolygonHoleAndMultipolygon()
    {
        uint[] geometry = [9, 0, 0, 26, 20, 0, 0, 20, 19, 0, 15, 9, 4, 15, 26, 0, 12, 12, 0, 0, 11, 15, 9, 24, 4, 26, 20, 0, 0, 20, 19, 0, 15];
        MapTileContent content = MvtDecoder.Decode(Tile("buildings", 1024, geometry), new("test"));
        Assert.Equal([false, true, false], content.Layers[0].Features[0].Paths.Select(path => path.IsHole));
    }

    /// <summary>Rejects malformed command sequences.</summary>
    /// <param name="geometry">Malformed geometry.</param>
    [Theory]
    [InlineData(new uint[] { 9, 0 })]
    [InlineData(new uint[] { 9, 0, 0, 0 })]
    [InlineData(new uint[] { 9, 0, 0, 10, 0, 0 })]
    [InlineData(new uint[] { 17, 0, 0, 10, 2, 2 })]
    [InlineData(new uint[] { 9, 4294967294, 0, 10, 2, 0 })]
    public void RejectsCommands(uint[] geometry)
        => Assert.Throws<InvalidDataException>(() => MvtDecoder.Decode(Tile("roads", 4096, geometry), new("test")));

    /// <summary>Rejects all truncations within a single layer.</summary>
    [Fact]
    public void RejectsTruncationAndVarintOverflow()
    {
        byte[] tile = Tile("roads", 4096, [9, 0, 0, 10, 2, 2]);
        for (int i = 1; i < tile.Length; i++)
        {
            byte[] truncated = tile[..i];
            Assert.Throws<InvalidDataException>(() => MvtDecoder.Decode(truncated, new("test")));
        }

        Assert.Throws<InvalidDataException>(() => MvtDecoder.Decode([128], new("test")));
        Assert.Throws<InvalidDataException>(() => MvtDecoder.Decode([8, 255, 255, 255, 255, 255, 255, 255, 255, 255, 2], new("test")));
    }

    /// <summary>Checks each independent capacity and the exact reservation boundary.</summary>
    [Fact]
    public void EnforcesLimitsBeforeAllocation()
    {
        byte[] tile = Tile("roads", 4096, [9, 0, 0, 10, 2, 2]);
        Assert.Throws<InvalidDataException>(() => MvtDecoder.Decode(tile, new("test", maxInputBytes: 1)));
        Assert.Throws<InvalidDataException>(() => MvtDecoder.Decode(tile, new("test", maxDecodedBytes: 1)));
        Assert.Throws<InvalidDataException>(() => MvtDecoder.Decode(tile, new("test", maxAttributeBytes: 1)));
        Assert.Throws<InvalidDataException>(() => MvtDecoder.Decode(tile, new("test", maxVertices: 1)));
        byte[] twoFeatures = Tile("roads", 4096, [9, 0, 0, 10, 2, 2], featureCount: 2);
        Assert.Throws<InvalidDataException>(() => MvtDecoder.Decode(twoFeatures, new("test", maxFeatures: 1)));
        MapTileContent content = MvtDecoder.Decode(tile, new("test"));
        Assert.Equal(content.PeakBuildBytes, MvtDecoder.Decode(tile, new("test", maxReservationBytesPerTile: content.PeakBuildBytes)).PeakBuildBytes);
        Assert.Throws<InvalidDataException>(() => MvtDecoder.Decode(tile, new("test", maxReservationBytesPerTile: content.PeakBuildBytes - 1)));
    }

    /// <summary>Schema is explicit, not inferred from geometry.</summary>
    [Fact]
    public void RejectsUnknownSchemaAndInvalidTags()
    {
        Assert.Throws<InvalidDataException>(() => MvtDecoder.Decode(Tile("transportation", 4096, [9, 0, 0, 10, 2, 2]), new("test")));
        Assert.Throws<InvalidDataException>(() => MvtDecoder.Decode(Tile("roads", 0, [9, 0, 0, 10, 2, 2]), new("test")));
        Assert.Throws<InvalidDataException>(() => MvtDecoder.Decode(Tile("roads", 4096, [9, 0, 0, 10, 2, 2], tags: [0]), new("test")));
        Assert.Throws<InvalidDataException>(() => MvtDecoder.Decode(Tile("roads", 4096, [9, 0, 0, 10, 2, 2], tags: [1, 0]), new("test")));
    }

    /// <summary>Rejects winding without an exterior, missing closure and incorrect MVT versions.</summary>
    [Fact]
    public void RejectsPolygonWindingClosureAndVersion()
    {
        Assert.Throws<InvalidDataException>(() => MvtDecoder.Decode(Tile("water", 4096, [9, 0, 0, 18, 0, 20, 20, 0, 15]), new("test")));
        Assert.Throws<InvalidDataException>(() => MvtDecoder.Decode(Tile("water", 4096, [9, 0, 0, 18, 20, 0, 0, 20]), new("test")));
        byte[] tile = Tile("roads", 4096, [9, 0, 0, 10, 2, 2]);
        int version = Array.IndexOf(tile, (byte)120);
        tile[version + 1] = 1;
        Assert.Throws<InvalidDataException>(() => MvtDecoder.Decode(tile, new("test")));
    }

    /// <summary>Large attribute tables validate duplicate names through bounded scratch sorting, preserving source order.</summary>
    [Fact]
    public void LargeAttributeTableAndDuplicateAliases()
    {
        const int Count = 2048;
        uint[] tags = Enumerable.Range(0, Count).SelectMany(index => new[] { (uint)index, 0U }).ToArray();
        byte[] feature = Join(Scalar(3, 2), Repeated(2, tags, true), Repeated(4, [9, 0, 0, 10, 2, 2], true));
        byte[][] keys = Enumerable.Range(0, Count).Select(index => Field(3, Encoding.UTF8.GetBytes($"key-{index:D4}"))).ToArray();
        byte[] layer = Join([Field(1, "roads"u8.ToArray()), Scalar(15, 2), Field(4, Field(1, "value"u8.ToArray())), Field(2, feature), .. keys]);
        MapTileContent content = MvtDecoder.Decode(Field(3, layer), new("large-attributes"));
        Assert.Equal(Count, content.Layers[0].Features[0].Attributes.Length);
        Assert.Equal("key-0000", content.Layers[0].Features[0].Attributes[0].Name);
        Assert.Equal("key-2047", content.Layers[0].Features[0].Attributes[^1].Name);
        keys[^1] = keys[0];
        byte[] duplicate = Join([Field(1, "roads"u8.ToArray()), Scalar(15, 2), Field(4, Field(1, "value"u8.ToArray())), Field(2, feature), .. keys]);
        Assert.Throws<InvalidDataException>(() => MvtDecoder.Decode(Field(3, duplicate), new("large-attributes")));
    }

    /// <summary>Library subitems must reject crossing a parent boundary even with following bytes available.</summary>
    /// <param name="bytes">Malformed wire input.</param>
    [Theory]
    [InlineData(new byte[] { 26, 10 })]
    [InlineData(new byte[] { 26, 1, 120, 2 })]
    [InlineData(new byte[] { 26, 2, 13, 0, 0, 0, 0 })]
    [InlineData(new byte[] { 26, 2, 10, 5, 114, 111, 97, 100, 115 })]
    [InlineData(new byte[] { 24, 0 })]
    [InlineData(new byte[] { 11, 12 })]
    [InlineData(new byte[] { 12 })]
    [InlineData(new byte[] { 0 })]
    [InlineData(new byte[] { 26, 255, 255, 255, 255, 255, 255, 255, 255, 127 })]
    public void RejectsSubitemCrossingAndInvalidWire(byte[] bytes)
        => Assert.Throws<InvalidDataException>(() => MvtDecoder.Decode(bytes, new("wire")));

    /// <summary>Each MVT known field retains strict wire and range checks.</summary>
    [Fact]
    public void RejectsKnownWrongWireAndOverflow()
    {
        byte[] layer = Join(Field(1, "roads"u8.ToArray()), Scalar(15, 2));
        foreach (byte[] wrong in new byte[][] { Scalar(1, 1), Scalar(2, 1), Scalar(3, 1), Scalar(4, 1), Field(5, []), Field(15, []) })
        {
            Assert.Throws<InvalidDataException>(() => MvtDecoder.Decode(Field(3, Join(layer, wrong)), new("wire")));
        }

        // 2^32 must not be truncated to zero or an otherwise valid geometry type.
        byte[] overflow = [128, 128, 128, 128, 16];
        Assert.Throws<InvalidDataException>(() => MvtDecoder.Decode(Field(3, Join(layer, [40], overflow)), new("wire")));
        byte[] feature = Join(Scalar(3, 2), Field(4, [9, 0, 0, 10, 128, 128, 128, 128, 16, 0]));
        Assert.Throws<InvalidDataException>(() => MvtDecoder.Decode(Field(3, Join(layer, Field(2, feature))), new("wire")));

        // Packed uint32 continuation crosses into the following field.
        feature = Join(Scalar(3, 2), Field(4, [128]), Scalar(1, 0));
        Assert.Throws<InvalidDataException>(() => MvtDecoder.Decode(Field(3, Join(layer, Field(2, feature))), new("wire")));
    }

    /// <summary>Scratch is bounded independently from total UTF8 payload accounting and cannot replace invalid UTF8.</summary>
    [Fact]
    public void ScratchAndCumulativeAttributeLimits()
    {
        byte[] basis = Join(Field(1, "roads"u8.ToArray()), Scalar(15, 2));
        byte[] tile = Field(3, Join(basis, Field(3, new byte[32])));
        Assert.Throws<InvalidDataException>(() => MvtDecoder.Decode(tile, new("scratch", maxAttributeBytes: 8)));
        byte[] repeated = Field(3, Join(basis, Field(3, "abc"u8.ToArray()), Field(3, "def"u8.ToArray())));
        Assert.Throws<InvalidDataException>(() => MvtDecoder.Decode(repeated, new("total", maxAttributeBytes: 10)));
        MapTileContent content = MvtDecoder.Decode(repeated, new("exact", maxAttributeBytes: 11));
        Assert.Equal(content.PeakBuildBytes, MvtDecoder.Decode(repeated, new("exact", maxAttributeBytes: 11, maxReservationBytesPerTile: content.PeakBuildBytes)).PeakBuildBytes);
        Assert.Throws<InvalidDataException>(() => MvtDecoder.Decode(repeated, new("exact", maxAttributeBytes: 11, maxReservationBytesPerTile: content.PeakBuildBytes - 1)));
        Assert.Throws<InvalidDataException>(() => MvtDecoder.Decode(Field(3, Join(basis, Field(3, [192, 175]))), new("utf8")));
        Assert.Throws<InvalidDataException>(() => MvtDecoder.Decode(Field(3, Join(basis, Field(4, Field(1, [255])))), new("utf8")));
    }

    /// <summary>Public memory input borrows only the requested segment; arrays keep an unambiguous convenience entry.</summary>
    [Fact]
    public void DecodesMemorySegmentAndAllScalarTypes()
    {
        byte[][] values = [Field(1, "text"u8.ToArray()), Join([21], BitConverter.GetBytes(1.5f)), Join([25], BitConverter.GetBytes(2.5)), [32, 255, 255, 255, 255, 255, 255, 255, 255, 255, 1], [40, 42], [48, 3], [56, 1]];
        object[] expected = ["text", 1.5f, 2.5, -1L, 42UL, -2L, true];
        uint[] tags = Enumerable.Range(0, values.Length).SelectMany(index => new[] { (uint)index, (uint)index }).ToArray();
        byte[] feature = Join(Scalar(3, 2), Repeated(2, tags, true), Repeated(4, [9, 0, 0, 10, 2, 2], true));
        byte[] layer = Join([Field(1, "roads"u8.ToArray()), Scalar(15, 2), Field(2, feature), .. values.Select(value => Field(4, value)), .. Enumerable.Range(0, values.Length).Select(index => Field(3, Encoding.UTF8.GetBytes($"k{index}")))]);
        byte[] tile = Field(3, layer);
        byte[] padded = Join([255], tile, [255]);
        MapTileContent content = MvtDecoder.Decode(padded.AsMemory(1, tile.Length), new("memory"));
        Assert.Equal(expected, content.Layers[0].Features[0].Attributes.Select(attribute => attribute.Value));
        Assert.Throws<InvalidDataException>(() => MvtDecoder.Decode(Field(3, Join(Field(1, "roads"u8.ToArray()), Scalar(15, 2), Field(4, [56, 2]))), new("bool")));
    }

    internal static byte[] Tile(string name, uint extent, uint[] geometry, bool packed = true, int featureCount = 1, uint[]? tags = null)
    {
        byte[] feature = Join(Scalar(3, name == "roads" || name == "transportation" ? 2U : 3U), Repeated(2, tags ?? [0, 0], packed), Repeated(4, geometry, packed));
        byte[] layer = Join([Field(1, Encoding.UTF8.GetBytes(name)), Scalar(15, 2), Scalar(5, extent), Field(3, "class"u8.ToArray()), Field(4, Field(1, "primary"u8.ToArray())), .. Enumerable.Repeat(Field(2, feature), featureCount)]);
        return Field(3, layer);
    }

    private static byte[] Repeated(int field, uint[] values, bool packed)
        => packed ? Field(field, Join(values.Select(value => Varint(value)).ToArray())) : Join(values.Select(value => Scalar(field, value)).ToArray());

    private static byte[] Scalar(int field, uint value) => Join(Varint((uint)(field << 3)), Varint(value));

    private static byte[] Field(int field, byte[] bytes) => Join(Varint((uint)((field << 3) | 2)), Varint((uint)bytes.Length), bytes);

    private static byte[] Join(params IEnumerable<byte[]> arrays) => arrays.SelectMany(bytes => bytes).ToArray();

    private static byte[] Varint(uint value)
    {
        var bytes = new List<byte>();
        do
        {
            bytes.Add((byte)((value & 127) | (value > 127 ? 128U : 0)));
            value >>= 7;
        }
        while (value != 0);

        return bytes.ToArray();
    }
}
