using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Vulkan;

internal sealed class VulkanShaderDataBuffer<T> : IGraphicsShaderDataBuffer<T>, IShaderDataSource
    where T : struct, IShaderData
{
    private readonly ShaderValueSnapshot?[] _values;
    private bool _disposed;

    internal VulkanShaderDataBuffer(VulkanDevice owner, ShaderArtifact artifact, ulong count, MemoryPreference memory)
    {
        if (memory is not (MemoryPreference.Automatic or MemoryPreference.Upload))
        {
            throw new ArgumentException("Shader data supports GPU storage or upload staging only.", nameof(memory));
        }

        T empty = default;
        ShaderValueSnapshot codec = IShaderArguments.Capture(in empty);
        Layout = ShaderDataLayout.Data(artifact.GetTarget(owner.Caps.ShaderTarget), codec.ShaderTypeName);
        if (count == 0 || count > int.MaxValue || checked(count * (ulong)Layout.Size) > owner.Caps.MaxBufferSize)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        Memory = memory;
        Storage = owner.CreateBuffer<byte>(new() { Count = checked(count * (ulong)Layout.Size), Memory = memory, Usage = memory == MemoryPreference.Upload ? BufferUsage.CopySource : BufferUsage.CopyDestination | BufferUsage.ShaderRead });
        Owner = owner;
        Count = count;
        _values = new ShaderValueSnapshot?[checked((int)count)];
    }

    public ulong Count { get; }

    public ulong SizeInBytes => checked(Count * ShaderElementStrideInBytes);

    public ulong ShaderElementStrideInBytes => (ulong)Layout.Size;

    public IShaderDataLayout Layout { get; }

    public MemoryPreference Memory { get; }

    public bool IsMapped => Storage.IsMapped;

    public object ShaderHandle => ((IShaderRawBuffer)Storage).ShaderHandle;

    internal IGraphicsBuffer<byte> Storage { get; }

    internal VulkanDevice Owner { get; }

    public ValueTask MapAsync()
    {
        ValidateAlive();
        if (Memory != MemoryPreference.Upload)
        {
            throw new InvalidOperationException("Only upload staging can be mapped.");
        }

        return Storage.MapAsync();
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
        if (Memory != MemoryPreference.Upload || !IsMapped)
        {
            throw new InvalidOperationException("CopyFrom requires mapped upload staging.");
        }

        if (elementOffset > Count || (ulong)source.Length > Count - elementOffset)
        {
            throw new ArgumentOutOfRangeException(nameof(elementOffset));
        }

        if (source.IsEmpty)
        {
            return;
        }

        var snapshots = new ShaderValueSnapshot[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            snapshots[i] = IShaderArguments.Capture(in source[i]);
        }

        byte[] bytes = new byte[checked(source.Length * Layout.Size)];
        for (int i = 0; i < snapshots.Length; i++)
        {
            Layout.Pack(snapshots[i], PackReference).CopyTo(bytes, i * Layout.Size);
        }

        Storage.Slice(checked(elementOffset * (ulong)Layout.Size), (ulong)bytes.Length).CopyFrom(bytes);
        snapshots.CopyTo(_values, checked((int)elementOffset));
    }

    public ShaderDataSlice<T> SliceElements(ulong offset, ulong count)
    {
        ValidateAlive();
        return new(this, offset, count);
    }

    public ShaderValueSnapshot Read(ulong index)
    {
        ValidateAlive();
        if (index >= Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return _values[checked((int)index)] ?? throw new InvalidOperationException("Shader data element has not been set by the CPU.");
    }

    public void SetTransferredValue(ulong index, ShaderValueSnapshot value)
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

        Storage.Dispose();
        Array.Clear(_values);
        _disposed = true;
    }

    private byte[] PackReference(ShaderValue value, string kind) => VulkanShaderReferenceEncoding.Pack(Owner, value, kind);
}
