using System.Buffers.Binary;
using System.IO.Compression;

namespace Lumyte.Graphics.Text;

/// <summary>Decodes the portable, non-interlaced PNG profiles used by OpenType bitmap glyphs.</summary>
internal static class PngDecoder
{
    private const ulong Signature = 0x89504E470D0A1A0A;
    private const int MaximumPixels = 64 * 1024 * 1024;

    internal static bool TryDecode(ReadOnlySpan<byte> source, out PngImage image)
    {
        try
        {
            image = Decode(source);
            return true;
        }
        catch (Exception exception) when (exception is IOException
            or ArgumentException
            or OverflowException)
        {
            image = default;
            return false;
        }
    }

    private static PngImage Decode(ReadOnlySpan<byte> source)
    {
        if (source.Length < sizeof(ulong)
            || BinaryPrimitives.ReadUInt64BigEndian(source) != Signature)
        {
            throw new InvalidDataException("The image does not have a PNG signature.");
        }

        uint width = 0;
        uint height = 0;
        byte bitDepth = 0;
        byte colorType = byte.MaxValue;
        byte[]? palette = null;
        byte[]? transparency = null;
        using var compressed = new MemoryStream();
        bool hasHeader = false;
        bool hasData = false;
        bool hasEnd = false;

        int offset = sizeof(ulong);
        while (offset <= source.Length - 12)
        {
            uint chunkLength = BinaryPrimitives.ReadUInt32BigEndian(source[offset..]);
            int dataLength = checked((int)chunkLength);
            int chunkEnd = checked(offset + 12 + dataLength);
            if (chunkEnd > source.Length)
            {
                throw new InvalidDataException("A PNG chunk extends beyond the image data.");
            }

            ReadOnlySpan<byte> type = source.Slice(offset + 4, 4);
            ReadOnlySpan<byte> data = source.Slice(offset + 8, dataLength);
            if (type.SequenceEqual("IHDR"u8))
            {
                if (hasHeader || data.Length != 13 || offset != sizeof(ulong))
                {
                    throw new InvalidDataException("The PNG header is invalid.");
                }
                width = BinaryPrimitives.ReadUInt32BigEndian(data);
                height = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
                bitDepth = data[8];
                colorType = data[9];
                if (width == 0 || height == 0
                    || checked((ulong)width * height) > MaximumPixels
                    || data[10] != 0
                    || data[11] != 0
                    || data[12] != 0)
                {
                    throw new InvalidDataException("The PNG dimensions or encoding method is unsupported.");
                }
                ValidatePixelFormat(colorType, bitDepth);
                hasHeader = true;
            }
            else if (type.SequenceEqual("PLTE"u8))
            {
                if (!hasHeader || hasData || data.Length == 0 || data.Length > 768 || data.Length % 3 != 0)
                {
                    throw new InvalidDataException("The PNG palette is invalid.");
                }
                palette = data.ToArray();
            }
            else if (type.SequenceEqual("tRNS"u8))
            {
                if (!hasHeader || hasData)
                {
                    throw new InvalidDataException("The PNG transparency table is misplaced.");
                }
                transparency = data.ToArray();
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                if (!hasHeader || hasEnd)
                {
                    throw new InvalidDataException("The PNG image data is misplaced.");
                }
                compressed.Write(data);
                hasData = true;
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                if (data.Length != 0 || !hasData)
                {
                    throw new InvalidDataException("The PNG end marker is invalid.");
                }
                hasEnd = true;
                break;
            }
            else if ((type[0] & 0x20) == 0)
            {
                throw new InvalidDataException("The PNG contains an unsupported critical chunk.");
            }

            offset = chunkEnd;
        }

        if (!hasHeader || !hasData || !hasEnd)
        {
            throw new InvalidDataException("The PNG image is incomplete.");
        }
        if (colorType == 3
            && (palette is null || palette.Length / 3 > 1 << bitDepth))
        {
            throw new InvalidDataException("The indexed PNG palette is missing or too large.");
        }
        ValidateTransparency(colorType, bitDepth, palette, transparency);

        int channels = Channels(colorType);
        int bitsPerPixel = checked(channels * bitDepth);
        int scanlineLength = checked((int)(((ulong)width * (uint)bitsPerPixel + 7) / 8));
        int filteredLength = checked((scanlineLength + 1) * (int)height);
        var filtered = new byte[filteredLength];
        compressed.Position = 0;
        using (var inflater = new ZLibStream(compressed, CompressionMode.Decompress, leaveOpen: true))
        {
            inflater.ReadExactly(filtered);
            if (inflater.ReadByte() != -1)
            {
                throw new InvalidDataException("The PNG expands beyond its declared dimensions.");
            }
        }

        int filterBytesPerPixel = Math.Max(1, (bitsPerPixel + 7) / 8);
        var scanlines = new byte[checked(scanlineLength * (int)height)];
        Unfilter(filtered, scanlines, scanlineLength, filterBytesPerPixel, height);
        byte[] pixels = ExpandPixels(
            scanlines,
            width,
            height,
            colorType,
            bitDepth,
            palette,
            transparency);
        return new(width, height, pixels);
    }

