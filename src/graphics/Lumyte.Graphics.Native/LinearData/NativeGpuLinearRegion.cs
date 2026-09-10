namespace Lumyte.Graphics.Native;

/// <summary>A separately owned linear resource placed in a borrowed heap.</summary>
public abstract class NativeGpuLinearRegion
{
    /// <summary>Initializes region metadata for a backend-defined linear resource.</summary>
    protected NativeGpuLinearRegion(
        NativeGpuHeap heap,
        ulong heapOffset,
        ulong size,
        ulong gpuAddress,
        nint cpuAddress)
    {
        ArgumentNullException.ThrowIfNull(heap);
        Heap = heap;
        HeapOffset = heapOffset;
        Size = size;
        GpuAddress = gpuAddress;
        CpuAddress = cpuAddress;
    }

    public NativeGpuHeap Heap { get; }
    public ulong HeapOffset { get; }
    public ulong Size { get; }
    public ulong GpuAddress { get; }
    /// <summary>A mapping of this region, or zero. The caller must synchronize CPU and GPU access.</summary>
    public nint CpuAddress { get; }
}
