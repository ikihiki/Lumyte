using System.Buffers.Binary;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Vulkan;

internal unsafe struct VulkanShaderValueWriter : IShaderValueWriter
{
    private readonly VulkanDevice _owner;
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

    internal VulkanShaderValueWriter(VulkanDevice owner, int size, IShaderRootStorage? root, IGraphicsBuffer<byte>? upload, ulong baseOffset, ShaderDataLayout? diagnostic)
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

        if (dependency.Table is not VulkanArgumentTable table || !table.BelongsTo(_owner))
        {
            throw new ArgumentException("Argument table belongs to another device.", nameof(reference));
        }

        table.ThrowIfDisposed();
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
            var info = new Silk.NET.Vulkan.BufferDeviceAddressInfo { SType = Silk.NET.Vulkan.StructureType.BufferDeviceAddressInfo, Buffer = buffer.ShaderBuffer };
            ulong address = _owner.Api.GetBufferDeviceAddress(_owner.NativeDevice, &info);
            BinaryPrimitives.WriteUInt64LittleEndian(wire, checked(address + dependency.OffsetInBytes));
        }

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
        return VulkanReferenceMetadata.Create(_dependencies ?? (IEnumerable<IShaderReference>)Array.Empty<IShaderReference>());
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
