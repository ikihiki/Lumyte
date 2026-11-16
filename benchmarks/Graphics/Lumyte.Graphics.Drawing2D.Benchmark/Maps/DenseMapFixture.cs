using System.Security.Cryptography;
using System.Text;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Maps;
using Lumyte.Maps.Mvt;

#if M6_GPU
namespace Lumyte.Benchmarks.GpuMaps;
#else
namespace Lumyte.Benchmarks.Maps;
#endif

/// <summary>Handmade, label-free MVT 2 city; no OSM or network data.</summary>
public static class DenseMapFixture
{
    /// <summary>The fixed synthetic building count.</summary>
    public const int Buildings = 256;

    /// <summary>The immutable decoder limit profile.</summary>
    public static readonly MapTileLoadProfile Profile = new("m6-dense-v1");

    /// <summary>The synthetic XYZ tile identity.</summary>
    public static readonly MapTileId Tile = new(1, 1, 1);

    /// <summary>The centered baseline camera.</summary>
    public static readonly MapCamera Camera = new(90, -66.51326044311186, 1);

    /// <summary>Gets the SHA256 of the generated MVT bytes.</summary>
    public static string Hash => Convert.ToHexString(SHA256.HashData(CreateBytes()));

    /// <summary>Generates the synthetic MVT 2 bytes without external input.</summary>
    /// <returns>The deterministic fixture value.</returns>
    public static byte[] CreateBytes()
    {
        var tile = new List<byte>();
        foreach (string name in new[] { "water", "buildings", "roads" })
        {
            var layer = new List<byte>();
            Field(layer, 1, Encoding.UTF8.GetBytes(name));
            Scalar(layer, 15, 2);
            Scalar(layer, 5, 4096);
            int count = name == "buildings" ? Buildings : name == "roads" ? 32 : 1;
            for (int i = 0; i < count; i++)
            {
                int x = name == "buildings" ? 64 + ((i % 16) * 248) : 0;
                int y = name == "buildings" ? 64 + (i / 16 * 248) : name == "roads" ? 32 + (i * 126) : 0;
                int width = name == "buildings" ? 160 : 4096;
                int height = name == "buildings" ? 160 : 384;
                var geometry = new List<byte>();
                foreach (uint value in name == "roads"
                    ? new uint[] { 9, Zig(x), Zig(y), 10, Zig(width), 0 }
                    : new uint[] { 9, Zig(x), Zig(y), 26, Zig(width), 0, 0, Zig(height), Zig(-width), 0, 15 })
                {
                    Varint(geometry, value);
                }

                var feature = new List<byte>();
                Scalar(feature, 1, (uint)i + 1);
                Scalar(feature, 3, name == "roads" ? 2U : 3U);
                Field(feature, 4, geometry.ToArray());
                Field(layer, 2, feature.ToArray());
            }

            Field(tile, 3, layer.ToArray());
        }

        return tile.ToArray();
    }

    /// <summary>Creates the fixed no-label painter order and line quality.</summary>
    /// <returns>The deterministic fixture value.</returns>
    public static MapStyle Style() => new(MvtDecoder.SchemaId, "m6-v1", new(0, 0, 0, 1), [new("water", new(0, 0, 1, 1)), new("buildings", new(1, 0, 0, 1)), new("roads", new(1, 1, 1, 1), new StrokeStyle2D { Width = 2 })]);

    private static uint Zig(int value) => unchecked((uint)((value << 1) ^ (value >> 31)));

    private static void Scalar(List<byte> output, int field, uint value)
    {
        Varint(output, (uint)(field << 3));
        Varint(output, value);
    }

    private static void Field(List<byte> output, int field, byte[] bytes)
    {
        Varint(output, (uint)((field << 3) | 2));
        Varint(output, (uint)bytes.Length);
        output.AddRange(bytes);
    }

    private static void Varint(List<byte> output, uint value)
    {
        do
        {
            output.Add((byte)((value & 127) | (value > 127 ? 128U : 0)));
            value >>= 7;
        }
        while (value != 0);
    }
}
