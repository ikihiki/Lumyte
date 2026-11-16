using System.Buffers.Binary;

namespace Lumyte.Maps.Mvt;

internal ref struct ProtoReader(ReadOnlySpan<byte> bytes)
{
    private ReadOnlySpan<byte> _bytes = bytes;

    internal readonly bool End => _bytes.IsEmpty;

    internal ulong Varint()
    {
        ulong value = 0;
        for (int i = 0; i < 10; i++)
        {
            if (_bytes.IsEmpty)
            {
                throw new InvalidDataException("Truncated varint.");
            }

            byte current = _bytes[0];
            _bytes = _bytes[1..];
            if (i == 9 && current > 1)
            {
                throw new InvalidDataException("Varint overflow.");
            }

            value |= (ulong)(current & 127) << (i * 7);
            if ((current & 128) == 0)
            {
                return value;
            }
        }

        throw new InvalidDataException("Invalid varint.");
    }

    internal uint UInt32()
    {
        ulong value = Varint();
        return value <= uint.MaxValue ? (uint)value : throw new InvalidDataException("uint32 overflow.");
    }

    internal int Tag()
    {
        uint tag = UInt32();
        if (tag >> 3 == 0 || tag >> 3 > 536870911)
        {
            throw new InvalidDataException("Invalid field number.");
        }

        return unchecked((int)tag);
    }

    internal ReadOnlySpan<byte> Bytes()
    {
        ulong length = Varint();
        if (length > (ulong)_bytes.Length)
        {
            throw new InvalidDataException("Truncated message.");
        }

        ReadOnlySpan<byte> result = _bytes[..(int)length];
        _bytes = _bytes[(int)length..];
        return result;
    }

    internal ulong Fixed(int size)
    {
        if (_bytes.Length < size)
        {
            throw new InvalidDataException("Truncated fixed value.");
        }

        ulong value = size == 4 ? BinaryPrimitives.ReadUInt32LittleEndian(_bytes) : BinaryPrimitives.ReadUInt64LittleEndian(_bytes);
        _bytes = _bytes[size..];
        return value;
    }

    internal void Skip(int wire)
    {
        switch (wire)
        {
            case 0:
                Varint();
                break;
            case 1:
                Fixed(8);
                break;
            case 2:
                Bytes();
                break;
            case 5:
                Fixed(4);
                break;
            default:
                throw new InvalidDataException("Unsupported wire type.");
        }
    }
}
