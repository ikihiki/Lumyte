namespace Lumyte.Graphics.Native;

/// <summary>A caller-owned backing allocation. Creating a heap does not create a linear resource.</summary>
public abstract class NativeGpuHeap
{
    /// <summary>Initializes allocation metadata for a backend-defined heap.</summary>
    protected NativeGpuHeap(ulong size, ulong alignment, NativeGpuMemoryKind kind)
    {
        Size = size;
        Alignment = alignment;
        Kind = kind;
    }

    public ulong Size { get; }
    public ulong Alignment { get; }
    public NativeGpuMemoryKind Kind { get; }
}
