using System.Text;

namespace Lumyte.Maps.Sample;

internal static class LocalFixture
{
    // Entirely hand-authored geometry, not downloaded OSM data.
    internal static byte[] CreateTile() => [
        .. Layer("water", 3, [9, 256, 256, 26, 1536, 0, 0, 1536, 1535, 0, 15]),
        .. Layer("buildings", 3, [9, 512, 512, 26, 1024, 0, 0, 1024, 1023, 0, 15, 9, 256, 767, 26, 0, 512, 512, 0, 0, 511, 15]),
        .. Layer("roads", 2, [9, 0, 1024, 18, 1024, 0, 1024, 512]),
    ];

    private static byte[] Layer(string name, uint type, uint[] geometry)
    {
        byte[] feature = [24, .. Varint(type), .. Message(4, geometry.SelectMany(Varint).ToArray())];
        byte[] layer = [.. Message(1, Encoding.UTF8.GetBytes(name)), .. Message(2, feature), 40, .. Varint(1024), 120, 2];

        // Return an already wrapped layer; CreateTile concatenates these messages.
        return Message(3, layer);
    }

    private static byte[] Message(uint field, byte[] contents) => [.. Varint((field << 3) | 2), .. Varint((uint)contents.Length), .. contents];

    private static byte[] Varint(uint value)
    {
        var bytes = new List<byte>();
        do
        {
            byte next = (byte)(value & 127);
            value >>= 7;
            bytes.Add(value == 0 ? next : (byte)(next | 128));
        }
        while (value != 0);
        return bytes.ToArray();
    }
}