    private static void ValidatePixelFormat(byte colorType, byte bitDepth)
    {
        bool supported = colorType switch
        {
            0 => bitDepth is 1 or 2 or 4 or 8,
            2 => bitDepth == 8,
            3 => bitDepth is 1 or 2 or 4 or 8,
            4 => bitDepth == 8,
            6 => bitDepth == 8,
            _ => false,
        };
        if (!supported)
        {
            throw new InvalidDataException("The PNG pixel format is unsupported.");
        }
    }

    private static int Channels(byte colorType) => colorType switch
    {
        0 or 3 => 1,
        2 => 3,
        4 => 2,
        6 => 4,
        _ => throw new InvalidDataException("The PNG color type is unsupported."),
    };

    private static void ValidateTransparency(
        byte colorType,
        byte bitDepth,
        byte[]? palette,
        byte[]? transparency)
    {
        if (transparency is null)
        {
            return;
        }

        bool valid = colorType switch
        {
            0 => transparency.Length == 2
                && BinaryPrimitives.ReadUInt16BigEndian(transparency) < 1 << bitDepth,
            2 => transparency.Length == 6
                && BinaryPrimitives.ReadUInt16BigEndian(transparency) <= byte.MaxValue
                && BinaryPrimitives.ReadUInt16BigEndian(transparency.AsSpan(2)) <= byte.MaxValue
                && BinaryPrimitives.ReadUInt16BigEndian(transparency.AsSpan(4)) <= byte.MaxValue,
            3 => palette is not null && transparency.Length <= palette.Length / 3,
            _ => false,
        };
        if (!valid)
        {
            throw new InvalidDataException("The PNG transparency table is invalid for its color type.");
        }
    }

    private static void Unfilter(
        ReadOnlySpan<byte> filtered,
        Span<byte> destination,
        int rowLength,
        int bytesPerPixel,
        uint height)
    {
        int sourceOffset = 0;
        for (int row = 0; row < height; row++)
        {
            byte filter = filtered[sourceOffset++];
            Span<byte> current = destination.Slice(checked(row * rowLength), rowLength);
            filtered.Slice(sourceOffset, rowLength).CopyTo(current);
            sourceOffset += rowLength;
            ReadOnlySpan<byte> previous = row == 0
                ? ReadOnlySpan<byte>.Empty
                : destination.Slice(checked((row - 1) * rowLength), rowLength);
            for (int column = 0; column < current.Length; column++)
            {
                byte left = column >= bytesPerPixel ? current[column - bytesPerPixel] : (byte)0;
                byte above = previous.IsEmpty ? (byte)0 : previous[column];
                byte upperLeft = previous.IsEmpty || column < bytesPerPixel
                    ? (byte)0
                    : previous[column - bytesPerPixel];
                current[column] = filter switch
                {
                    0 => current[column],
                    1 => unchecked((byte)(current[column] + left)),
                    2 => unchecked((byte)(current[column] + above)),
                    3 => unchecked((byte)(current[column] + ((left + above) >> 1))),
                    4 => unchecked((byte)(current[column] + Paeth(left, above, upperLeft))),
                    _ => throw new InvalidDataException("The PNG uses an unknown scanline filter."),
                };
            }
        }
    }

