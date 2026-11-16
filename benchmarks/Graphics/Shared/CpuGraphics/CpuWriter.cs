using System.Buffers.Binary;

using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Benchmarks.Testing;

// Same bulk-wire/fixup protocol as production backends; resource IDs replace native addresses.
internal ref struct CpuWriter : IShaderValueWriter
{
    private readonly CpuGraphicsDevice _owner;

    private readonly Span<byte> _destination;

    private int _position;

    private bool _completed;

    private CpuTable? _table;

    internal CpuWriter(CpuGraphicsDevice owner, Span<byte> destination)
    {
        this = default;
        _owner = owner;
        _destination = destination;
    }

    internal CpuTable? Table => _table;

    public void WriteValue(ReadOnlySpan<byte> value)
    {
        Validate(value.Length);
        value.CopyTo(_destination[_position..]);
        _owner.Consume(value);
        _position += value.Length;
    }

    public void WriteReference<T>(IGpuRef<T>? value)
    {
        Validate(16);
        if (value is not CpuRef<T> reference)
        {
            throw new ArgumentException("Reference must originate in this device.");
        }

        reference.Validate();
        if (reference.Table is not CpuTable t || t.Owner != _owner || (_table != null && _table != t))
        {
            throw new ArgumentException("References must use one same-device _table.");
        }

        _table = t;
        Span<byte> bytes = _destination.Slice(_position, 16);
        ulong id = _owner.Own(reference.Resource).Id;
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, checked((id << 32) + reference.OffsetInBytes));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[8..], checked((uint)reference.Count));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[12..], reference.Slot);
        _owner.Consume(bytes);
        _position += 16;
    }

    internal void Complete()
    {
        if (_completed || _position != _destination.Length || _position == 0)
        {
            throw new InvalidOperationException("Codec must write exactly its full C# wire size.");
        }

        _completed = true;
    }

    private void Validate(int length)
    {
        if (_completed || length > _destination.Length - _position)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }
    }
}
