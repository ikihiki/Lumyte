using System.Buffers.Binary;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

internal ref struct Direct3D12ShaderValueWriter : IShaderValueWriter
{
    private readonly Direct3D12Device _owner;
    private readonly int _size;
    private readonly Span<byte> _root;
    private readonly IGraphicsBuffer<byte>? _upload;
    private readonly ulong _baseOffset;
    private readonly ShaderDataLayout? _diagnostic;
    private List<IShaderReference>? _dependencies;
    private int _position;
    private int _referenceCount;
    private bool _written;
    private bool _completed;

    internal Direct3D12ShaderValueWriter(Direct3D12Device owner, int size, Span<byte> root, IGraphicsBuffer<byte>? upload, ulong baseOffset, ShaderDataLayout? diagnostic)
    {
        this = default;
        _owner = owner;
        _size = size;
        _root = root;
        _upload = upload;
        _baseOffset = baseOffset;
        _diagnostic = diagnostic;
    }

    public void WriteValue(scoped ReadOnlySpan<byte> value)
    {
        ValidateWrite(value.Length);
        if (_upload == null)
        {
            value.CopyTo(_root.Slice(_position));
        }
        else
        {
            _upload!.CopyBytesFrom(value, checked(_baseOffset + (ulong)_position), (ulong)value.Length);
        }

        _position += value.Length;
        _written = true;
    }

    public void WriteReference<T>(IGpuRef<T>? reference)
    {
        ValidateWrite(16);
        _diagnostic?.ValidateReference(typeof(T), reference, _position);
        if (reference is not GpuReference<T> dependency)
        {
            throw new ArgumentException("A shader reference must originate from an argument table.", nameof(reference));
        }

        if (dependency.Table is not Direct3D12ArgumentTable table || !table.BelongsTo(_owner))
        {
            throw new ArgumentException("Argument table belongs to another device.", nameof(reference));
        }

        table.ThrowIfDisposed();
        Span<byte> wire = stackalloc byte[16];
        wire.Clear();
        _owner.RequireReady();
        BinaryPrimitives.WriteUInt32LittleEndian(wire, dependency.Slot);
        BinaryPrimitives.WriteUInt32LittleEndian(wire[4..], checked((uint)dependency.OffsetInBytes));
        BinaryPrimitives.WriteUInt32LittleEndian(wire[8..], checked((uint)dependency.Count));
        WriteValue(wire);
        (_dependencies ??= []).Add(dependency);
        _referenceCount++;
    }

    internal IShaderReferenceMetadata Complete()
    {
        if (!_written || _completed || _position != _size)
        {
            throw new InvalidOperationException("The codec must write exactly the complete C# wire value.");
        }

        _completed = true;
        _diagnostic?.ValidateReferenceCount(_referenceCount);
        return Direct3D12ReferenceMetadata.Create(_dependencies ?? (IEnumerable<IShaderReference>)Array.Empty<IShaderReference>());
    }

    private readonly void ValidateWrite(int length)
    {
        if (_completed)
        {
            throw new InvalidOperationException("The transfer is already complete.");
        }

        if (_size < 0 || _position > _size || length > _size - _position)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "The value exceeds the C# wire size.");
        }
    }
}
