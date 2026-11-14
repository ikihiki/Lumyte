using System.Buffers.Binary;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal struct BrowserShaderValueWriter : IShaderValueWriter
{
    private readonly BrowserDevice _owner;
    private readonly int _size;
    private readonly IShaderRootStorage? _root;
    private readonly IGraphicsBuffer<byte>? _upload;
    private readonly ulong _baseOffset;
    private readonly ShaderDataLayout? _diagnostic;
    private List<IShaderReference>? _dependencies;
    private int _position;
    private int _referenceCount;
    private bool _written;
    private bool _completed;

    internal BrowserShaderValueWriter(BrowserDevice owner, int size, IShaderRootStorage? root, IGraphicsBuffer<byte>? upload, ulong baseOffset, ShaderDataLayout? diagnostic)
    {
        this = default;
        (_owner, _size, _root, _upload, _baseOffset, _diagnostic) = (owner, size, root, upload, baseOffset, diagnostic);
    }

    public void WriteValue(ReadOnlySpan<byte> value)
    {
        ValidateWrite(value.Length);
        if (_root != null)
        {
            _root.Write(value, _position);
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

        if (dependency.Table is not BrowserArgumentTable table || !table.BelongsTo(_owner))
        {
            throw new ArgumentException("Argument table belongs to another device.", nameof(reference));
        }

        table.ThrowIfDisposed();
        Span<byte> wire = stackalloc byte[16];
        wire.Clear();
        if (dependency.Resource is IShaderDataSource data && data.Memory != MemoryPreference.Automatic)
        {
            throw new ArgumentException("Upload staging cannot be referenced by a shader.", nameof(reference));
        }

        // WGSL helper ABI: stable slot, registration-relative byte offset, count, reserved zero.
        BinaryPrimitives.WriteUInt32LittleEndian(wire, dependency.Slot);
        BinaryPrimitives.WriteUInt32LittleEndian(wire[4..], checked((uint)(dependency.OffsetInBytes - dependency.RegistrationOffsetInBytes)));
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
        return BrowserReferenceMetadata.Create(_dependencies ?? (IEnumerable<IShaderReference>)Array.Empty<IShaderReference>());
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
