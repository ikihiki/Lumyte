using System.Buffers.Binary;
using System.IO.Compression;

namespace Lumyte.UI.Application.Desktop.Benchmark.HostCompletedFrame;

/// <summary>Independent analytic probes for the public host's unfiltered RGBA PNG, outside timed work.</summary>
public static class PresentationPixels
{
    /// <summary>Checks every rectangle center and an untouched opaque white background pixel.</summary>
    /// <param name="png">Actual composed presentation PNG.</param>
    /// <param name="green">Expected independent rectangle states.</param>
    /// <param name="narrow">Whether the first row is two pixels narrower after a local width signal change.</param>
    /// <param name="expectedWidth">The actual public window snapshot width, independently recorded before rendering.</param>
    /// <param name="expectedHeight">The actual public window snapshot height, independently recorded before rendering.</param>
    public static void Verify(byte[] png, IReadOnlyList<bool> green, bool narrow = false, int expectedWidth = 320, int expectedHeight = 400)
    {
        if (png.Length < 33 || !png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            throw new InvalidDataException("Expected a PNG from the real host.");
        }

        int width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4));
        int height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));
        if (width != expectedWidth || height != expectedHeight || png[24] != 8 || png[25] != 6)
        {
            throw new InvalidDataException($"Expected actual snapshot {expectedWidth}x{expectedHeight}, scale-one RGBA8 presentation; got {width}x{height}, bitDepth={png[24]}, colorType={png[25]}.");
        }

        using var compressed = new MemoryStream();
        for (int offset = 8; offset < png.Length;)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, 4));
            if (png.AsSpan(offset + 4, 4).SequenceEqual("IDAT"u8))
            {
                compressed.Write(png, offset + 8, length);
            }

            offset = checked(offset + length + 12);
        }

        compressed.Position = 0;
        using var decoder = new ZLibStream(compressed, CompressionMode.Decompress);
        byte[] pixels = new byte[checked(height * ((width * 4) + 1))];
        decoder.ReadExactly(pixels);
        for (int index = 0; index < green.Count; index++)
        {
            Check(((index % 25) * 8) + 3, ((index / 25) * 8) + 3, green[index] ? (byte)0 : (byte)255, green[index] ? (byte)255 : (byte)0, 0);
        }

        Check(300, 380, 255, 255, 255);
        Check(199, 3, narrow || !green[24] ? (byte)255 : (byte)0, narrow || green[24] ? (byte)255 : (byte)0, narrow ? (byte)255 : (byte)0);

        void Check(int x, int y, byte red, byte greenChannel, byte blue)
        {
            int row = y * ((width * 4) + 1);
            int pixel = row + 1 + (x * 4);
            if (pixels[row] != 0 || pixels[pixel] != red || pixels[pixel + 1] != greenChannel || pixels[pixel + 2] != blue || pixels[pixel + 3] != 255)
            {
                throw new InvalidDataException($"Analytic presentation pixel mismatch at ({x},{y}).");
            }
        }
    }
}
