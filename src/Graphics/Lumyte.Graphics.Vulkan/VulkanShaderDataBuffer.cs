using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Vulkan;

internal sealed class VulkanShaderDataBuffer<T> : IGraphicsShaderDataBuffer<T>, IShaderDataSource, IValidatedShaderDataStorage, IVulkanShaderBuffer
    where T : struct, IShaderArguments
{
    private readonly IShaderReferenceMetadata?[] _values;
    private readonly ShaderDataLayout? _diagnosticLayout;
    private bool _disposed;

    internal VulkanShaderDataBuffer(VulkanDevice owner, ShaderArtifact artifact, ulong count, MemoryPreference memory)
    {
        if (memory is not (MemoryPreference.Automatic or MemoryPreference.Upload))
        {
            throw new ArgumentException("Shader data supports GPU storage or upload staging only.", nameof(memory));
        }

        Layout = ShaderDataLayout.Data<T>(artifact, owner.Caps.ShaderTarget);
        _diagnosticLayout = owner.Validation == null ? null : ShaderDataLayout.Data(artifact, owner.Caps.ShaderTarget, T.ShaderTypeName);
        if (count == 0 || count > int.MaxValue || checked(count * (ulong)Layout.Size) > owner.Caps.MaxBufferSize)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        Memory = memory;
        Storage = owner.CreateBuffer<byte>(new() { Count = checked(count * (ulong)Layout.Size), Memory = memory, Usage = memory == MemoryPreference.Upload ? BufferUsage.CopySource : BufferUsage.CopyDestination | BufferUsage.ShaderRead });
        Owner = owner;
        Count = count;
        _values = new IShaderReferenceMetadata?[checked((int)count)];
    }

    object IValidatedShaderDataStorage.ValidationStorage => Storage;

    public ulong Count { get; }

    public ulong SizeInBytes => checked(Count * ShaderElementStrideInBytes);

    public ulong ShaderElementStrideInBytes => (ulong)Layout.Size;

    public IShaderDataLayout Layout { get; }

    public MemoryPreference Memory { get; }

    public bool IsMapped => Storage.IsMapped;

    ulong IVulkanShaderBuffer.ShaderAddress
    {
        get
        {
            ValidateAlive();
            return ((IVulkanShaderBuffer)Storage).ShaderAddress;
        }
    }

    public object ShaderHandle => ((IShaderRawBuffer)Storage).ShaderHandle;

    internal IGraphicsBuffer<byte> Storage { get; }

    internal VulkanDevice Owner { get; }

    public ValueTask MapAsync(CancellationToken cancellationToken = default)
    {
        ValidateAlive();
        if (Memory != MemoryPreference.Upload)
        {
            throw new InvalidOperationException("Only upload staging can be mapped.");
        }

        return Storage.MapAsync(cancellationToken);
    }

    public void Unmap()
    {
        ValidateAlive();
        if (Memory != MemoryPreference.Upload)
        {
            throw new InvalidOperationException("Only upload staging can be unmapped.");
        }

        Storage.Unmap();
    }

    public void CopyFrom(ReadOnlySpan<T> source, ulong elementOffset = 0)
    {
        ValidateAlive();
        if (source.IsEmpty)
        {
            if (Memory != MemoryPreference.Upload || !IsMapped)
            {
                throw new InvalidOperationException("CopyFrom requires mapped upload staging.");
            }

            if (elementOffset > Count)
            {
                throw new ArgumentOutOfRangeException(nameof(elementOffset));
            }

            return;
        }

        VulkanShaderTransfer.CopyFrom(Owner, source, Storage, elementOffset, _values, _diagnosticLayout);
    }

    public ShaderDataSlice<T> SliceElements(ulong offset, ulong count)
    {
        ValidateAlive();
        return new(this, offset, count);
    }

    public IShaderReferenceMetadata ReadReferences(ulong index)
    {
        ValidateAlive();
        if (index >= Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return _values[checked((int)index)] ?? throw new InvalidOperationException("Shader data reference metadata is uninitialized or invalid after a failed transfer.");
    }

    public void SetTransferredReferences(ulong index, IShaderReferenceMetadata value)
    {
        ValidateAlive();
        if (Memory != MemoryPreference.Automatic || index >= Count)
        {
            throw new InvalidOperationException("Invalid shader data transfer destination.");
        }

        _values[checked((int)index)] = value;
    }

    public void ValidateAlive() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Owner.Validation?.ValidateRelease(Storage);
        Owner.Validation?.ValidateRelease(this);

        Storage.Dispose();
        Owner.Validation?.Release(this);
        Array.Clear(_values);
        _disposed = true;
    }
}