    private static byte[] ExpandPixels(
        ReadOnlySpan<byte> source,
        uint width,
        uint height,
        byte colorType,
        byte bitDepth,
        byte[]? palette,
        byte[]? transparency)
    {
        var result = new byte[checked((int)(width * height * 4))];
        int rowLength = checked((int)(((ulong)width * (uint)(Channels(colorType) * bitDepth) + 7) / 8));
        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<byte> row = source.Slice(checked(y * rowLength), rowLength);
            for (int x = 0; x < width; x++)
            {
                int output = checked((y * (int)width + x) * 4);
                switch (colorType)
                {
                    case 0:
                        int graySample = ReadPackedSample(row, x, bitDepth);
                        byte gray = ExpandSample(graySample, bitDepth);
                        result[output] = gray;
                        result[output + 1] = gray;
                        result[output + 2] = gray;
                        result[output + 3] = transparency is not null
                            && graySample == BinaryPrimitives.ReadUInt16BigEndian(transparency)
                                ? (byte)0
                                : (byte)255;
                        break;
                    case 2:
                        row.Slice(x * 3, 3).CopyTo(result.AsSpan(output, 3));
                        result[output + 3] = transparency is not null
                            && row[x * 3] == BinaryPrimitives.ReadUInt16BigEndian(transparency)
                            && row[x * 3 + 1] == BinaryPrimitives.ReadUInt16BigEndian(transparency.AsSpan(2))
                            && row[x * 3 + 2] == BinaryPrimitives.ReadUInt16BigEndian(transparency.AsSpan(4))
                                ? (byte)0
                                : (byte)255;
                        break;
                    case 3:
                        int paletteIndex = ReadPackedSample(row, x, bitDepth);
                        int paletteOffset = checked(paletteIndex * 3);
                        if (palette is null || paletteOffset + 3 > palette.Length)
                        {
                            throw new InvalidDataException("A PNG pixel references a missing palette entry.");
                        }
                        palette.AsSpan(paletteOffset, 3).CopyTo(result.AsSpan(output, 3));
                        result[output + 3] = transparency is not null && paletteIndex < transparency.Length
                            ? transparency[paletteIndex]
                            : (byte)255;
                        break;
                    case 4:
                        result[output] = row[x * 2];
                        result[output + 1] = row[x * 2];
                        result[output + 2] = row[x * 2];
                        result[output + 3] = row[x * 2 + 1];
                        break;
                    case 6:
                        row.Slice(x * 4, 4).CopyTo(result.AsSpan(output, 4));
                        break;
                }
            }
        }
        return result;
    }

    private static int ReadPackedSample(ReadOnlySpan<byte> row, int x, int bitDepth)
    {
        if (bitDepth == 8)
        {
            return row[x];
        }
        int bitOffset = checked(x * bitDepth);
        int shift = 8 - bitDepth - bitOffset % 8;
        return (row[bitOffset / 8] >> shift) & ((1 << bitDepth) - 1);
    }

    private static byte ExpandSample(int value, int bitDepth)
        => checked((byte)(value * 255 / ((1 << bitDepth) - 1)));

    private static byte Paeth(byte left, byte above, byte upperLeft)
    {
        int estimate = left + above - upperLeft;
        int leftDistance = Math.Abs(estimate - left);
        int aboveDistance = Math.Abs(estimate - above);
        int upperLeftDistance = Math.Abs(estimate - upperLeft);
        return leftDistance <= aboveDistance && leftDistance <= upperLeftDistance
            ? left
            : aboveDistance <= upperLeftDistance ? above : upperLeft;
    }
}

internal readonly record struct PngImage(uint Width, uint Height, byte[] Pixels);
