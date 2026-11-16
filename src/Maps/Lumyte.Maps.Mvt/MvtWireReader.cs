using ProtoBuf;

namespace Lumyte.Maps.Mvt;

// Policy adapter only: all wire operations belong to protobuf-net.Core.
internal ref struct MvtWireReader
{
    private readonly ReadOnlyMemory<byte> _input;
    private readonly DecodeBudget _budget;
    private ProtoReader.State _state;

    internal MvtWireReader(ReadOnlyMemory<byte> input, DecodeBudget budget)
    {
        budget.Charge(1024, false);
        _budget = budget;
        _input = input;
        _state = ProtoReader.State.Create(input, model: null);
    }

    internal readonly bool End => _state.GetPosition() == _input.Length;

    internal void Dispose() => _state.Dispose();

    internal void Reset()
    {
        _state.Dispose();
        _budget.Charge(1024, false);
        _state = ProtoReader.State.Create(_input, model: null);
    }

    internal int Tag()
    {
        int field = _state.ReadFieldHeader();
        if (field <= 0 || _state.WireType is not (WireType.Varint or WireType.Fixed64 or WireType.String or WireType.Fixed32))
        {
            throw new InvalidDataException("Invalid field or unsupported group/wire type.");
        }

        return (field << 3) | (int)_state.WireType;
    }

    internal ulong Varint() => _state.ReadUInt64();

    // ReadUInt32 permits signed ten-byte encodings; MVT uint32 must not truncate.
    internal uint UInt32() => checked((uint)_state.ReadUInt64());

    internal long Int64() => _state.ReadInt64();

    internal long SignedInt64()
    {
        _state.Hint(WireType.SignedVarint);
        return _state.ReadInt64();
    }

    internal float Single() => _state.ReadSingle();

    internal double Double() => _state.ReadDouble();

    internal void Skip(int wire)
    {
        if (wire is not (0 or 1 or 2 or 5))
        {
            throw new InvalidDataException("Unsupported wire type.");
        }

        _state.SkipField();
        CheckPosition();
    }

    // Scan before exposing a borrowed, independently bounded message view. EndSubItem
    // is essential: StartSubItem does not check the declared length against input,
    // and primitive/header reads can cross a subitem boundary before it detects it.
    internal ReadOnlyMemory<byte> Message()
    {
        SubItemToken token = _state.StartSubItem();
        long start = _state.GetPosition();
        int field;
        while ((field = _state.ReadFieldHeader()) != 0)
        {
            if (field < 0 || _state.WireType is not (WireType.Varint or WireType.Fixed64 or WireType.String or WireType.Fixed32))
            {
                throw new InvalidDataException("Unsupported group/wire type.");
            }

            _state.SkipField();
            CheckPosition();
        }

        _state.EndSubItem(token);
        CheckPosition();
        return _input.Slice(checked((int)start), checked((int)(_state.GetPosition() - start)));
    }

    internal ReadOnlySpan<byte> StringBytes(DecodeBudget budget)
    {
        byte[] scratch = budget.Scratch;
        _state.ReadBytes(scratch, out int length);
        CheckPosition();
        return scratch.AsSpan(0, length);
    }

    internal int PackedCount()
    {
        SubItemToken token = _state.StartSubItem();
        int count = 0;
        while (_state.HasSubValue(WireType.Varint))
        {
            UInt32();
            count = checked(count + 1);
            CheckPosition();
        }

        _state.EndSubItem(token);
        CheckPosition();
        return count;
    }

    internal void Packed(uint[] target, ref int index)
    {
        SubItemToken token = _state.StartSubItem();
        while (_state.HasSubValue(WireType.Varint))
        {
            target[index++] = UInt32();
            CheckPosition();
        }

        _state.EndSubItem(token);
        CheckPosition();
    }

    private readonly void CheckPosition()
    {
        if (_state.GetPosition() > _input.Length)
        {
            throw new InvalidDataException("Truncated protobuf input.");
        }
    }
}
