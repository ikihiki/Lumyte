using System.Buffers.Binary;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

internal ref struct Direct3D12ShaderValueWriter : IShaderValueWriter
{
    private readonly Direct3D12Device _owner;
    private readonly int _size;
    private readonly Span<byte> _destination;
    private readonly ShaderDataLayout? _diagnostic;
    private readonly int _expectedCount;
    private readonly HashSet<IShaderReference>? _collector;
    private IShaderReference[]? _dependencies;
    private Direct3D12ArgumentTable? _table;
    private int _position;
    private int _referenceCount;
    private bool _written;
    private bool _completed;

    internal Direct3D12ShaderValueWriter(Direct3D12Device owner, int size, Span<byte> destination, int expectedCount, ShaderDataLayout? diagnostic, bool collect = false, HashSet<IShaderReference>? collector = null)
    {
        this = default;
        ValidateExpectedCount(size, expectedCount);
        if (destination.Length < size)
        {
            throw new ArgumentException("Destination is smaller than the C# wire size.", nameof(destination));
        }

        _owner = owner;
        _size = size;
        _destination = destination;
        _diagnostic = diagnostic;
        _expectedCount = expectedCount;
        _collector = collector;
        _dependencies = collect ? (expectedCount == 0 ? Array.Empty<IShaderReference>() : new IShaderReference[expectedCount]) : null;
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
        if (_referenceCount >= _expectedCount)
        {
            throw new InvalidOperationException("The codec exceeds its declared reference count.");
        }

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
        dependency.ValidateRegistration();
        if (_table != null && !ReferenceEquals(_table, table))
        {
            throw new ArgumentException("Shader references must use one argument table.", nameof(reference));
        }

        _table = table;
        Span<byte> wire = stackalloc byte[16];
        wire.Clear();
        _owner.RequireReady();
        BinaryPrimitives.WriteUInt32LittleEndian(wire, dependency.Slot);
        BinaryPrimitives.WriteUInt32LittleEndian(wire[4..], checked((uint)dependency.OffsetInBytes));
        BinaryPrimitives.WriteUInt32LittleEndian(wire[8..], checked((uint)dependency.Count));
        WriteValue(wire);
        if (_dependencies != null)
        {
            _dependencies[_referenceCount] = dependency;
        }

        _collector?.Add(dependency);
        _referenceCount++;
    }

    internal static void ValidateExpectedCount(int size, int expectedCount)
    {
        if (size < 0 || expectedCount < 0 || expectedCount > size / 16)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedCount));
        }
    }

    internal IShaderReferenceMetadata? Complete()
    {
        if (!_written || _completed || _position != _size || _referenceCount != _expectedCount)
        {
            throw new InvalidOperationException("The codec must write exactly the complete C# wire value.");
        }

        _completed = true;
        _diagnostic?.ValidateReferenceCount(_referenceCount);
        IShaderReference[]? dependencies = _dependencies;
        _dependencies = null;
        return dependencies == null ? null : Direct3D12ReferenceMetadata.TakeOwnership(dependencies);
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
