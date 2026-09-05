using Lumyte.Graphics.TwoD;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Lumyte.Graphics.Text;

/// <summary>An sRGB color bitmap and its baseline-relative bounds in font units.</summary>
internal sealed class ColorBitmapGlyph
{
    internal ColorBitmapGlyph(uint width, uint height, byte[] pixels, Rect bounds)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
        Bounds = bounds;
        ContentId = ColorBitmapContentId.Create(pixels);
    }

    internal uint Width { get; }
    internal uint Height { get; }
    internal ReadOnlyMemory<byte> Pixels { get; }
    internal Rect Bounds { get; }
    internal ColorBitmapContentId ContentId { get; }
}

internal readonly record struct ColorBitmapContentId(ulong A, ulong B, ulong C, ulong D)
{
    internal static ColorBitmapContentId Create(ReadOnlySpan<byte> pixels)
    {
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(pixels, hash);
        return new(
            BinaryPrimitives.ReadUInt64LittleEndian(hash),
            BinaryPrimitives.ReadUInt64LittleEndian(hash[8..]),
            BinaryPrimitives.ReadUInt64LittleEndian(hash[16..]),
            BinaryPrimitives.ReadUInt64LittleEndian(hash[24..]));
    }
}
