using System.Buffers.Binary;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Vulkan;

internal unsafe ref struct VulkanShaderValueWriter : IShaderValueWriter
{
    private readonly VulkanDevice _owner;
    private readonly int _size;
    private readonly Span<byte> _destination;
    private readonly int _expectedCount;
    private VulkanArgumentTable? _table;
    private int _position;
    private int _referenceCount;
    private bool _written;
    private bool _completed;

    internal VulkanShaderValueWriter(VulkanDevice owner, int size, Span<byte> destination, int expectedCount)
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
        _expectedCount = expectedCount;
    }

    internal VulkanArgumentTable? Table => _table;

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

        if (reference is not GpuReference<T> dependency)
        {
            throw new ArgumentException("A shader reference must originate from an argument table.", nameof(reference));
        }

        if (dependency.Table is not VulkanArgumentTable table || !table.BelongsTo(_owner))
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
        if (dependency.Resource is IGraphicsTextureView or IGraphicsSampler)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(wire, dependency.Slot);
        }
        else
        {
            IVulkanShaderBuffer buffer = dependency.Resource switch
            {
                IShaderDataSource source when source.Memory == MemoryPreference.Automatic && dependency.BufferAccess == ShaderBufferAccess.ReadOnly && source is IVulkanShaderBuffer data => data,
                IShaderRawBuffer raw when raw is IVulkanShaderBuffer data => data,
                _ => throw new ArgumentException("A buffer reference requires GPU storage."),
            };
            ulong address = buffer.ShaderAddress;
            BinaryPrimitives.WriteUInt64LittleEndian(wire, checked(address + dependency.OffsetInBytes));
        }

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
