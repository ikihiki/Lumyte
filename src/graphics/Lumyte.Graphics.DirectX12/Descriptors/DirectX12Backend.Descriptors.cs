using Lumyte.Graphics.Native;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;

namespace Lumyte.Graphics.DirectX12;

public sealed unsafe partial class DirectX12Backend
{
    public NativeGpuDescriptorHeap CreateDescriptorHeap(NativeGpuDescriptorHeapKind kind, uint capacity)
    {
        VerifyAvailable();
        DescriptorHeapType type = DescriptorType(kind);
        var description = new DescriptorHeapDesc(type, capacity, DescriptorHeapFlags.ShaderVisible);
        ComPtr<ID3D12DescriptorHeap> heap = default;
        try
        {
            Check(device.CreateDescriptorHeap(in description, out heap), "CreateDescriptorHeap");
            return new DescriptorRecord(this, kind, capacity, heap, device.GetDescriptorHandleIncrementSize(type));
        }
        catch { heap.Dispose(); throw; }
    }

    public void DestroyDescriptorHeap(NativeGpuDescriptorHeap heap)
    {
        VerifyNotDisposed();
        DescriptorRecord record = RequireDescriptorHeap(heap);
        record.Disposed = true;
        record.Heap.Dispose();
    }

    public void WriteTextureDescriptor(NativeGpuDescriptorHeap heap, uint index, NativeGpuTextureView view,
        NativeGpuTextureDescriptorType type = NativeGpuTextureDescriptorType.Sampled)
    {
        VerifyAvailable();
        DescriptorRecord destination = RequireDescriptorHeap(heap, NativeGpuDescriptorHeapKind.Resource);
        CpuDescriptorHandle handle = DescriptorSlot(destination, index);
        TextureRecord texture = RequireTexture(view.Texture);
        switch (type)
        {
            case NativeGpuTextureDescriptorType.Sampled:
                ShaderResourceViewDesc sampled = SampledTextureDescription(view, texture.Description.SampleCount);
                device.CreateShaderResourceView(texture.Resource, in sampled, handle);
                break;
            case NativeGpuTextureDescriptorType.Storage:
                UnorderedAccessViewDesc storage = StorageTextureDescription(view);
                device.CreateUnorderedAccessView(texture.Resource, null, in storage, handle);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(type));
        }
    }

    public void WriteBufferDescriptor(NativeGpuDescriptorHeap heap, uint index, NativeGpuRange range,
        NativeGpuBufferAccess access)
    {
        VerifyAvailable();
        DescriptorRecord destination = RequireDescriptorHeap(heap, NativeGpuDescriptorHeapKind.Resource);
        CpuDescriptorHandle handle = DescriptorSlot(destination, index);
        LinearRecord linear = RequireLinear(range.Region);
        switch (access)
        {
            case NativeGpuBufferAccess.ReadOnly:
                ShaderResourceViewDesc read = ReadBufferDescription(range.Offset, range.Size);
                device.CreateShaderResourceView(linear.Resource, in read, handle);
                break;
            case NativeGpuBufferAccess.ReadWrite:
                UnorderedAccessViewDesc write = WriteBufferDescription(range.Offset, range.Size);
                device.CreateUnorderedAccessView(linear.Resource, null, in write, handle);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(access));
        }
    }

    public void WriteSamplerDescriptor(NativeGpuDescriptorHeap heap, uint index, NativeGpuSamplerDescription description)
    {
        VerifyAvailable();
        DescriptorRecord destination = RequireDescriptorHeap(heap, NativeGpuDescriptorHeapKind.Sampler);
        CpuDescriptorHandle handle = DescriptorSlot(destination, index);
        SamplerDesc native = SamplerDescription(description);
        device.CreateSampler(in native, handle);
    }

    private DescriptorRecord RequireDescriptorHeap(NativeGpuDescriptorHeap heap, NativeGpuDescriptorHeapKind? kind = null)
    {
        ArgumentNullException.ThrowIfNull(heap);
        if (heap is not DescriptorRecord record || !ReferenceEquals(record.Owner, this))
        {
            throw new ArgumentException("The descriptor heap belongs to another device.", nameof(heap));
        }
        ObjectDisposedException.ThrowIf(record.Disposed, heap);
        if (kind.HasValue && record.Kind != kind.Value)
        {
            throw new ArgumentException("The descriptor heap has the wrong kind for this operation.", nameof(heap));
        }
        return record;
    }

    internal static DescriptorHeapType DescriptorType(NativeGpuDescriptorHeapKind kind) => kind switch
    {
        NativeGpuDescriptorHeapKind.Resource => DescriptorHeapType.CbvSrvUav,
        NativeGpuDescriptorHeapKind.Sampler => DescriptorHeapType.Sampler,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static CpuDescriptorHandle DescriptorSlot(DescriptorRecord heap, uint index)
        => DescriptorSlot(heap.Heap.GetCPUDescriptorHandleForHeapStart(), heap.Increment, heap.Capacity, index);

    internal static CpuDescriptorHandle DescriptorSlot(CpuDescriptorHandle start, uint increment, uint capacity, uint index)
    {
        // Native descriptor handles are opaque. Only the native increment advances a slot.
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, capacity);
        return new(checked(start.Ptr + checked((nuint)index * increment)));
    }

    private sealed class DescriptorRecord(DirectX12Backend owner, NativeGpuDescriptorHeapKind kind,
        uint capacity, ComPtr<ID3D12DescriptorHeap> heap, uint increment) : NativeGpuDescriptorHeap(kind, capacity)
    {
        public DirectX12Backend Owner { get; } = owner;
        public ComPtr<ID3D12DescriptorHeap> Heap = heap;
        public uint Increment { get; } = increment;
        public bool Disposed;
    }
}
