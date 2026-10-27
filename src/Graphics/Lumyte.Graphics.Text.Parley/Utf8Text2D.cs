using System.Text;

namespace Lumyte.Graphics.Text.Parley;

internal sealed class Utf8Text2D
{
    private static readonly UTF8Encoding _strictEncoding = new(false, true);
    private readonly int[] _utf16ToUtf8;
    private readonly int[] _utf8ToUtf16;

    public Utf8Text2D(string text)
    {
        if (_strictEncoding.GetByteCount(text) > 1024 * 1024)
        {
            throw new ArgumentException("Text exceeds the one MiB UTF-8 paragraph limit.", nameof(text));
        }

        Bytes = _strictEncoding.GetBytes(text);
        _utf16ToUtf8 = new int[text.Length + 1];
        _utf8ToUtf16 = new int[Bytes.Length + 1];
        Array.Fill(_utf16ToUtf8, -1);
        Array.Fill(_utf8ToUtf16, -1);
        int utf16 = 0;
        int utf8 = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            _utf16ToUtf8[utf16] = utf8;
            _utf8ToUtf16[utf8] = utf16;
            utf16 += rune.Utf16SequenceLength;
            utf8 += rune.Utf8SequenceLength;
        }

        _utf16ToUtf8[utf16] = utf8;
        _utf8ToUtf16[utf8] = utf16;
    }

    public byte[] Bytes { get; }

    public static uint FontTag(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        if (tag.Length != 4 || tag.Any(character => character is < ' ' or > '~'))
        {
            throw new ArgumentException("OpenType tags must contain four printable ASCII characters.", nameof(tag));
        }

        return ((uint)tag[0] << 24) | ((uint)tag[1] << 16) | ((uint)tag[2] << 8) | tag[3];
    }

    public int Utf8Offset(int utf16)
    {
        if ((uint)utf16 >= (uint)_utf16ToUtf8.Length || _utf16ToUtf8[utf16] < 0)
        {
            throw new ArgumentException("Text-run boundaries must be valid UTF-16 indices and must not split surrogate pairs.");
        }

        return _utf16ToUtf8[utf16];
    }

    public int Utf16Offset(uint utf8)
    {
        if (utf8 >= _utf8ToUtf16.Length || _utf8ToUtf16[utf8] < 0)
        {
            throw new InvalidDataException("The layout backend returned a source cluster outside a Unicode scalar boundary.");
        }

        return _utf8ToUtf16[utf8];
    }
}
