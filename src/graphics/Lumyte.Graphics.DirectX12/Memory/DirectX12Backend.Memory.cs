using Lumyte.Graphics.Native;

using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;

namespace Lumyte.Graphics.DirectX12;

public sealed unsafe partial class DirectX12Backend
{
    public NativeGpuMemoryRequirements GetLinearMemoryRequirements(ulong size, NativeGpuMemoryKind kind)
    {
        VerifyAvailable();
        ResourceDesc description = LinearDescription(size, kind);
        ResourceAllocationInfo requirements = device.GetResourceAllocationInfo(0, 1, in description);
        if (requirements.SizeInBytes == ulong.MaxValue || requirements.Alignment == 0)
        {
            throw new InvalidOperationException("Direct3D 12 rejected the linear resource description.");
        }

        ulong alignment = requirements.Alignment;
        ulong reservation = checked((requirements.SizeInBytes + alignment - 1) / alignment * alignment);
        var compatibility = new HeapCompatibility(this, kind, HeapFlags.AllowOnlyBuffers);
        return new(reservation, alignment, compatibility);
    }

    public NativeGpuHeap CreateGpuHeap(
        ulong size,
        ulong alignment,
        NativeGpuMemoryKind kind,
        ReadOnlySpan<NativeGpuMemoryCompatibility> compatibilities)
    {
        VerifyAvailable();
        if (compatibilities.IsEmpty)
        {
            throw new ArgumentException("At least one allocation requirement is required.", nameof(compatibilities));
        }

        HeapFlags flags = HeapFlags.AllowOnlyBuffers
            | HeapFlags.AllowOnlyNonRTDSTextures | HeapFlags.AllowOnlyRTDSTextures;
        foreach (NativeGpuMemoryCompatibility compatibility in compatibilities)
        {
            if (compatibility is not HeapCompatibility native || !ReferenceEquals(native.Owner, this)
                || native.Kind != kind)
            {
                throw new ArgumentException(
                    "Allocation requirements must belong to this device and memory kind.", nameof(compatibilities));
            }
            flags &= native.Flags;
        }

        var description = new HeapDesc(size, new HeapProperties(HeapTypeFor(kind)), alignment, flags);
        ComPtr<ID3D12Heap> nativeHeap = default;
        try
        {
            Check(device.CreateHeap<ID3D12Heap>(in description, out nativeHeap), "ID3D12Device.CreateHeap");
            return new HeapRecord(this, nativeHeap, size, alignment, kind);
        }
        catch
        {
            nativeHeap.Dispose();
            throw;
        }
    }

    public void DestroyGpuHeap(NativeGpuHeap heap)
    {
        VerifyNotDisposed();
        HeapRecord record = RequireHeap(heap);
        record.Disposed = true;
        record.Heap.Dispose();
    }

    private HeapRecord RequireHeap(NativeGpuHeap heap)
    {
        ArgumentNullException.ThrowIfNull(heap);
        if (heap is not HeapRecord record || !ReferenceEquals(record.Owner, this))
        {
            throw new ArgumentException("The heap belongs to another device.", nameof(heap));
        }
        ObjectDisposedException.ThrowIf(record.Disposed, heap);
        return record;
    }

    private static HeapType HeapTypeFor(NativeGpuMemoryKind kind) => kind switch
    {
        NativeGpuMemoryKind.CpuVisible => HeapType.Upload,
        NativeGpuMemoryKind.GpuOnly => HeapType.Default,
        NativeGpuMemoryKind.Readback => HeapType.Readback,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private sealed class HeapCompatibility(DirectX12Backend owner, NativeGpuMemoryKind kind, HeapFlags flags)
        : NativeGpuMemoryCompatibility
    {
        public DirectX12Backend Owner { get; } = owner;
        public NativeGpuMemoryKind Kind { get; } = kind;
        public HeapFlags Flags { get; } = flags;
    }

    private sealed class HeapRecord(DirectX12Backend owner, ComPtr<ID3D12Heap> heap,
        ulong size, ulong alignment, NativeGpuMemoryKind kind) : NativeGpuHeap(size, alignment, kind)
    {
        public DirectX12Backend Owner { get; } = owner;
        public ComPtr<ID3D12Heap> Heap = heap;
        public bool Disposed;
    }
}
