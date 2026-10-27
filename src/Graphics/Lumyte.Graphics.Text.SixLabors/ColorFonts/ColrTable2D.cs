using System.Buffers.Binary;

namespace Lumyte.Graphics.Text;

internal sealed class ColrTable2D(byte[] bytes)
{
    public int Length => bytes.Length;

    public byte U8(int offset)
    {
        Check(offset, 1);
        return bytes[offset];
    }

    public ushort U16(int offset)
    {
        Check(offset, 2);
        return BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset));
    }

    public short I16(int offset) => unchecked((short)U16(offset));

    public uint U32(int offset)
    {
        Check(offset, 4);
        return BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset));
    }

    public int I32(int offset) => unchecked((int)U32(offset));

    public float F2(int offset) => I16(offset) / 16384f;

    public float Fixed(int offset) => I32(offset) / 65536f;

    public int Offset32(int parent, int field, bool nullable = false)
        => Offset(parent, U32(field), nullable);

    public int Offset24(int parent, int field)
    {
        Check(field, 3);
        uint value = ((uint)bytes[field] << 16) | ((uint)bytes[field + 1] << 8) | bytes[field + 2];
        return Offset(parent, value, nullable: false);
    }

    public void Check(int offset, int count)
    {
        if (offset < 0 || count < 0 || offset > bytes.Length - count)
        {
            throw new InvalidDataException("An OpenType color table offset or record lies outside its table.");
        }
    }

    private int Offset(int parent, uint offset, bool nullable)
    {
        if (offset == 0)
        {
            if (nullable)
            {
                return 0;
            }

            throw new InvalidDataException("A required OpenType color-table offset is null.");
        }

        long absolute = parent + (long)offset;
        if (absolute < 0 || absolute >= bytes.Length)
        {
            throw new InvalidDataException("An OpenType color-table offset lies outside its table.");
        }

        return (int)absolute;
    }
}
