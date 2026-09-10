using Lumyte.Graphics.Native;

using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;

namespace Lumyte.Graphics.DirectX12;

public sealed unsafe partial class DirectX12Backend
{
    public NativeGpuLinearRegion CreateLinearRegion(ulong size, NativeGpuHeap heap, ulong offset)
    {
        VerifyNotDisposed();
        HeapRecord backing = RequireHeap(heap);
        ResourceDesc description = LinearDescription(size, heap.Kind);
        ResourceStates initialState = heap.Kind switch
        {
            NativeGpuMemoryKind.CpuVisible => ResourceStates.GenericRead,
            NativeGpuMemoryKind.GpuOnly => ResourceStates.Common,
            NativeGpuMemoryKind.Readback => ResourceStates.CopyDest,
            _ => throw new ArgumentOutOfRangeException(nameof(heap)),
        };
        ComPtr<ID3D12Resource> resource = default;
        bool mapped = false;
        try
        {
            Check(device.CreatePlacedResource<ID3D12Heap, ID3D12Resource>(
                backing.Heap, offset, in description, initialState, null, out resource),
                "ID3D12Device.CreatePlacedResource");
            void* cpuAddress = null;
            if (heap.Kind != NativeGpuMemoryKind.GpuOnly)
            {
                var readRange = heap.Kind == NativeGpuMemoryKind.Readback
                    ? new Silk.NET.Direct3D12.Range(0, checked((nuint)size))
                    : new Silk.NET.Direct3D12.Range(0, 0);
                Check(resource.Map(0, &readRange, &cpuAddress), "ID3D12Resource.Map");
                mapped = true;
            }

            ulong gpuAddress = resource.GetGPUVirtualAddress();
            return new LinearRecord(this, resource, mapped, heap, offset, size, gpuAddress, (nint)cpuAddress);
        }
        catch
        {
            if (mapped) { resource.Unmap(0, (Silk.NET.Direct3D12.Range*)null); }
            resource.Dispose();
            throw;
        }
    }

    public void DestroyLinearRegion(NativeGpuLinearRegion region)
    {
        VerifyNotDisposed();
        ArgumentNullException.ThrowIfNull(region);
        if (region is not LinearRecord record || !ReferenceEquals(record.Owner, this))
        {
            throw new ArgumentException("The linear region belongs to another device.", nameof(region));
        }
        ObjectDisposedException.ThrowIf(record.Disposed, region);
        record.Disposed = true;
        if (record.Mapped)
        {
            var noWrites = new Silk.NET.Direct3D12.Range(0, 0);
            record.Resource.Unmap(0, region.Heap.Kind == NativeGpuMemoryKind.Readback ? &noWrites : null);
        }
        record.Resource.Dispose();
    }

    private static ResourceDesc LinearDescription(ulong size, NativeGpuMemoryKind kind)
    {
        ResourceFlags flags = kind switch
        {
            NativeGpuMemoryKind.GpuOnly => ResourceFlags.AllowUnorderedAccess,
            NativeGpuMemoryKind.CpuVisible or NativeGpuMemoryKind.Readback => ResourceFlags.None,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        return new(ResourceDimension.Buffer, 0, size, 1, 1, 1, Format.FormatUnknown,
            new SampleDesc(1, 0), TextureLayout.LayoutRowMajor, flags);
    }

    private sealed class LinearRecord(DirectX12Backend owner, ComPtr<ID3D12Resource> resource, bool mapped,
        NativeGpuHeap heap, ulong heapOffset, ulong size, ulong gpuAddress, nint cpuAddress)
        : NativeGpuLinearRegion(heap, heapOffset, size, gpuAddress, cpuAddress)
    {
        public DirectX12Backend Owner { get; } = owner;
        public ComPtr<ID3D12Resource> Resource = resource;
        public bool Mapped { get; } = mapped;
        public bool Disposed;
    }
}
