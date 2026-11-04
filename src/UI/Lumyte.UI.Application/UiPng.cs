using System.Buffers.Binary;
using System.IO.Compression;

namespace Lumyte.UI.Application;

internal static class UiPng
{
    internal static byte[] Encode(uint width, uint height, byte[] pixels)
    {
        using var output = new MemoryStream();
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        Span<byte> header = stackalloc byte[13];
        header.Clear();
        BinaryPrimitives.WriteUInt32BigEndian(header, width);
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], height);
        header[8] = 8;
        header[9] = 6;
        Chunk(output, "IHDR"u8, header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            for (int row = 0; row < height; row++)
            {
                zlib.WriteByte(0);
                zlib.Write(pixels.AsSpan(checked(row * (int)width * 4), checked((int)width * 4)));
            }
        }

        Chunk(output, "IDAT"u8, compressed.ToArray());
        Chunk(output, "IEND"u8, []);
        return output.ToArray();
    }

    private static void Chunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> content)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(number, checked((uint)content.Length));
        output.Write(number);
        output.Write(type);
        output.Write(content);
        uint crc = Update(uint.MaxValue, type);
        crc = Update(crc, content);
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc);
        output.Write(number);

        static uint Update(uint crc, ReadOnlySpan<byte> bytes)
        {
            foreach (byte value in bytes)
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++)
                {
                    crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320);
                }
            }

            return crc;
        }
    }
}
