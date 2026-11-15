using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal ref struct BrowserShaderValueWriter : IShaderValueWriter
{
    private readonly BrowserDevice _owner;
    private readonly int _size;
    private readonly BrowserRootStorage? _root;
    private readonly JSObject? _upload;
    private readonly int _baseOffset;
    private readonly int _expectedCount;
    private BrowserArgumentTable? _table;
    private int _position;
    private int _referenceCount;
    private bool _written;
    private bool _completed;

    internal BrowserShaderValueWriter(BrowserDevice owner, int size, BrowserRootStorage? root, JSObject? upload, int baseOffset, int expectedCount)
    {
        this = default;
        ValidateExpectedCount(size, expectedCount);
        _owner = owner;
        _size = size;
        _root = root;
        _upload = upload;
        _baseOffset = baseOffset;
        _expectedCount = expectedCount;
    }

    internal BrowserArgumentTable? Table => _table;

    public void WriteValue(scoped ReadOnlySpan<byte> value)
    {
        ValidateWrite(value.Length);
        if (_root != null)
        {
            _root.Write(value, _position);
        }
        else
        {
            BrowserInterop.CopyMappedUploadRange(_upload!, MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference(value), value.Length), checked(_baseOffset + _position));
        }

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

        if (reference is not GpuReference<T> dependency)
        {
            throw new ArgumentException("A shader reference must originate from an argument table.", nameof(reference));
        }

        if (dependency.Table is not BrowserArgumentTable table || !table.BelongsTo(_owner))
        {
            throw new ArgumentException("Argument table belongs to another device.", nameof(reference));
        }

        table.ThrowIfDisposed();

        if (_table != null && !ReferenceEquals(_table, table))
        {
            throw new ArgumentException("Shader references must use one argument table.", nameof(reference));
        }

        _table = table;
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

        _referenceCount++;
    }

    internal static void ValidateExpectedCount(int size, int expectedCount)
    {
        if (size < 0 || expectedCount < 0 || expectedCount > size / 16)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedCount));
        }
    }

    internal void Complete()
    {
        if (!_written || _completed || _position != _size || _referenceCount != _expectedCount)
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
