using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Lumyte.Resources.Gltf.Sample;

internal static class PngFile
{
    internal static void Write(string path, int width, int height, ReadOnlySpan<byte> rgba)
    {
        using FileStream file = File.Create(path);
        file.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
        header[8] = 8;
        header[9] = 6;
        header[10..].Clear();
        Chunk(file, "IHDR", header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, true))
        {
            for (int y = 0; y < height; y++)
            {
                zlib.WriteByte(0);
                zlib.Write(rgba.Slice(y * width * 4, width * 4));
            }
        }

        Chunk(file, "IDAT", compressed.ToArray());
        Chunk(file, "IEND", []);
    }

    private static void Chunk(Stream file, string name, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        file.Write(number);
        byte[] type = Encoding.ASCII.GetBytes(name);
        file.Write(type);
        file.Write(data);
        uint crc = uint.MaxValue;
        foreach (byte value in type)
        {
            crc = Update(crc, value);
        }

        foreach (byte value in data)
        {
            crc = Update(crc, value);
        }

        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc);
        file.Write(number);
    }

    private static uint Update(uint crc, byte value)
    {
        crc ^= value;
        for (int bit = 0; bit < 8; bit++)
        {
            crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
        }

        return crc;
    }
}
