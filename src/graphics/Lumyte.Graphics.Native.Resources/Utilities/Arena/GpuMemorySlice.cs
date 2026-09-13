namespace Lumyte.Graphics.Native.Resources;

/// <summary>One arena loan of reserved bytes. It does not own a placed resource or the backing heap.</summary>
/// <remarks>Destroy placed resources and end all references before returning this loan to its arena.</remarks>
public sealed class GpuMemorySlice
{
    internal GpuMemorySlice(GpuMemoryArena owner, NativeGpuHeap heap, ulong offset, ulong size)
    {
        Owner = owner;
        Heap = heap;
        Offset = offset;
        Size = size;
    }

    internal GpuMemoryArena Owner { get; }
    public NativeGpuHeap Heap { get; }
    public ulong Offset { get; }
    public ulong Size { get; }
}
