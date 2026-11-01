using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.UI.SampleSupport;

internal static class UiSampleScreenshot
{
    internal static async ValueTask<byte[]> CaptureAsync(IGraphicDevice device, UiSurface surface, Vector2 scale, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        surface.UpdateLayout();
        var description = new TextureDesc
        {
            Width = checked((uint)Math.Ceiling(surface.RootElement.Size.X * scale.X)),
            Height = checked((uint)Math.Ceiling(surface.RootElement.Size.Y * scale.Y)),
            Format = TextureFormat.Rgba8Srgb,
            Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource,
        };
        using IGraphicsTexture target = device.CreateTexture(description);
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.Scale(scale);
        surface.Record(canvas);

        // Once GPU use begins, finish its uncancellable wait before observing request cancellation.
        byte[] pixels = await UiGraphExercise.DrawAsync(device, target, description, false, canvas.End(), null, null, false, new(0.04f, 0.04f, 0.06f, 1));
        cancellationToken.ThrowIfCancellationRequested();
        return Encode(description.Width, description.Height, pixels);
    }

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
