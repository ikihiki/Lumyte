using System.Buffers.Binary;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

internal ref struct Direct3D12ShaderValueWriter : IShaderValueWriter
{
    private readonly Direct3D12Device _owner;
    private readonly int _size;
    private readonly Span<byte> _destination;
    private Direct3D12ArgumentTable? _table;
    private int _position;
    private bool _written;
    private bool _completed;

    internal Direct3D12ShaderValueWriter(Direct3D12Device owner, int size, Span<byte> destination)
    {
        this = default;
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        if (destination.Length < size)
        {
            throw new ArgumentException("Destination is smaller than the C# wire size.", nameof(destination));
        }

        _owner = owner;
        _size = size;
        _destination = destination;
    }

    internal Direct3D12ArgumentTable? Table => _table;

    public void WriteValue(scoped ReadOnlySpan<byte> value)
    {
        ValidateWrite(value.Length);
        value.CopyTo(_destination.Slice(_position, value.Length));

        _position += value.Length;
        _written = true;
    }

    public void WriteReference<T>(IGpuRef<T>? reference)
    {
        ValidateWrite(16);
        if (reference is not GpuReference<T> dependency)
        {
            throw new ArgumentException("A shader reference must originate from an argument table.", nameof(reference));
        }

        if (dependency.Table is not Direct3D12ArgumentTable table || !table.BelongsTo(_owner))
        {
            throw new ArgumentException("Argument table belongs to another device.", nameof(reference));
        }

        table.ThrowIfDisposed();
        if (_table != null && !ReferenceEquals(_table, table))
        {
            throw new ArgumentException("Shader references must use one argument table.", nameof(reference));
        }

        _owner.RequireReady();
        uint offset = checked((uint)dependency.OffsetInBytes);
        uint slot = dependency.Slot;
        uint count = checked((uint)dependency.Count);
        Span<byte> wire = _destination.Slice(_position, 16);
        BinaryPrimitives.WriteUInt32LittleEndian(wire, slot);
        BinaryPrimitives.WriteUInt32LittleEndian(wire[4..], offset);
        BinaryPrimitives.WriteUInt32LittleEndian(wire[8..], count);
        BinaryPrimitives.WriteUInt32LittleEndian(wire[12..], 0);

        _table = table;
        _position += 16;
        _written = true;
    }

    internal void Complete()
    {
        if (!_written || _completed || _position != _size)
        {
            throw new InvalidOperationException("The codec must write exactly the complete C# wire value.");
        }

        _completed = true;
    }

    private readonly void ValidateWrite(int length)
    {
        if (_completed)
        {
            throw new InvalidOperationException("The transfer is already complete.");
        }

        if (length < 0 || _size < 0 || _position > _size || length > _size - _position)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "The value exceeds the C# wire size.");
        }
    }
}
